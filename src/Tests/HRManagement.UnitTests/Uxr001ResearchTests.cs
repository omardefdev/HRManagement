using System.Text.Json;

namespace HRManagement.UnitTests;

public class Uxr001ResearchTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "UXR_001_RESEARCH_FRAMEWORK.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Framework_and_uxr_docs_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "UXR_001_RESEARCH_FRAMEWORK.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "uxr", "journey-blueprints.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "uxr", "usability-metrics-baseline.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "uxr", "research-ethics-protocol.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "uxr001-validate.ps1")));
    }

    [Fact]
    public void Metrics_baseline_covers_four_critical_journeys()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "uxr", "usability-metrics-baseline.json")));
        var root = doc.RootElement;
        Assert.Equal("UXR-001", root.GetProperty("card").GetString());

        var journeyIds = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var need in new[] { "journey-hire", "journey-payroll", "journey-leave", "journey-transfer" })
            Assert.Contains(need, journeyIds);
    }

    [Fact]
    public void Metrics_baseline_requires_all_archetypes()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "uxr", "usability-metrics-baseline.json")));
        var archetypes = doc.RootElement.GetProperty("archetypesRequired").EnumerateArray()
            .Select(a => a.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var need in new[]
                 {
                     "central-ministry", "governorate", "autonomous-institution",
                     "branch-office", "specialized-body"
                 })
            Assert.Contains(need, archetypes);
    }

    [Fact]
    public void Journey_blueprints_reference_all_critical_flows()
    {
        var content = System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "uxr", "journey-blueprints.md"));

        foreach (var flow in new[] { "journey-hire", "journey-payroll", "journey-leave", "journey-transfer" })
            Assert.Contains(flow, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ethics_protocol_covers_consent_and_privacy()
    {
        var content = System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "uxr", "research-ethics-protocol.md"));

        Assert.Contains("consent", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRV", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Global_targets_meet_acceptance_thresholds()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(ResolveRepoRoot(), "docs", "uxr", "usability-metrics-baseline.json")));
        var targets = doc.RootElement.GetProperty("globalTargets");

        Assert.True(targets.GetProperty("taskSuccessRateMin").GetDouble() >= 0.85);
        Assert.True(targets.GetProperty("susScoreMin").GetInt32() >= 68);
        Assert.Equal(0, targets.GetProperty("accessibilityBlockersMax").GetInt32());
    }
}
