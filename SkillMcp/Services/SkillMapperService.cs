using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkillMcp.Models;
using Microsoft.Extensions.Logging;

namespace SkillMcp.Services;

/// <summary>
/// Maps a project code and free-text user suggestions to a ranked list of
/// relevant skills from a local skills repository.
///
/// Two loading strategies are supported:
/// <list type="bullet">
///   <item><term>Dictionary</term><description>Parse a Markdown dictionary file
///   (YAML front-matter blocks per heading, or a Markdown table) for rich
///   metadata — name, description, categories.</description></item>
///   <item><term>Fallback scan</term><description>Walk <paramref name="SkillsPath"/>
///   looking for <c>SKILL.md</c> files and extract YAML front-matter from
///   each one.</description></item>
/// </list>
/// </summary>
public sealed class SkillMapperService
{
    private readonly ILogger<SkillMapperService> _logger;

    // Words ignored when building the keyword set
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "for", "with", "to", "in", "of",
        "on", "at", "is", "it", "by", "as", "be", "we", "use", "my",
        "our", "add", "new", "get", "set", "run", "do", "i", "me",
        "this", "that", "from", "into", "are", "was", "has", "have",
        "will", "can", "not", "but", "so", "if", "then", "all", "some",
        "code", "project", "skill", "skills"
    };

    public SkillMapperService(ILogger<SkillMapperService> logger) =>
        _logger = logger;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Perform a full skill-mapping pass across one or more skill repositories.
    /// Skills from all repos are merged; later repos take precedence on name collision.
    /// </summary>
    /// <param name="projectCode">Project identifier or short description (e.g. "ERP-001").</param>
    /// <param name="userSuggestions">Free-text hints from the user.</param>
    /// <param name="repos">
    /// One or more skill repo sources. Each may have an optional dictionary path and label.
    /// </param>
    /// <param name="topN">Maximum number of ranked results to return (0 = all).</param>
    public async Task<SkillMappingResult> MapAsync(
        string projectCode,
        string userSuggestions,
        IReadOnlyList<SkillRepoSource> repos,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        if (repos is null || repos.Count == 0)
            repos = [new SkillRepoSource(Path.Combine(Directory.GetCurrentDirectory(), "skills"))];

        // Auto-clone repos whose local path is missing but a URL is provided
        var resolvedRepos = new List<SkillRepoSource>(repos.Count);
        foreach (var repo in repos)
        {
            if (!Directory.Exists(repo.Path) && !string.IsNullOrWhiteSpace(repo.Url))
            {
                _logger.LogWarning("Path '{Path}' not found — attempting auto-clone from {Url}", repo.Path, repo.Url);
                var cloned = await TryAutoCloneAsync(repo.Url, repo.Path, cancellationToken);
                resolvedRepos.Add(cloned ? repo : repo); // path is now expected to exist
            }
            else
            {
                resolvedRepos.Add(repo);
            }
        }
        repos = resolvedRepos;

        // Merge skills from all repos — later repos overwrite earlier ones on name collision
        var merged     = new Dictionary<string, SkillEntry>(StringComparer.OrdinalIgnoreCase);
        var sourceNotes = new List<string>();
        bool anyDictionary = false;

        foreach (var repo in repos)
        {
            bool usedDict = !string.IsNullOrWhiteSpace(repo.Dictionary) &&
                            File.Exists(repo.Dictionary);

            List<SkillEntry> skills;
            string label = repo.Label ?? Path.GetFileName(repo.Path.TrimEnd('/', '\\')) ?? repo.Path;

            if (usedDict)
            {
                _logger.LogDebug("[{Label}] Loading from dictionary: {Dict}", label, repo.Dictionary);
                skills = LoadFromDictionary(repo.Dictionary!, repo.Path, label);
                anyDictionary = true;
                sourceNotes.Add($"{label}: dictionary ({Path.GetFileName(repo.Dictionary)})");
            }
            else
            {
                _logger.LogDebug("[{Label}] Fallback scan: {Path}", label, repo.Path);
                skills = LoadByScanning(repo.Path, label);
                sourceNotes.Add($"{label}: directory scan");
            }

            _logger.LogInformation("[{Label}] Loaded {Count} skills", label, skills.Count);

            foreach (var s in skills)
                merged[s.Name] = s;
        }

        _logger.LogInformation("Total merged skills: {Count} from {Repos} repo(s)",
            merged.Count, repos.Count);

        // Rank
        var keywords = ExtractKeywords(projectCode + " " + userSuggestions);
        _logger.LogDebug("Keywords: {Keywords}", string.Join(", ", keywords));

        var ranked = RankSkills(merged.Values, keywords);
        if (topN > 0) ranked = ranked.Take(topN).ToList();

        var allCategories = ranked
            .SelectMany(r => r.Categories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();


        return new SkillMappingResult(
            ProjectCode:     projectCode,
            UserSuggestions: userSuggestions,
            RankedSkills:    ranked,
            AllCategories:   allCategories,
            UsedDictionary:  anyDictionary,
            SourceNotes:     sourceNotes);
    }

    // ── Auto-clone helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Clones <paramref name="repoUrl"/> into the directory implied by <paramref name="skillsPath"/>
    /// (e.g. for path "awesome-copilot/skills/" the clone destination is "./awesome-copilot").
    /// Returns <c>true</c> if the clone succeeded or was already present.
    /// </summary>
    private async Task<bool> TryAutoCloneAsync(string repoUrl, string skillsPath, CancellationToken ct)
    {
        // Derive clone destination from the repo URL (last path segment without .git)
        var repoName = repoUrl.TrimEnd('/').Split('/').Last();
        if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repoName = repoName[..^4];
        if (string.IsNullOrWhiteSpace(repoName)) repoName = "skill-repo";

        var cloneDest = Path.Combine(Directory.GetCurrentDirectory(), repoName);

        if (Directory.Exists(cloneDest))
        {
            _logger.LogInformation("Skip clone: '{Dest}' already exists", cloneDest);
            return true;
        }

        _logger.LogInformation("Cloning {Url} into {Dest}", repoUrl, cloneDest);

        var psi = new ProcessStartInfo("git", $"clone --depth 1 \"{repoUrl}\" \"{cloneDest}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("git clone failed (exit {Code}) for {Url}", process.ExitCode, repoUrl);
            return false;
        }

        _logger.LogInformation("Clone complete: {Dest}", cloneDest);
        return true;
    }

    // ── Loading strategies ────────────────────────────────────────────────────

    /// <summary>
    /// Parse a Markdown dictionary file.  Two formats are supported:
    /// <list type="bullet">
    ///   <item>YAML front-matter blocks beneath each heading.</item>
    ///   <item>Markdown table with columns: Name | Description | Categories.</item>
    /// </list>
    /// Falls back to the directory scan for any skill whose SKILL.md is reachable
    /// from <paramref name="skillsPath"/> but is not listed in the dictionary.
    /// </summary>
    private List<SkillEntry> LoadFromDictionary(string dictionaryPath, string skillsPath, string repoLabel)
    {
        var entries = new List<SkillEntry>();
        var text = File.ReadAllText(dictionaryPath);

        // ── Strategy A: YAML front-matter code fences ─────────────────────
        // Looks for patterns like:
        //   ```yaml
        //   name: foo
        //   description: '...'
        //   categories: [A, B]
        //   ```
        var yamlFenceRx = new Regex(
            @"```(?:yaml|skill)\s*\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match m in yamlFenceRx.Matches(text))
        {
            var entry = ParseYamlFrontMatter(m.Groups[1].Value, skillsPath, repoLabel);
            if (entry is not null) entries.Add(entry);
        }

        if (entries.Count > 0)
        {
            _logger.LogDebug("Dictionary: parsed {N} entries via YAML fences", entries.Count);
            return entries;
        }

        // ── Strategy B: Markdown table ─────────────────────────────────────
        // | Name | Description | Categories |
        // |------|-------------|------------|
        // | ... | ... | A, B |
        var tableRowRx = new Regex(
            @"^\s*\|(.+?)\|(.+?)\|(.+?)\|\s*$",
            RegexOptions.Multiline);

        bool headerSkipped = false;
        foreach (Match m in tableRowRx.Matches(text))
        {
            // Skip separator row (e.g. |---|---|---|)
            if (Regex.IsMatch(m.Value, @"\|-+\|")) continue;

            var cols = m.Groups.Cast<Group>()
                        .Skip(1)
                        .Select(g => g.Value.Trim())
                        .ToArray();

            if (!headerSkipped) { headerSkipped = true; continue; }

            if (cols.Length < 2) continue;

            var name = cols[0].Trim('`', ' ');
            var desc = cols.Length > 1 ? cols[1] : string.Empty;
            var cats = cols.Length > 2
                ? cols[2].Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                         .Select(c => c.Trim())
                         .ToList()
                : (List<string>)[];

            if (!string.IsNullOrWhiteSpace(name))
                entries.Add(new SkillEntry(name, desc, cats,
                    ResolveSkillPath(name, skillsPath), repoLabel));
        }

        if (entries.Count > 0)
        {
            _logger.LogDebug("Dictionary: parsed {N} entries via Markdown table", entries.Count);
            return entries;
        }

        // ── Strategy C: Headings + description paragraph ───────────────────
        // ## skill-name
        // Short description text.
        var headingRx = new Regex(
            @"^#+\s+([a-z0-9][a-z0-9\-\.]+)\s*$(?:\r?\n)+([^\n#]+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        foreach (Match m in headingRx.Matches(text))
        {
            var name = m.Groups[1].Value.Trim();
            var desc = m.Groups[2].Value.Trim();
            entries.Add(new SkillEntry(name, desc, [],
                ResolveSkillPath(name, skillsPath), repoLabel));
        }

        _logger.LogDebug("Dictionary: parsed {N} entries via headings", entries.Count);

        // Merge with directory scan to pick up anything the dictionary missed
        var scanned = LoadByScanning(skillsPath, repoLabel);
        var known   = entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in scanned.Where(s => !known.Contains(s.Name)))
            entries.Add(s);

        return entries;
    }

    /// <summary>Scan <paramref name="skillsPath"/> for <c>SKILL.md</c> files.</summary>
    private List<SkillEntry> LoadByScanning(string skillsPath, string repoLabel)
    {
        var entries = new List<SkillEntry>();

        if (!Directory.Exists(skillsPath))
        {
            _logger.LogWarning("Skills path not found: {Path}", skillsPath);

            // Try to find skills.json next to the path as a last-resort
            var jsonFile = Path.Combine(skillsPath, "skills.json");
            if (File.Exists(jsonFile))
                return LoadFromSkillsJson(jsonFile, repoLabel);

            return entries;
        }

        // Check for a skills.json index first (faster than individual SKILL.md reads)
        var skillsJsonPath = Path.Combine(skillsPath, "skills.json");
        if (File.Exists(skillsJsonPath))
        {
            var jsonEntries = LoadFromSkillsJson(skillsJsonPath, repoLabel);
            // Enrich from SKILL.md front-matter where available
            foreach (var je in jsonEntries)
            {
                var enriched = TryEnrichFromSkillMd(je);
                entries.Add(enriched);
            }
            return entries;
        }

        // Walk directories, extracting YAML front-matter from each SKILL.md
        foreach (var dir in Directory.EnumerateDirectories(skillsPath))
        {
            var skillMdPath = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMdPath)) continue;

            var name    = Path.GetFileName(dir);
            var content = File.ReadAllText(skillMdPath);
            var entry   = ParseYamlFrontMatter(ExtractYamlBlock(content), skillsPath, repoLabel)
                          ?? new SkillEntry(name, InferDescriptionFromContent(content), [], dir, repoLabel);
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>Build minimal <see cref="SkillEntry"/> list from a skills.json index.</summary>
    private List<SkillEntry> LoadFromSkillsJson(string jsonPath, string repoLabel)
    {
        try
        {
            using var fs  = File.OpenRead(jsonPath);
            using var doc = JsonDocument.Parse(fs);
            var root      = doc.RootElement;

            if (!root.TryGetProperty("skills", out var skillsArr))
                return [];

            var entries = new List<SkillEntry>();
            foreach (var item in skillsArr.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(name))
                    entries.Add(new SkillEntry(name, string.Empty, [], path, repoLabel));
            }
            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse skills.json at {Path}", jsonPath);
            return [];
        }
    }

    // ── Enrichment helpers ────────────────────────────────────────────────────

    private static SkillEntry TryEnrichFromSkillMd(SkillEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SourcePath)) return entry;

        // Resolve actual directory path from relative ".\\skills\\name" notation
        var dir = entry.SourcePath.Replace("./", "").Replace(".\\", "");
        var skillMdPath = File.Exists(Path.Combine(dir, "SKILL.md"))
            ? Path.Combine(dir, "SKILL.md")
            : null;

        if (skillMdPath is null) return entry;

        try
        {
            var content = File.ReadAllText(skillMdPath);
            var enriched = ParseYamlFrontMatter(ExtractYamlBlock(content), dir);
            return enriched ?? entry;
        }
        catch { return entry; }
    }

    /// <summary>Extract the YAML front-matter block from a Markdown document.</summary>
    private static string ExtractYamlBlock(string markdown)
    {
        // Matches either ```skill or ```yaml fence, or the --- ... --- front-matter
        var fenceMatch = Regex.Match(markdown,
            @"```(?:skill|yaml)\s*\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fenceMatch.Success) return fenceMatch.Groups[1].Value;

        var fmMatch = Regex.Match(markdown, @"^---\r?\n(.*?)\r?\n---", RegexOptions.Singleline);
        return fmMatch.Success ? fmMatch.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Naively parse a YAML-like block to a <see cref="SkillEntry"/>.
    /// Uses hand-rolled parsing to avoid a full YAML dependency.
    /// </summary>
    private static SkillEntry? ParseYamlFrontMatter(string yaml, string basePath, string? repoLabel = null)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return null;

        string? name = null, description = null;
        var categories = new List<string>();

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                name = StripYamlValue(line["name:".Length..]);
            else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                description = StripYamlValue(line["description:".Length..]);
            else if (line.StartsWith("categories:", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("category:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = StripYamlValue(line[(line.IndexOf(':') + 1)..]);
                if (rest.StartsWith('['))
                {
                    // Inline list: [A, B, C]
                    categories.AddRange(
                        rest.Trim('[', ']').Split(',').Select(c => c.Trim().Trim('"', '\'')));
                }
                else if (!string.IsNullOrWhiteSpace(rest))
                {
                    categories.Add(rest);
                }
            }
            // YAML inline list item: "  - Database"
            else if (line.StartsWith("- ", StringComparison.Ordinal) &&
                     categories.Count == 0 &&
                     !line.Contains(':'))
            {
                categories.Add(line[2..].Trim().Trim('"', '\''));
            }
        }

        if (string.IsNullOrWhiteSpace(name)) return null;

        return new SkillEntry(name, description ?? string.Empty, categories,
            ResolveSkillPath(name, basePath), repoLabel);
    }

    private static string StripYamlValue(string s) =>
        s.Trim().Trim('\'', '"');

    private static string? ResolveSkillPath(string name, string basePath)
    {
        var candidate = Path.Combine(basePath, name);
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string InferDescriptionFromContent(string markdown)
    {
        // Skip the front-matter / code fence, then take the first non-empty non-heading line
        var lines = markdown.Split('\n');
        bool inFence = false;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("```")) { inFence = !inFence; continue; }
            if (inFence || t.StartsWith("---") || t.StartsWith("#") || string.IsNullOrWhiteSpace(t))
                continue;
            return t.Length > 200 ? t[..200] + "…" : t;
        }
        return string.Empty;
    }

    // ── Ranking ───────────────────────────────────────────────────────────────

    private static List<RankedSkill> RankSkills(IEnumerable<SkillEntry> skills, ISet<string> keywords)
    {
        if (!keywords.Any())
        {
            // No keywords — return everything in alphabetical order with score 0
            return skills
                .OrderBy(s => s.Name)
                .Select(s => new RankedSkill(s.Name, 0.0, s.Description, s.Categories, [], s.RepoLabel))
                .ToList();
        }

        var scored = new List<RankedSkill>();
        foreach (var skill in skills)
        {
            // Tokenise the skill's searchable text
            var skillTokens = TokenizeSkill(skill);
            var matched     = new List<string>();
            double score    = 0.0;

            foreach (var kw in keywords)
            {
                // Exact containment in skill name (highest weight)
                if (skill.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    score += 3.0;
                    matched.Add(kw);
                }
                // Containment in description
                else if (!string.IsNullOrWhiteSpace(skill.Description) &&
                         skill.Description.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    score += 2.0;
                    matched.Add(kw);
                }
                // Category match
                else if (skill.Categories.Any(c =>
                             c.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 2.5;
                    matched.Add(kw);
                }
                // Token-level partial match inside the tokenised set
                else if (skillTokens.Any(t => t.Contains(kw) || kw.Contains(t)))
                {
                    score += 1.0;
                    matched.Add(kw);
                }
            }

            if (score > 0)
                scored.Add(new RankedSkill(
                    skill.Name, Math.Round(score, 2), skill.Description,
                    skill.Categories, matched.Distinct().ToList(), skill.RepoLabel));
        }

        return scored
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Name)
            .ToList();
    }

    private static HashSet<string> TokenizeSkill(SkillEntry skill)
    {
        var combined = skill.Name + " " + skill.Description + " " +
                       string.Join(" ", skill.Categories);
        return [.. Tokenize(combined)];
    }

    // ── Keyword extraction ────────────────────────────────────────────────────

    public static ISet<string> ExtractKeywords(string input)
    {
        var tokens = Tokenize(input);
        return new HashSet<string>(
            tokens.Where(t => t.Length > 2 && !StopWords.Contains(t)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Tokenize(string text) =>
        Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+")
             .Where(t => !string.IsNullOrWhiteSpace(t));
}
