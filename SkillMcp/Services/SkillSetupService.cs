using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SkillMcp.Models;
using Microsoft.Extensions.Logging;

namespace SkillMcp.Services;

/// <summary>
/// Resolves the awesome-copilot skill source repository, determines which skill
/// folders apply to a detected project type, and copies/refreshes them into
/// the target project's <c>skills/</c> directory.
///
/// Mirrors the logic in the embedded PowerShell/Bash scripts in SKILL.md.
/// </summary>
public sealed class SkillSetupService
{
    private readonly ILogger<SkillSetupService> _logger;

    // ── Skill map: project type → skill folder names ─────────────────────────
    private static readonly Dictionary<string, IReadOnlyList<string>> SkillMap = new()
    {
        [ProjectType.Blazor] = new[]
        {
            "fluentui-blazor", "aspnet-minimal-api-openapi", "ef-core", "dotnet-best-practices",
            "dotnet-design-pattern-review", "csharp-async", "csharp-docs", "csharp-xunit",
            "containerize-aspnetcore", "multi-stage-dockerfile", "refactor",
            "create-specification", "sql-optimization", "sql-code-review"
        },
        [ProjectType.AspNetCoreApi] = new[]
        {
            "aspnet-minimal-api-openapi", "ef-core", "dotnet-best-practices", "dotnet-design-pattern-review",
            "csharp-async", "csharp-docs", "csharp-xunit", "sql-optimization", "sql-code-review",
            "containerize-aspnetcore", "multi-stage-dockerfile", "openapi-to-application-code",
            "create-specification"
        },
        [ProjectType.MAUI] = new[]
        {
            "dotnet-best-practices", "dotnet-design-pattern-review", "csharp-async", "csharp-docs",
            "csharp-xunit", "dotnet-upgrade", "multi-stage-dockerfile", "refactor"
        },
        [ProjectType.WinForms] = new[]
        {
            "dotnet-best-practices", "dotnet-upgrade", "containerize-aspnet-framework",
            "dotnet-design-pattern-review", "refactor", "csharp-docs"
        },
        [ProjectType.Android] = new[]
        {
            "kotlin-springboot", "java-springboot", "java-docs", "java-junit",
            "java-refactoring-extract-method", "java-refactoring-remove-parameter",
            "java-add-graalvm-native-image-support"
        },
        [ProjectType.Frontend] = new[]
        {
            "create-web-form", "javascript-typescript-jest", "markdown-to-html",
            "multi-stage-dockerfile", "refactor"
        },
        [ProjectType.CppCMake] = new[]
        {
            "multi-stage-dockerfile", "refactor", "refactor-method-complexity-reduce", "sql-code-review"
        }
    };

    // Skills always included for every project type
    private static readonly string[] CommonSkills =
    {
        "git-commit", "conventional-commit", "create-readme",
        "folder-structure-blueprint-generator",
        "technology-stack-blueprint-generator",
        "context-map", "what-context-needed"
    };

    // Keywords used to auto-discover new skills added to the upstream repo
    private static readonly Dictionary<string, IReadOnlyList<string>> TypeKeywords = new()
    {
        [ProjectType.Blazor]        = new[] { "blazor", "razor", "fluentui" },
        [ProjectType.AspNetCoreApi] = new[] { "aspnet", "openapi", "minimal-api", "ef-core", "signalr", "grpc" },
        [ProjectType.MAUI]          = new[] { "maui", "dotnet-maui" },
        [ProjectType.WinForms]      = new[] { "winforms", "wpf", "windows-form", "aspnet-framework" },
        [ProjectType.Android]       = new[] { "android", "kotlin-", "graalvm", "java-" },
        [ProjectType.Frontend]      = new[] { "javascript", "typescript", "react", "vue", "angular", "vite", "css", "html", "web-form" },
        [ProjectType.CppCMake]      = new[] { "cpp", "cmake", "vcpkg", "conan" }
    };

