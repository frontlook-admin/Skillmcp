using System.ComponentModel;
using System.Text;
using System.Text.Json;
using SkillMcp.Models;
using SkillMcp.Services;
using ModelContextProtocol.Server;

namespace SkillMcp.Tools;

/// <summary>
/// MCP tools that implement the <em>map_project_skills</em> workflow:
/// given a project code and user hints, load skills from one or more repositories
/// and return a ranked list of the most relevant ones.
/// </summary>
[McpServerToolType]
public sealed class SkillMapperTools
{
    private readonly SkillMapperService _mapper;

    public SkillMapperTools(SkillMapperService mapper) => _mapper = mapper;

    // ────────────────────────────────────────────────────────────────────────
    // Tool: map_project_skills
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "map_project_skills")]
    [Description(
        "Maps a project to a ranked list of relevant GitHub Copilot skills. " +
        "Skills are loaded from one or more repositories and merged before ranking. " +
        "\n\n" +
        "REPOSITORY SOURCES are resolved in order:\n" +
        "  1. skillRepos parameter — JSON array: [{\"url\":\"...\",\"path\":\"...\",\"dictionary\":\"...\",\"label\":\"...\"}]\n" +
        "  2. SKILL_REPOS env var  — same JSON format (may be a JSON-array string or a native JSON value)\n" +
        "  3. Legacy fallback      — SKILL_PATH + SKILL_DICTIONARY env vars (single repo)\n" +
        "  4. Default              — ./skills in the current working directory\n" +
        "\n" +
        "When 'url' is specified and the local 'path' does not exist, the server will " +
        "automatically git-clone the repository before loading skills. " +
        "For each repo: if a dictionary Markdown file is present, its richer metadata " +
        "(name, description, categories) is used; otherwise SKILL.md files are scanned. " +
        "Returns a ranked list with scores, matched keywords, source repo, and categories.")]
    public async Task<string> MapProjectSkills(
        [Description(
            "Project code or short description used to derive keywords. " +
            "Examples: 'ERP-001', 'REST API for invoicing', 'SPA dashboard'.")]
        string projectCode,

        [Description(
            "Free-text suggestions from the user that describe the required capabilities. " +
            "Examples: 'complex schema design + reporting', 'async background jobs and unit tests'.")]
        string userSuggestions,

        [Description(
            "JSON array of skill repository sources. Each entry may have: " +
            "url (git clone URL, optional), path (local skills folder, optional), " +
            "dictionary (Markdown dictionary file, optional), label (display name, optional). " +
            "When 'url' is provided and 'path' does not exist locally, the repo is cloned automatically. " +
            "Example: [{\"url\":\"https://github.com/github/awesome-copilot\",\"path\":\"awesome-copilot/skills/\",\"dictionary\":\"awesome-copilot/docs/README.skills.md\",\"label\":\"awesome-copilot\"}," +
            "{\"url\":\"https://github.com/my-org/skill-library\",\"path\":\"skill-library/skills/\"}]. " +
            "When omitted, falls back to SKILL_REPOS env var, then SKILL_PATH, then ./skills.")]
        string? skillRepos = null,

        [Description(
            "Maximum number of ranked results to include in the output. " +
            "Pass 0 to return all matches. Defaults to 10.")]
        int topN = 10)
    {
        var repos = ResolveRepos(skillRepos);

        SkillMappingResult result;
        try
        {
            result = await _mapper.MapAsync(
                projectCode:     projectCode,
                userSuggestions: userSuggestions,
                repos:           repos,
                topN:            topN);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }

        return FormatResult(result);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Repo resolution — parameter → env → legacy fallback → default
    // ────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<SkillRepoSource> ResolveRepos(string? skillReposJson)
    {
        // 1. Explicit tool parameter
        var repos = TryParseReposJson(skillReposJson);
        if (repos is { Count: > 0 }) return repos;

        // 2. SKILL_REPOS env var (JSON)
        repos = TryParseReposJson(Environment.GetEnvironmentVariable("SKILL_REPOS"));
        if (repos is { Count: > 0 }) return repos;

        // 3. Legacy single-repo env vars (backward compat)
        var legacyPath = Environment.GetEnvironmentVariable("SKILL_PATH");
        var legacyDict = Environment.GetEnvironmentVariable("SKILL_DICTIONARY");
        if (!string.IsNullOrWhiteSpace(legacyPath))
            return [new SkillRepoSource(legacyPath, legacyDict)];

        // 4. Default: ./skills
        return [new SkillRepoSource(Path.Combine(Directory.GetCurrentDirectory(), "skills"))];
    }

    private static IReadOnlyList<SkillRepoSource>? TryParseReposJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dtos = JsonSerializer.Deserialize<List<SkillRepoDto>>(json, opts);
            if (dtos is null || dtos.Count == 0) return null;

            return dtos
                .Where(d => !string.IsNullOrWhiteSpace(d.Path) || !string.IsNullOrWhiteSpace(d.Url))
                .Select(d => new SkillRepoSource(
                    Path:       d.Path ?? DerivePathFromUrl(d.Url!),
                    Dictionary: string.IsNullOrWhiteSpace(d.Dictionary) ? null : d.Dictionary,
                    Label:      d.Label,
                    Url:        d.Url))
                .ToList();
        }
        catch
        {
            // Treat the raw string as a plain path (single repo, no dictionary)
            return [new SkillRepoSource(json.Trim())];
        }
    }

    /// <summary>
    /// Derives a relative local path from a git URL when no explicit path is provided.
    /// e.g. https://github.com/github/awesome-copilot → awesome-copilot/skills/
    /// </summary>
    private static string DerivePathFromUrl(string url)
    {
        var segment = url.TrimEnd('/').Split('/').Last();
        if (segment.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            segment = segment[..^4];
        return string.IsNullOrWhiteSpace(segment)
            ? "skills"
            : Path.Combine(segment, "skills") + Path.DirectorySeparatorChar;
    }

    // DTO for JSON deserialization of repo entries
    private sealed class SkillRepoDto
    {
        public string? Url        { get; set; }
        public string? Path       { get; set; }
        public string? Dictionary { get; set; }
        public string? Label      { get; set; }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Formatting
    // ────────────────────────────────────────────────────────────────────────

    private static string FormatResult(SkillMappingResult r)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Project        : {r.ProjectCode}");
        sb.AppendLine($"Suggestions    : {r.UserSuggestions}");

        if (r.SourceNotes.Count == 1)
            sb.AppendLine($"Source         : {r.SourceNotes[0]}");
        else
        {
            sb.AppendLine($"Sources ({r.SourceNotes.Count} repos):");
            foreach (var note in r.SourceNotes)
                sb.AppendLine($"  • {note}");
        }

        sb.AppendLine();

        if (r.RankedSkills.Count == 0)
        {
            sb.AppendLine("No matching skills found for the given project and suggestions.");
            sb.AppendLine("Try broadening the user suggestions or ensure the skills path is correct.");
            return sb.ToString();
        }

        sb.AppendLine($"## Ranked Skills ({r.RankedSkills.Count} results)");
        sb.AppendLine();

        int rank = 1;
        foreach (var skill in r.RankedSkills)
        {
            sb.Append($"{rank,2}. **{skill.Name}**");

            if (skill.Score > 0)
                sb.Append($"  (score: {skill.Score})");

            if (!string.IsNullOrWhiteSpace(skill.RepoLabel))
                sb.Append($"  [{skill.RepoLabel}]");

            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(skill.Description))
                sb.AppendLine($"    {Truncate(skill.Description, 160)}");

            if (skill.Categories.Count > 0)
                sb.AppendLine($"    Categories : {string.Join(", ", skill.Categories)}");

            if (skill.MatchedKeywords.Count > 0)
                sb.AppendLine($"    Matched on : {string.Join(", ", skill.MatchedKeywords)}");

            sb.AppendLine();
            rank++;
        }

        if (r.AllCategories.Count > 0)
        {
            sb.AppendLine($"## Categories");
            sb.AppendLine(string.Join(", ", r.AllCategories));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";
}