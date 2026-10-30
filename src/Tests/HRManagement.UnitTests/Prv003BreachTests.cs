using System.Text.Json;

namespace HRManagement.UnitTests;

public class Prv003BreachTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "PRV_003_BREACH_RESPONSE.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Workflow_json_has_all_required_phases()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "prv", "breach-response-workflow.json")));
        var root = doc.RootElement;
        Assert.Equal("PRV-003", root.GetProperty("card").GetString());

        var phases = root.GetProperty("phases").EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in new[] { "detection-triage", "scoping", "risk-assessment",
            "regulator-notification", "individual-notification", "remediation", "post-incident-review" })
            Assert.Contains(id, phases);
    }

    [Fact]
    public void Regulator_notification_within_72_hours_and_statutory()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "prv", "breach-response-workflow.json")));
        var phase = doc.RootElement.GetProperty("phases").EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == "regulator-notification");

        Assert.True(phase.GetProperty("targetHours").GetInt32() <= 72);
        Assert.True(phase.GetProperty("statutory").GetBoolean());
    }

    [Fact]
    public void No_notify_decision_requires_dpo_and_legal_signoff()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "prv", "breach-response-workflow.json")));
        var tree = doc.RootElement.GetProperty("notifyDecisionTree");
        var required = tree.GetProperty("noNotifyRequires").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet();

        Assert.Contains("dpoSignoff", required);
        Assert.Contains("legalSignoff", required);
    }

    [Fact]
    public void Processor_must_notify_within_24_hours()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "prv", "breach-response-workflow.json")));
        var obligation = doc.RootElement.GetProperty("processorObligation");

        Assert.True(obligation.GetProperty("notifyControllerMaxHours").GetInt32() <= 24);
    }

    [Fact]
    public void Triage_phase_targets_4_hours()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "prv", "breach-response-workflow.json")));
        var phase = doc.RootElement.GetProperty("phases").EnumerateArray()
            .First(p => p.GetProperty("id").GetString() == "detection-triage");

        Assert.True(phase.GetProperty("targetHours").GetInt32() <= 4);
    }

    [Fact]
    public void Prv003_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "PRV_003_BREACH_RESPONSE.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "prv", "breach-response-workflow.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "prv003-validate.ps1")));
    }

    [Fact]
    public void Prv003_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        // QA-034: RepoScriptRunner passes -ExecutionPolicy Bypass (Windows) / uses pwsh (Linux) and drains both streams.
        var (exitCode, stdout, stderr) = HRManagement.TestSupport.RepoScriptRunner.RunScript(Path.Combine(root, "scripts", "prv003-validate.ps1"));
        Assert.True(exitCode == 0, $"prv003-validate.ps1 failed ({exitCode}):\n{stdout}\n{stderr}");
    }
}
