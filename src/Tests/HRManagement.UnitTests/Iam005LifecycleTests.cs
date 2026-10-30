using System.Text.Json;

namespace HRManagement.UnitTests;

public class Iam005LifecycleTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "IAM_005_IDENTITY_LIFECYCLE.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Jml_json_has_all_lifecycle_events()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "iam", "joiner-mover-leaver.json")));
        var root = doc.RootElement;
        Assert.Equal("IAM-005", root.GetProperty("card").GetString());

        var types = root.GetProperty("lifecycleEvents").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var t in new[] { "Joiner", "Mover", "Leaver" })
            Assert.Contains(t, types);
    }

    [Fact]
    public void Mover_revokes_source_before_assigning_destination()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "iam", "joiner-mover-leaver.json")));
        var mover = doc.RootElement.GetProperty("lifecycleEvents").EnumerateArray()
            .First(e => e.GetProperty("type").GetString() == "Mover");
        var steps = mover.GetProperty("steps").EnumerateArray().ToList();

        var revokeIdx = steps.FindIndex(s => s.GetProperty("id").GetString() == "revoke-source");
        var assignIdx = steps.FindIndex(s => s.GetProperty("id").GetString() == "assign-destination");

        Assert.True(revokeIdx < assignIdx, "revoke-source must come before assign-destination");
    }

    [Fact]
    public void Leaver_disables_login_within_1_hour()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "iam", "joiner-mover-leaver.json")));
        var leaver = doc.RootElement.GetProperty("lifecycleEvents").EnumerateArray()
            .First(e => e.GetProperty("type").GetString() == "Leaver");
        var disable = leaver.GetProperty("steps").EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == "disable-login");

        Assert.True(disable.GetProperty("slaHours").GetInt32() <= 1);
    }

    [Fact]
    public void Feed_outage_is_fail_closed()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "iam", "joiner-mover-leaver.json")));
        var outage = doc.RootElement.GetProperty("feedOutage");

        Assert.True(outage.GetProperty("failClosed").GetBoolean());
        Assert.True(outage.GetProperty("noNewGrants").GetBoolean());
        Assert.True(outage.GetProperty("alertWithinMinutes").GetInt32() <= 15);
    }

    [Fact]
    public void Orphan_accounts_disabled_within_24_hours()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "iam", "joiner-mover-leaver.json")));
        var policy = doc.RootElement.GetProperty("orphanPolicy");

        Assert.True(policy.GetProperty("orphanDisableHours").GetInt32() <= 24);
        Assert.True(policy.GetProperty("dormantThresholdDays").GetInt32() <= 90);
    }

    [Fact]
    public void Iam005_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "IAM_005_IDENTITY_LIFECYCLE.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "iam", "joiner-mover-leaver.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "iam005-validate.ps1")));
    }

    [Fact]
    public void Iam005_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        // QA-034: RepoScriptRunner passes -ExecutionPolicy Bypass (Windows) / uses pwsh (Linux) and drains both streams.
        var (exitCode, stdout, stderr) = HRManagement.TestSupport.RepoScriptRunner.RunScript(Path.Combine(root, "scripts", "iam005-validate.ps1"));
        Assert.True(exitCode == 0, $"iam005-validate.ps1 failed ({exitCode}):\n{stdout}\n{stderr}");
    }
}
