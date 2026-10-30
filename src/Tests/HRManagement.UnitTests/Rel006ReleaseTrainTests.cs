using System.Text.Json;

namespace HRManagement.UnitTests;

public class Rel006ReleaseTrainTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "REL_006_RELEASE_TRAIN.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Freeze_windows_json_has_required_windows()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "rel", "freeze-windows.json")));
        var root = doc.RootElement;
        Assert.Equal("REL-006", root.GetProperty("card").GetString());

        var windows = root.GetProperty("freezeWindows").EnumerateArray()
            .Select(w => w.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in new[] { "payroll-close", "fiscal-year-end", "national-holidays", "pilot-stabilization" })
            Assert.Contains(id, windows);
    }

    [Fact]
    public void Emergency_hotfix_requires_dual_review_and_pir()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "rel", "freeze-windows.json")));
        var policy = doc.RootElement.GetProperty("emergencyHotfixPolicy");

        Assert.True(policy.GetProperty("requiredApprovals").GetInt32() >= 2);
        Assert.True(policy.GetProperty("icApprovalRequired").GetBoolean());
        Assert.True(policy.GetProperty("pirDeadlineHours").GetInt32() <= 24);
        Assert.True(policy.GetProperty("ciMustPass").GetBoolean());
    }

    [Fact]
    public void Freeze_windows_have_exception_policies()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "rel", "freeze-windows.json")));
        foreach (var window in doc.RootElement.GetProperty("freezeWindows").EnumerateArray())
        {
            Assert.True(window.TryGetProperty("exceptions", out var exceptions),
                $"Window {window.GetProperty("id")} must have exceptions list");
            Assert.True(exceptions.GetArrayLength() > 0);
        }
    }

    [Fact]
    public void Rel006_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "REL_006_RELEASE_TRAIN.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "rel", "freeze-windows.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "rel006-validate.ps1")));
    }

    [Fact]
    public void Rel006_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        // QA-034: RepoScriptRunner passes -ExecutionPolicy Bypass (Windows) / uses pwsh (Linux) and drains both streams.
        var (exitCode, stdout, stderr) = HRManagement.TestSupport.RepoScriptRunner.RunScript(Path.Combine(root, "scripts", "rel006-validate.ps1"));
        Assert.True(exitCode == 0, $"rel006-validate.ps1 failed ({exitCode}):\n{stdout}\n{stderr}");
    }
}
