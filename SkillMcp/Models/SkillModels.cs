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
    int TotalInstalled);
