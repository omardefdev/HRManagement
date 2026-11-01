using System.Diagnostics;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-37 (P-7): scripts/ci/dedupe-push-run.sh skips the heavy jobs of a post-merge push run only when the merged
/// pull request's latest run succeeded on the identical tree; every other case (and every error) runs everything.
/// The GitHub API and git are replaced by fakes (GH_BIN / GIT_BIN).
/// </summary>
public sealed class DedupePushRunScriptTests : IDisposable
{
    private const string Tree = "1111111111111111111111111111111111111111";
    private const string OtherTree = "2222222222222222222222222222222222222222";
    private const string Head = "3333333333333333333333333333333333333333";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "int037-dedupe-" + Guid.NewGuid().ToString("N"));

    public DedupePushRunScriptTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Identical_tree_from_a_successful_pr_run_skips()
    {
        var (output, summary, _) = Run("push", FakeGh(pr: "31", run: "900 completed success", testedTree: Tree));
        Assert.Contains("skip=true", output);
        Assert.Contains("#31", summary);
        Assert.Contains("actions/runs/900", summary);
    }

    [Fact]
    public void Different_tree_runs()
    {
        var (output, _, _) = Run("push", FakeGh(pr: "31", run: "900 completed success", testedTree: OtherTree));
        Assert.Contains("skip=false", output);
    }

    [Fact]
    public void Api_error_runs()
    {
        var (output, _, log) = Run("push", "echo 'HTTP 502' >&2; exit 1");
        Assert.Contains("skip=false", output);
        Assert.Contains("could not list the pull requests", log);
    }

    [Fact]
    public void No_pull_request_runs()
    {
        var (output, _, log) = Run("push", FakeGh(pr: "", run: "900 completed success", testedTree: Tree));
        Assert.Contains("skip=false", output);
        Assert.Contains("no merged pull request", log);
    }

    [Theory]
    [InlineData("900 completed failure")]
    [InlineData("900 in_progress null")]
    [InlineData("")]
    public void Latest_pr_run_that_is_not_a_success_runs(string run)
    {
        var (output, _, _) = Run("push", FakeGh(pr: "31", run: run, testedTree: Tree));
        Assert.Contains("skip=false", output);
    }

    [Fact]
    public void Missing_artifact_runs()
    {
        var (output, _, log) = Run("push", FakeGh(pr: "31", run: "900 completed success", testedTree: null));
        Assert.Contains("skip=false", output);
        Assert.Contains("could not download", log);
    }

    [Fact]
    public void Pull_request_run_records_the_tree_and_never_skips()
    {
        var (output, _, _) = Run("pull_request", "exit 1");
        Assert.Contains("skip=false", output);
        Assert.Equal(Tree, System.IO.File.ReadAllText(Path.Combine(_dir, "tree", "tested-tree.txt")).Trim());
    }

    private string FakeGh(string pr, string run, string? testedTree)
    {
        var download = testedTree is null
            ? "exit 1"
            : $"d=''; while [ $# -gt 0 ]; do [ \"$1\" = --dir ] && d=\"$2\"; shift; done; printf '%s\\n' {testedTree} > \"$d/tested-tree.txt\"";
        return $$"""
            case "$1 $2" in
              "api repos/owner/repo/commits/"*) printf '%s\n' "{{pr}}";;
              "api repos/owner/repo/pulls/"*) printf '%s\n' "{{Head}}";;
              "api repos/owner/repo/actions/"*) case "$2" in *head_sha={{Head}}*) printf '%s\n' "{{run}}";; *) exit 1;; esac;;
              "run download") shift 2; {{download}};;
              *) exit 1;;
            esac
            """;
    }

    private (string Output, string Summary, string Log) Run(string eventName, string ghBody)
    {
        var gh = Path.Combine(_dir, "fake-gh.sh");
        var git = Path.Combine(_dir, "fake-git.sh");
        System.IO.File.WriteAllText(gh, ghBody.Replace("\r\n", "\n") + "\n");
        System.IO.File.WriteAllText(git, $"[ \"$1 $2\" = \"rev-parse HEAD^{{tree}}\" ] && echo {Tree} && exit 0\nexit 1\n");
        var output = Path.Combine(_dir, "output.txt");
        var summary = Path.Combine(_dir, "summary.md");
        System.IO.File.WriteAllText(output, "");
        System.IO.File.WriteAllText(summary, "");

        var psi = new ProcessStartInfo(BashPath(), $"\"{ToBash(Path.Combine(RepoPaths.Root, "scripts", "ci", "dedupe-push-run.sh"))}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _dir
        };
        psi.Environment["GH_BIN"] = "bash " + ToBash(gh);
        psi.Environment["GIT_BIN"] = "bash " + ToBash(git);
        psi.Environment["GITHUB_EVENT_NAME"] = eventName;
        psi.Environment["GITHUB_SHA"] = "4444444444444444444444444444444444444444";
        psi.Environment["GITHUB_REPOSITORY"] = "owner/repo";
        psi.Environment["GITHUB_OUTPUT"] = ToBash(output);
        psi.Environment["GITHUB_STEP_SUMMARY"] = ToBash(summary);
        psi.Environment["TREE_FILE"] = ToBash(Path.Combine(_dir, "tree", "tested-tree.txt"));
        psi.Environment["MSYS_NO_PATHCONV"] = "1";
        using var process = Process.Start(psi)!;
        var log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "dedupe script timed out");
        Assert.True(process.ExitCode == 0, $"the guard must always exit 0 (fail open); exit {process.ExitCode}: {log}");
        return (System.IO.File.ReadAllText(output), System.IO.File.ReadAllText(summary), log);
    }

    private static string ToBash(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private static string BashPath()
    {
        if (!OperatingSystem.IsWindows())
            return "bash";
        var psi = new ProcessStartInfo("git", "--exec-path") { RedirectStandardOutput = true, UseShellExecute = false };
        using var git = Process.Start(psi)!;
        var exec = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        // <git>/mingw64/libexec/git-core -> <git>/bin/bash.exe
        var root = Directory.GetParent(exec)!.Parent!.Parent!.FullName;
        return Path.Combine(root, "bin", "bash.exe");
    }
}
