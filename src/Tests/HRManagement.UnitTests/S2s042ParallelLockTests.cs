using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 (P-11): two plans take turns on the one acceptance stack and the one demo server.</summary>
public sealed class S2s042ParallelLockTests : IDisposable
{
    private static readonly string Script = Path.Combine(RepoPaths.Root, "scripts", "parallel", "hrm-lock.ps1");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrm-lock-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private (int Exit, string Output) Lock(string action, string plan, string extra = "")
    {
        var (exit, stdout, stderr) = RepoScriptRunner.RunScript(
            Script,
            $"-Action {action} -Resource acceptance-stack -Plan {plan} -Purpose test -LockDir \"{_dir}\" {extra}",
            60_000);
        return (exit, stdout + stderr);
    }

    [Fact]
    public void A_second_acquire_fails_and_names_the_holder()
    {
        Assert.Equal(0, Lock("Acquire", "PLAN-43").Exit);

        var (exit, output) = Lock("Acquire", "PLAN-47");

        Assert.Equal(1, exit);
        Assert.Contains("PLAN-43", output);
    }

    [Fact]
    public void Release_frees_it_and_another_plan_cannot_release_it()
    {
        Assert.Equal(0, Lock("Acquire", "PLAN-43").Exit);

        Assert.Equal(1, Lock("Release", "PLAN-47").Exit);
        Assert.Equal(1, Lock("Acquire", "PLAN-47").Exit);
        Assert.Equal(0, Lock("Release", "PLAN-43").Exit);
        Assert.Equal(0, Lock("Acquire", "PLAN-47").Exit);
    }

    [Fact]
    public void An_expired_lock_can_be_taken_over_and_says_so()
    {
        Assert.Equal(0, Lock("Acquire", "PLAN-43", "-Hours -1").Exit);

        var (exit, output) = Lock("Acquire", "PLAN-47");

        Assert.Equal(0, exit);
        Assert.Contains("expired", output);
        Assert.Contains("PLAN-43", output);
        Assert.Equal(1, Lock("Acquire", "PLAN-48").Exit);
    }

    [Fact]
    public void Renew_extends_the_holders_lock_only()
    {
        Assert.Equal(0, Lock("Acquire", "PLAN-43", "-Hours -1").Exit);

        Assert.Equal(1, Lock("Renew", "PLAN-47").Exit);
        Assert.Equal(0, Lock("Renew", "PLAN-43").Exit);
        Assert.Equal(1, Lock("Acquire", "PLAN-47").Exit);
    }

    [Fact]
    public void Status_says_free_and_held()
    {
        Assert.Contains("free", Lock("Status", "PLAN-43").Output);
        Lock("Acquire", "PLAN-43");
        Assert.Contains("held", Lock("Status", "PLAN-47").Output);
    }

    [Fact]
    public void The_script_is_ASCII_and_needs_no_pwsh()
    {
        var text = System.IO.File.ReadAllText(Script);

        Assert.All(text, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4}"));
        Assert.DoesNotContain("pwsh", text);
    }
}
