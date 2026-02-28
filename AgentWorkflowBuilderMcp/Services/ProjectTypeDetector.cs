using AgentWorkflowBuilderMcp.Models;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilderMcp.Services;

/// <summary>
/// Scans a project directory and infers its technology stack.
/// Mirrors the detection logic from the embedded PowerShell / Bash scripts in SKILL.md.
/// </summary>
public sealed class ProjectTypeDetector
{
    private readonly ILogger<ProjectTypeDetector> _logger;

    // Folders / path segments to exclude from file scanning
    private static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj"
    };

    public ProjectTypeDetector(ILogger<ProjectTypeDetector> logger)
        => _logger = logger;

    /// <summary>
    /// Detects all matching project types in <paramref name="projectRoot"/>.
    /// Multiple types are possible (e.g. a Blazor project also gets Frontend skills).
    /// </summary>
    public DetectionResult Detect(string projectRoot)
    {
        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"Project directory not found: {projectRoot}");

        _logger.LogDebug("Scanning project: {Root}", projectRoot);

        var allFiles = EnumerateFiles(projectRoot);

        var extensions = allFiles
            .Select(f => Path.GetExtension(f).ToLowerInvariant())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fileNames = allFiles
            .Select(f => Path.GetFileName(f).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Read up to 5 .csproj files to check for SDK / UseMaui
        var csprojContent = allFiles
            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .Select(f => TryReadFile(f))
            .Where(c => c is not null)
            .Select(c => c!)
            .Aggregate("", (a, b) => a + b);

        // ── Individual checks ──────────────────────────────────────────────────
        bool hasRazor      = extensions.Contains(".razor")
                          || fileNames.Contains("_imports.razor");
        bool hasCsproj     = extensions.Contains(".csproj");
        bool hasVb         = extensions.Contains(".vb");
        bool hasKotlin     = extensions.Contains(".kt");
        bool hasJava       = extensions.Contains(".java");
        bool hasAndroid    = fileNames.Contains("androidmanifest.xml");
        bool hasCpp        = extensions.Contains(".cpp")
                          || fileNames.Contains("cmakelists.txt")
                          || extensions.Contains(".vcxproj");
        bool hasTs         = extensions.Contains(".ts") || extensions.Contains(".tsx");
        bool hasPkgJson    = fileNames.Contains("package.json");
        bool hasProgramCs  = fileNames.Contains("program.cs");
        bool hasMaui       = csprojContent.Contains("Microsoft.NET.Sdk.Maui", StringComparison.OrdinalIgnoreCase)
                          || csprojContent.Contains("<UseMaui>true", StringComparison.OrdinalIgnoreCase);
        bool hasDesigner   = allFiles.Any(f =>
            f.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase));

        // ── Detect all matching project types (each if is independent) ─────────
        var detected = new List<string>();

        if (hasAndroid || hasKotlin || hasJava)                                     detected.Add(ProjectType.Android);
        if (hasCpp)                                                                  detected.Add(ProjectType.CppCMake);
        if (hasMaui)                                                                 detected.Add(ProjectType.MAUI);
        if (hasRazor && hasCsproj)                                                   detected.Add(ProjectType.Blazor);
        if (hasCsproj && hasProgramCs && (hasDesigner || hasVb))                     detected.Add(ProjectType.WinForms);

        // AspNetCoreApi only when no more-specific .NET type detected
        if (hasCsproj && !hasRazor && !hasMaui && !(hasDesigner || hasVb))
            detected.Add(ProjectType.AspNetCoreApi);

        if ((hasTs && hasPkgJson) || (hasPkgJson && !hasCsproj))
            detected.Add(ProjectType.Frontend);

        // Blazor is a web UI framework — always include Frontend skills
        if (detected.Contains(ProjectType.Blazor) && !detected.Contains(ProjectType.Frontend))
            detected.Add(ProjectType.Frontend);

        if (detected.Count == 0)
            detected.Add(ProjectType.Unknown);

        _logger.LogInformation("Detected types: {Types}", string.Join(", ", detected));
        return new DetectionResult(detected.AsReadOnly(), projectRoot);
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private static IReadOnlyList<string> EnumerateFiles(string root)
    {
        var result = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (ShouldExclude(file)) continue;
                result.Add(file);
            }
        }
        catch { /* ignore permission errors */ }
        return result;
    }

    private static bool ShouldExclude(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => ExcludedSegments.Contains(s));
    }

    private static string? TryReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return null; }
    }
}
