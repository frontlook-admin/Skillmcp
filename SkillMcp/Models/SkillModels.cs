namespace SkillMcp.Models;

/// <summary>
/// A single skill repository source — a URL to clone and the sub-folder inside
/// that repo that contains the individual skill directories.
/// </summary>
/// <param name="Url">Git URL to clone, e.g. https://github.com/github/awesome-copilot</param>
/// <param name="Folder">Sub-folder inside the repo that contains skill directories. Defaults to "skills".</param>
public sealed record SkillSource(string Url, string Folder = "skills");

/// <summary>Recognised project-type identifiers.</summary>
public static class ProjectType
{
    public const string Blazor        = "Blazor";
    public const string AspNetCoreApi = "AspNetCoreApi";
    public const string MAUI          = "MAUI";
    public const string WinForms      = "WinForms";
    public const string Android       = "Android";
    public const string Frontend      = "Frontend";
    public const string CppCMake      = "CppCMake";
    public const string Unknown       = "Unknown";
}

/// <summary>Result of a detection pass over a project directory.</summary>
public sealed record DetectionResult(
    IReadOnlyList<string> DetectedTypes,
    string ProjectRoot);

/// <summary>Outcome of a skill-setup operation.</summary>
public sealed record SetupResult(
    string DetectedProjectType,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Refreshed,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Obsolete,
    bool IsDryRun,
    string? ManifestPath,
    string? SettingsPath,
    string? InstructionsPath,
    int TotalInstalled);

// ── Skill Mapper models ───────────────────────────────────────────────────────

/// <summary>
/// Identifies a single skill repository source to load skills from.
/// Multiple sources can be provided; skills are merged and deduplicated.
/// </summary>
/// <param name="Path">
/// Local filesystem path to the skills folder (e.g. "awesome-copilot/skills/").
/// </param>
/// <param name="Dictionary">
/// Optional path to a Markdown dictionary file with enriched metadata.
/// </param>
/// <param name="Label">
/// Human-readable label used in output (e.g. the repo name or URL).
/// </param>
/// <param name="Url">
/// Git clone URL. When <paramref name="Path"/> does not exist on disk the service
/// will attempt to clone from this URL before loading skills.
/// Example: https://github.com/github/awesome-copilot
/// </param>
public sealed record SkillRepoSource(string Path, string? Dictionary = null, string? Label = null, string? Url = null);

/// <summary>
/// Metadata for a single skill loaded from a skills repository.
/// Populated either from a parsed dictionary file or by scanning SKILL.md files.
/// </summary>
public sealed record SkillEntry(
    string Name,
    string Description,
    IReadOnlyList<string> Categories,
    string? SourcePath = null,
    string? RepoLabel = null);

/// <summary>
/// A skill that matched a mapping request, enriched with a relevance score.
/// </summary>
public sealed record RankedSkill(
    string Name,
    double Score,
    string Description,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> MatchedKeywords,
    string? RepoLabel = null);

/// <summary>Result of a <c>map_project_skills</c> invocation.</summary>
public sealed record SkillMappingResult(
    string ProjectCode,
    string UserSuggestions,
    IReadOnlyList<RankedSkill> RankedSkills,
    IReadOnlyList<string> AllCategories,
    bool UsedDictionary,
    IReadOnlyList<string> SourceNotes);