    private static readonly string[] CommonKeywords =
    {
        "git-commit", "conventional-commit", "create-readme", "folder-structure",
        "technology-stack", "context-map", "what-context", "refactor", "multi-stage"
    };

    // Well-known locations where awesome-copilot might be cloned locally
    private static readonly string[] CandidatePaths =
    {
        @"G:\Repos\frontlook-admin\AI_HELPERS\awesome-copilot",
        @"G:\Repos\frontlook-admin\awesome-copilot",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repos", "awesome-copilot"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "awesome-copilot"),
        @"C:\src\awesome-copilot"
    };

    /// <summary>
    /// Default repository to clone when awesome-copilot is not found locally.
    /// Can be overridden via the AWESOME_COPILOT_REPO_URL environment variable
    /// or the <c>skillRepoUrl</c> parameter on <see cref="SetupAsync"/>.
    /// </summary>
    public const string DefaultRepoUrl = "https://github.com/github/awesome-copilot";

    public SkillSetupService(ILogger<SkillSetupService> logger) => _logger = logger;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Executes the skill setup for the given detection result.</summary>
    /// <param name="detection">Result from <see cref="ProjectTypeDetector.Detect"/>.</param>
    /// <param name="overwrite">When true, replaces existing skill folders.</param>
    /// <param name="checkOnly">When true, performs a dry-run with no file changes.</param>
    /// <param name="skillSourcePath">Optional override for the awesome-copilot repo path.</param>
    /// <param name="skillRepoUrl">
    /// Git URL to clone when the repo is not found locally.
    /// Falls back to the AWESOME_COPILOT_REPO_URL environment variable, then
    /// <see cref="DefaultRepoUrl"/> (<c>https://github.com/github/awesome-copilot</c>).
    /// </param>
    public async Task<SetupResult> SetupAsync(
        DetectionResult detection,
        bool overwrite = false,
        bool checkOnly = false,
        string? skillSourcePath = null,
        string? skillRepoUrl = null,
        CancellationToken cancellationToken = default)
    {
        var repoRoot = ResolveSkillRepo(skillSourcePath);

        // Resolve effective repo URL: explicit param → env var → default
        var effectiveRepoUrl = !string.IsNullOrWhiteSpace(skillRepoUrl)
            ? skillRepoUrl
            : Environment.GetEnvironmentVariable("AWESOME_COPILOT_REPO_URL") is { Length: > 0 } envUrl
                ? envUrl
                : DefaultRepoUrl;

        if (repoRoot is null)
        {
            _logger.LogWarning("Skill repo not found locally. Attempting auto-clone from {Url}", effectiveRepoUrl);
            repoRoot = await TryCloneRepoAsync(effectiveRepoUrl, cancellationToken);
        }

        if (repoRoot is null)
            throw new InvalidOperationException(
                $"awesome-copilot repository could not be found or cloned. " +
                $"Provide its local path via the skillSourcePath parameter. " +
                $"Searched: {string.Join(", ", CandidatePaths)}");

        var skillSourceDir = Path.Combine(repoRoot, "skills");
        if (!Directory.Exists(skillSourceDir))
            throw new DirectoryNotFoundException($"skills/ folder not found inside repo at {repoRoot}");

        _logger.LogInformation("Skill source: {Source}", skillSourceDir);

        // ── Build the merged skill list ─────────────────────────────────────
        var (typeSkills, commonSkills) = AutoDiscoverAndBuildSkillList(skillSourceDir, detection.DetectedTypes);

        var allSkills = typeSkills.Union(commonSkills, StringComparer.OrdinalIgnoreCase)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(s => s)
                                  .ToList();

        _logger.LogInformation("Skills selected: {Count}", allSkills.Count);

        // ── Diff against existing installation ──────────────────────────────
        var skillsDest = Path.Combine(detection.ProjectRoot, "skills");
        var alreadyInstalled = Directory.Exists(skillsDest)
            ? Directory.GetDirectories(skillsDest).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newSkills   = allSkills.Where(s => !alreadyInstalled.Contains(s)).ToList();
        var existSkills = allSkills.Where(s =>  alreadyInstalled.Contains(s)).ToList();
        var obsolete    = alreadyInstalled
                            .Where(s => s != "skills.json" && !allSkills.Contains(s, StringComparer.OrdinalIgnoreCase))
                            .OrderBy(s => s)
                            .ToList();

        var projectType = string.Join(", ", detection.DetectedTypes);

        if (checkOnly)
        {
            _logger.LogInformation("Dry-run mode: {New} new, {Exist} existing, {Obsolete} obsolete",
                newSkills.Count, existSkills.Count, obsolete.Count);

            return new SetupResult(
                DetectedProjectType: projectType,
                Added:     newSkills,
                Refreshed: [],
                Skipped:   existSkills,
                Missing:   [],
                Obsolete:  obsolete,
                IsDryRun:  true,
                ManifestPath: null,
                SettingsPath: null,
                TotalInstalled: alreadyInstalled.Count);
        }

        // ── Copy skills ─────────────────────────────────────────────────────
        if (!Directory.Exists(skillsDest))
            Directory.CreateDirectory(skillsDest);

        var added     = new List<string>();
        var refreshed = new List<string>();
        var skipped   = new List<string>();
        var missing   = new List<string>();

        foreach (var skill in allSkills)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var src  = Path.Combine(skillSourceDir, skill);
            var dest = Path.Combine(skillsDest, skill);

            if (!Directory.Exists(src)) { missing.Add(skill); continue; }

            if (Directory.Exists(dest))
            {
                if (overwrite)
                {
                    Directory.Delete(dest, recursive: true);
                    CopyDirectory(src, dest);
                    refreshed.Add(skill);
                }
                else
                {
                    skipped.Add(skill);
                }
                continue;
            }

            CopyDirectory(src, dest);
            added.Add(skill);
        }

