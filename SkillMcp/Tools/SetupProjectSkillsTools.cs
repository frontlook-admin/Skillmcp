using System.ComponentModel;
using System.Text;
using SkillMcp.Models;
using SkillMcp.Services;
using ModelContextProtocol.Server;

namespace SkillMcp.Tools;

/// <summary>
/// MCP tools that implement the setup-project-skills skill:
/// detecting the technology stack of a project and installing / refreshing
/// matching GitHub Copilot skill folders.
/// </summary>
[McpServerToolType]
public sealed class SetupProjectSkillsTools
{
    private readonly ProjectTypeDetector _detector;
    private readonly SkillSetupService   _service;

    public SetupProjectSkillsTools(ProjectTypeDetector detector, SkillSetupService service)
    {
        _detector = detector;
        _service  = service;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tool 1: detect_project_type
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "detect_project_type")]
    [Description(
        "Scans a project directory and detects its technology stack " +
        "(Blazor, AspNetCoreApi, MAUI, WinForms, Android, Frontend, CppCMake, or Unknown). " +
        "Multiple types are possible (e.g. a Blazor project also returns Frontend). " +
        "Makes NO changes to the file system. " +
        "Use this before setup_project_skills to preview what will be detected.")]
    public string DetectProjectType(
        [Description("Absolute path to the project root directory. Defaults to current directory if omitted.")]
        string? targetProject = null)
    {
        var root = ResolveProject(targetProject);

        DetectionResult result;
        try { result = _detector.Detect(root); }
        catch (Exception ex) { return $"ERROR: {ex.Message}"; }

        var sb = new StringBuilder();
        sb.AppendLine($"Project root   : {root}");
        sb.AppendLine($"Detected types : {string.Join(", ", result.DetectedTypes)}");
        sb.AppendLine();
        sb.AppendLine("Use setup_project_skills to install matching skill folders.");
        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tool 2: check_project_skills  (dry-run)
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "check_project_skills")]
    [Description(
        "Dry-run: detects the project type, resolves the awesome-copilot skill source, " +
        "and reports which skills WOULD be added, are already installed, or are obsolete — " +
        "without making any file-system changes. " +
        "Use this to preview before running setup_project_skills or refresh_project_skills.")]
    public async Task<string> CheckProjectSkillsAsync(
        [Description("Absolute path to the project root directory. Defaults to current directory if omitted.")]
        string? targetProject = null,
        [Description("Optional path to a locally cloned awesome-copilot repository. " +
                     "If omitted the tool searches well-known locations and auto-clones if needed.")]
        string? skillSourcePath = null,
        [Description("Git URL of the awesome-copilot repository to clone when not found locally. " +
                     "Falls back to the AWESOME_COPILOT_REPO_URL environment variable, then " +
                     $"the built-in default ({SkillSetupService.DefaultRepoUrl}).")]
        string? skillRepoUrl = null,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveProject(targetProject);

        DetectionResult detection;
        try { detection = _detector.Detect(root); }
        catch (Exception ex) { return $"ERROR detecting project type: {ex.Message}"; }

        SetupResult result;
        try
        {
            result = await _service.SetupAsync(
                detection, overwrite: false, checkOnly: true,
                skillSourcePath: skillSourcePath,
                skillRepoUrl: skillRepoUrl,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) { return $"ERROR: {ex.Message}"; }

        return FormatCheckResult(result);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tool 3: setup_project_skills  (incremental — default recommended mode)
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "setup_project_skills")]
    [Description(
        "Detects the project's technology stack, resolves or auto-clones the awesome-copilot " +
        "skill source repository, and copies NEW skill folders into <projectRoot>/skills/. " +
        "Existing skill folders are left untouched (incremental / additive mode). " +
        "Also writes skills/skills.json and updates .vscode/settings.json so Copilot discovers the skills. " +
        "Run check_project_skills first for a dry-run preview.")]
    public async Task<string> SetupProjectSkillsAsync(
        [Description("Absolute path to the project root directory. Defaults to current directory if omitted.")]
        string? targetProject = null,
        [Description("Optional path to a locally cloned awesome-copilot repository. " +
                     "If omitted the tool searches well-known locations and auto-clones if needed.")]
        string? skillSourcePath = null,
        [Description("Git URL of the awesome-copilot repository to clone when not found locally. " +
                     "Falls back to the AWESOME_COPILOT_REPO_URL environment variable, then " +
                     $"the built-in default ({SkillSetupService.DefaultRepoUrl}).")]
        string? skillRepoUrl = null,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveProject(targetProject);

        DetectionResult detection;
        try { detection = _detector.Detect(root); }
        catch (Exception ex) { return $"ERROR detecting project type: {ex.Message}"; }

        SetupResult result;
        try
        {
            result = await _service.SetupAsync(
                detection, overwrite: false, checkOnly: false,
                skillSourcePath: skillSourcePath,
                skillRepoUrl: skillRepoUrl,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) { return $"ERROR: {ex.Message}"; }

        return FormatSetupResult(result);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tool 4: refresh_project_skills  (full overwrite)
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "refresh_project_skills")]
    [Description(
        "Full refresh: detects the project type, then REPLACES ALL skill folders in <projectRoot>/skills/ " +
        "with the latest versions from the awesome-copilot source repository. " +
        "Use this when new upstream skills have been published and you want the latest content. " +
        "Equivalent to setup_project_skills with -Overwrite / --overwrite.")]
    public async Task<string> RefreshProjectSkillsAsync(
        [Description("Absolute path to the project root directory. Defaults to current directory if omitted.")]
        string? targetProject = null,
        [Description("Optional path to a locally cloned awesome-copilot repository. " +
                     "If omitted the tool searches well-known locations and auto-clones if needed.")]
        string? skillSourcePath = null,
        [Description("Git URL of the awesome-copilot repository to clone when not found locally. " +
                     "Falls back to the AWESOME_COPILOT_REPO_URL environment variable, then " +
                     $"the built-in default ({SkillSetupService.DefaultRepoUrl}).")]
        string? skillRepoUrl = null,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveProject(targetProject);

        DetectionResult detection;
        try { detection = _detector.Detect(root); }
        catch (Exception ex) { return $"ERROR detecting project type: {ex.Message}"; }

        SetupResult result;
        try
        {
            result = await _service.SetupAsync(
                detection, overwrite: true, checkOnly: false,
                skillSourcePath: skillSourcePath,
                skillRepoUrl: skillRepoUrl,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) { return $"ERROR: {ex.Message}"; }

        return FormatSetupResult(result);
    }

    // ── Formatting helpers ──────────────────────────────────────────────────

    private static string ResolveProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Directory.GetCurrentDirectory();

        // Normalize to forward slashes and collapse any accidental double-slashes
        // that can appear when callers pass already-escaped Windows paths.
        static string Normalize(string p) =>
            System.Text.RegularExpressions.Regex.Replace(p.Replace('\\', '/'), "/{2,}", "/");

        // When running inside Docker, translate Windows drive paths to the
        // container-mounted equivalents (e.g. G:\Repos\foo  ->  /g/Repos/foo).
        // The mcp.json mounts G:\Repos at /g/Repos in the container.
        if (path.Length >= 2 && path[1] == ':')
        {
            var driveLetter = char.ToLower(path[0]);
            var rest = Normalize(path.Substring(2)).TrimStart('/');
            return $"/{driveLetter}/{rest}";
        }

        return Normalize(path);
    }

    private static string FormatCheckResult(SetupResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Detected project type : {r.DetectedProjectType}");
        sb.AppendLine($"Mode                  : DRY-RUN (no changes made)");
        sb.AppendLine();

        if (r.Added.Count > 0)
        {
            sb.AppendLine($"📦 New skills that WOULD be added ({r.Added.Count}):");
            foreach (var s in r.Added) sb.AppendLine($"   + {s}");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("✅ All skills are already installed. Nothing to add.");
        }

        if (r.Skipped.Count > 0)
        {
            sb.AppendLine($"Already installed ({r.Skipped.Count}): {string.Join(", ", r.Skipped.Take(10))}" +
                          (r.Skipped.Count > 10 ? $" … (+{r.Skipped.Count - 10} more)" : ""));
            sb.AppendLine();
        }

        if (r.Obsolete.Count > 0)
        {
            sb.AppendLine($"🗑  Skills in ./skills/ not in current map ({r.Obsolete.Count}):");
            foreach (var s in r.Obsolete) sb.AppendLine($"   ~ {s}");
            sb.AppendLine();
        }

        sb.AppendLine("Run setup_project_skills to apply changes, or refresh_project_skills to overwrite all.");
        return sb.ToString();
    }

    private static string FormatSetupResult(SetupResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Detected project type : {r.DetectedProjectType}");
        sb.AppendLine();

        if (r.Added.Count > 0)
        {
            sb.AppendLine($"✅ Added ({r.Added.Count}):");
            foreach (var s in r.Added) sb.AppendLine($"   + {s}");
            sb.AppendLine();
        }

        if (r.Refreshed.Count > 0)
        {
            sb.AppendLine($"🔄 Refreshed ({r.Refreshed.Count}):");
            foreach (var s in r.Refreshed) sb.AppendLine($"   ↺ {s}");
            sb.AppendLine();
        }

        if (r.Skipped.Count > 0)
            sb.AppendLine($"⏭  Up-to-date (skipped) : {r.Skipped.Count} skills");

        if (r.Missing.Count > 0)
        {
            sb.AppendLine($"⚠️  Not found in source  : {string.Join(", ", r.Missing)}");
            sb.AppendLine();
        }

        if (r.Obsolete.Count > 0)
        {
            sb.AppendLine($"🗑  Skills not in current map ({r.Obsolete.Count}):");
            foreach (var s in r.Obsolete) sb.AppendLine($"   ~ {s}");
            sb.AppendLine();
        }

        sb.AppendLine($"Total installed  : {r.TotalInstalled}  |  " +
                      $"Added: {r.Added.Count}  |  " +
                      $"Refreshed: {r.Refreshed.Count}  |  " +
                      $"Up-to-date: {r.Skipped.Count}");

        if (r.ManifestPath is not null)
            sb.AppendLine($"Manifest         : {r.ManifestPath}");

        if (r.SettingsPath is not null)
            sb.AppendLine($"VS Code settings : {r.SettingsPath}  (chat.promptFilesLocations updated)");

        return sb.ToString();
    }
}
