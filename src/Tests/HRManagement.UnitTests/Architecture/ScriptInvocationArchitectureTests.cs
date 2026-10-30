using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// QA-034: a test that runs a repository PowerShell script with <c>-File</c> must pass <c>-ExecutionPolicy Bypass</c>
/// (or call <see cref="RepoScriptRunner"/>, which does). Without it the script is refused under the default Windows
/// policy, and seven tests were red on Windows for that reason only.
/// </summary>
public class ScriptInvocationArchitectureTests
{
    // "-File" as a separate argument ("-File", ...) or inside an argument string (... -File \"{script}\" ...).
    private static readonly Regex FileSwitch = new(@"""-File""|-File\s+\\""|-File\s+\{", RegexOptions.Compiled);
    private static readonly Regex Bypass = new(@"ExecutionPolicy""?\s*,?\s*""?\s*Bypass|-ExecutionPolicy\s+Bypass", RegexOptions.Compiled);
    private static readonly Regex Shell = new(@"""(powershell|pwsh)""|PowerShellExecutable|\bshell\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const int Window = 12;

    public static IEnumerable<string> TestSourceFiles()
    {
        var testsRoot = Path.Combine(RepoPaths.Root, "src", "Tests");
        return Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public void Script_invocations_in_tests_bypass_the_execution_policy()
    {
        var offenders = new List<string>();
        foreach (var file in TestSourceFiles())
        {
            if (Path.GetFileName(file) == nameof(ScriptInvocationArchitectureTests) + ".cs")
                continue;
            var lines = System.IO.File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!FileSwitch.IsMatch(lines[i]))
                    continue;
                var from = Math.Max(0, i - Window);
                var to = Math.Min(lines.Length - 1, i + Window);
                var block = string.Join("\n", lines[from..(to + 1)]);
                if (!Shell.IsMatch(block))
                    continue; // not a PowerShell invocation (e.g. a file path argument of another tool)
                if (!Bypass.IsMatch(block))
                    offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "PowerShell -File without -ExecutionPolicy Bypass (use HRManagement.TestSupport.RepoScriptRunner.RunScript):\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Guard_detects_a_missing_bypass()
    {
        // Self-check of the detection on the pattern the seven fixed tests used to have.
        const string bad = """
                           var psi = new System.Diagnostics.ProcessStartInfo
                           {
                               FileName = "powershell",
                               ArgumentList = { "-NoProfile", "-File", script, "-RepoRoot", root },
                           };
                           """;
        Assert.Matches(FileSwitch, bad);
        Assert.Matches(Shell, bad);
        Assert.DoesNotMatch(Bypass, bad);

        const string good = """
                            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{root}\"",
                            """;
        Assert.Matches(FileSwitch, good);
        Assert.Matches(Bypass, good);
    }
}