        // ── Write skills.json manifest ──────────────────────────────────────
        var installedDirs = Directory.GetDirectories(skillsDest)
                               .Select(Path.GetFileName)
                               .OfType<string>()
                               .OrderBy(s => s)
                               .ToList();

        var manifest = new
        {
            projectType,
            detectedAt  = DateTime.UtcNow.ToString("o"),
            sourcePath  = skillSourceDir,
            targetPath  = skillsDest,
            skills      = installedDirs.Select(name => new { name, path = $@".\skills\{name}" }).ToArray()
        };

        var jsonPath = Path.Combine(skillsDest, "skills.json");
        await File.WriteAllTextAsync(jsonPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        _logger.LogInformation("Manifest written to {Path}", jsonPath);

        // ── Update .vscode/settings.json ────────────────────────────────────
        var settingsPath = await UpdateVsCodeSettingsAsync(detection.ProjectRoot, cancellationToken);

        return new SetupResult(
            DetectedProjectType: projectType,
            Added:     added,
            Refreshed: refreshed,
            Skipped:   skipped,
            Missing:   missing,
            Obsolete:  obsolete,
            IsDryRun:  false,
            ManifestPath: jsonPath,
            SettingsPath: settingsPath,
            TotalInstalled: installedDirs.Count);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private (List<string> typeSkills, List<string> commonSkills) AutoDiscoverAndBuildSkillList(
        string skillSourceDir, IReadOnlyList<string> detectedTypes)
    {
        // Start with static maps
        var currentSkillMap    = SkillMap.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var currentCommonSkills = CommonSkills.ToList();
        var allMapped = currentSkillMap.Values.SelectMany(v => v).Concat(currentCommonSkills)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Auto-discover new skill folders in the repo that aren't in the static map
        if (Directory.Exists(skillSourceDir))
        {
            foreach (var dir in Directory.GetDirectories(skillSourceDir))
            {
                var skill = Path.GetFileName(dir);
                if (allMapped.Contains(skill)) continue;

                var sl = skill.ToLowerInvariant();
                var matchedCommon = CommonKeywords.Any(kw => sl.Contains(kw, StringComparison.OrdinalIgnoreCase));

                if (matchedCommon)
                {
                    currentCommonSkills.Add(skill);
                    _logger.LogDebug("Auto-discovered (common): {Skill}", skill);
                    continue;
                }

                foreach (var (type, keywords) in TypeKeywords)
                {
                    if (keywords.Any(kw => sl.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!currentSkillMap.ContainsKey(type))
                            currentSkillMap[type] = new List<string>();
                        currentSkillMap[type].Add(skill);
                        _logger.LogDebug("Auto-discovered: {Skill} → {Type}", skill, type);
                    }
                }
            }
        }

        // Union of all detected type skill lists
        var typeSkills = detectedTypes
            .SelectMany(t => currentSkillMap.TryGetValue(t, out var list) ? list : Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        return (typeSkills, currentCommonSkills.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList());
    }

    private static string? ResolveSkillRepo(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Directory.Exists(Path.Combine(overridePath, "skills")))
                return overridePath;
        }

        foreach (var candidate in CandidatePaths)
        {
            if (Directory.Exists(Path.Combine(candidate, "skills")))
                return candidate;
        }

        return null;
    }

