using System.Diagnostics;

namespace HRManagement.E2E;

/// <summary>
/// PLAN-41: what an evidence folder must say about its run, read once per test process. The job script passes the values it
/// already knows (<c>ACCEPTANCE_PROFILE</c>, <c>ACCEPTANCE_SOURCE_SHA</c>, <c>ACCEPTANCE_CODE_TREE</c>,
/// <c>ACCEPTANCE_COMPOSE_PROJECT</c>); a developer's run works them out here. The code tree always comes from
/// <c>scripts/ci/acceptance-code-tree.ps1</c>, so the recorder, the summarizer and the validator cannot disagree on it (P-2).
/// </summary>
public sealed record AcceptanceRunContext(
    string Profile,
    string? SourceSha,
    string? CodeTree,
    IReadOnlyDictionary<string, string> ImageRevisions,
    string Source,
    string? CiRunId,
    string? CiEvent)
{
    public const string RevisionLabel = "org.opencontainers.image.revision";
    private const string DefaultProject = "hrm-acceptance";

    private static readonly Lazy<AcceptanceRunContext> Lazy = new(Read);

    public static AcceptanceRunContext Current => Lazy.Value;

    private static string Project => Variable("ACCEPTANCE_COMPOSE_PROJECT") ?? DefaultProject;

    private static AcceptanceRunContext Read()
    {
        var ciRunId = Variable("GITHUB_RUN_ID");
        var containers = ReadContainers();
        return new AcceptanceRunContext(
            Variable("ACCEPTANCE_PROFILE") ?? DetectProfile(containers),
            Variable("ACCEPTANCE_SOURCE_SHA") ?? Run("git", "rev-parse", "HEAD"),
            Variable("ACCEPTANCE_CODE_TREE") ?? ReadCodeTree(),
            ParseRevisions(containers, Project),
            ciRunId is null ? "local" : "ci",
            ciRunId,
            Variable("GITHUB_EVENT_NAME"));
    }

    private static string? Variable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadCodeTree()
    {
        var script = Path.Combine(E2eSeed.RepoRoot, "scripts", "ci", "acceptance-code-tree.ps1");
        var tree = Run(OperatingSystem.IsWindows() ? "powershell" : "pwsh", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script);
        return tree?.Length == 64 ? tree : null;
    }

    /// <summary>
    /// One line per container of the stack: image, service, revision label, environment. A group that runs with the stack
    /// down (the checks of AC-QA-34/35 at the end of the job) reads the images the stack was started from.
    /// </summary>
    private static IReadOnlyList<string> ReadContainers()
    {
        const string containerFormat =
            "{{.Config.Image}}|{{index .Config.Labels \"com.docker.compose.service\"}}|" +
            "{{index .Config.Labels \"" + RevisionLabel + "\"}}|{{range .Config.Env}}{{.}};{{end}}";
        var ids = Run("docker", "ps", "-q", "--filter", $"label=com.docker.compose.project={Project}");
        if (!string.IsNullOrWhiteSpace(ids))
            return Inspect(containerFormat, ids);

        var images = Run("docker", "image", "ls", "--format", "{{.Repository}}:{{.Tag}}", "--filter", $"reference={Project}-*:latest");
        if (string.IsNullOrWhiteSpace(images))
            return [];
        const string imageFormat =
            "{{index .RepoTags 0}}|{{index .Config.Labels \"com.docker.compose.service\"}}|" +
            "{{index .Config.Labels \"" + RevisionLabel + "\"}}|";
        return Inspect(imageFormat, images);
    }

    private static IReadOnlyList<string> Inspect(string format, string targets)
    {
        var args = new List<string> { "inspect", "--format", format };
        args.AddRange(targets.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var output = Run("docker", [.. args]);
        return output is null ? [] : output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Only images built from this repository count; third-party images carry their own revision label.</summary>
    public static IReadOnlyDictionary<string, string> ParseRevisions(IEnumerable<string> containers, string project = DefaultProject)
    {
        var revisions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in containers)
        {
            var parts = line.Split('|');
            if (parts.Length < 3 || !parts[0].StartsWith(project + "-", StringComparison.Ordinal))
                continue;
            var name = parts[0][(project.Length + 1)..].Split(':')[0];
            revisions[name] = parts[2].Length == 0 || parts[2] == "<no value>" ? "unknown" : parts[2];
        }

        return revisions;
    }

    /// <summary>As AC-HCM-25 tells the stacks apart: the prod-like file sets these two values on HR.Core.</summary>
    public static string DetectProfile(IEnumerable<string> containers)
    {
        var hrCore = containers.Select(line => line.Split('|')).FirstOrDefault(parts => parts.Length >= 4 && parts[1] == "hr-core-web");
        if (hrCore is null)
            return "none";
        var prodLike = hrCore[3].Contains("NationalScope__IsLabScale=false;", StringComparison.Ordinal)
                       && hrCore[3].Contains("PolicyEngine__Core.Recruitment=Shadow;", StringComparison.Ordinal);
        return prodLike ? "prodlike" : "lab";
    }

    private static string? Run(string fileName, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = E2eSeed.RepoRoot
            };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            _ = errors.GetAwaiter().GetResult();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}