    private async Task<string?> TryCloneRepoAsync(string repoUrl, CancellationToken cancellationToken)
    {
        // Find a good parent directory
        var candidates = new[]
        {
            @"G:\Repos\frontlook-admin\AI_HELPERS",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repos"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        var parent = candidates.FirstOrDefault(Directory.Exists)
                  ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var cloneDest = Path.Combine(parent, "awesome-copilot");
        _logger.LogInformation("Cloning {Url} into {Dest}", repoUrl, cloneDest);

        var psi = new ProcessStartInfo("git", $"clone --depth 1 \"{repoUrl}\" \"{cloneDest}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process.");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || !Directory.Exists(Path.Combine(cloneDest, "skills")))
        {
            _logger.LogError("git clone failed with exit code {Code}", process.ExitCode);
            return null;
        }

        _logger.LogInformation("Clone complete: {Dest}", cloneDest);
        return cloneDest;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private async Task<string> UpdateVsCodeSettingsAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var vscodeDir    = Path.Combine(projectRoot, ".vscode");
        var settingsPath = Path.Combine(vscodeDir, "settings.json");

        Directory.CreateDirectory(vscodeDir);

        Dictionary<string, object> settings;

        if (File.Exists(settingsPath))
        {
            try
            {
                var raw = await File.ReadAllTextAsync(settingsPath, cancellationToken);
                // Strip single-line comments to avoid JSON parse errors (VS Code allows them)
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"//[^\n]*", "");
                settings = JsonSerializer.Deserialize<Dictionary<string, object>>(raw)
                        ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse existing settings.json — will append only.");
                settings = new Dictionary<string, object>();
            }
        }
        else
        {
            settings = new Dictionary<string, object>();
        }

        // Ensure the promptFilesLocations key exists and contains our entry
        if (settings.TryGetValue("chat.promptFilesLocations", out var existingLocations)
            && existingLocations is JsonElement elem
            && elem.ValueKind == JsonValueKind.Object)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(elem.GetRawText())
                    ?? new Dictionary<string, object>();
            dict["${workspaceFolder}/skills"] = true;
            settings["chat.promptFilesLocations"] = dict;
        }
        else
        {
            settings["chat.promptFilesLocations"] = new Dictionary<string, object>
            {
                ["${workspaceFolder}/skills"] = true
            };
        }

        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        _logger.LogInformation("VS Code settings updated: {Path}", settingsPath);
        return settingsPath;
    }
}
