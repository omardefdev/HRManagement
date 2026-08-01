using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>ONB-002: tests for agency accreditation wave artifacts.</summary>
public class Onb002AccreditationTests
{

    [Fact]
    public void Checklist_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "onb", "wave-readiness-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("ONB-002", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Checklist_has_wave_progression()
    {
        var path = RepoPaths.Resolve("docs", "onb", "wave-readiness-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("waveProgression").GetArrayLength() >= 3);
    }

    [Fact]
    public void Checklist_has_readiness_dimensions()
    {
        var path = RepoPaths.Resolve("docs", "onb", "wave-readiness-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("readinessDimensions").GetArrayLength() >= 6);
    }

    [Fact]
    public void Go_no_go_requires_all_mandatory()
    {
        var path = RepoPaths.Resolve("docs", "onb", "wave-readiness-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var decision = doc.RootElement.GetProperty("goNoGoDecision");
        Assert.True(decision.GetProperty("allMandatoryRequired").GetBoolean());
    }

    [Fact]
    public void Go_no_go_defines_blast_radius()
    {
        var path = RepoPaths.Resolve("docs", "onb", "wave-readiness-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var decision = doc.RootElement.GetProperty("goNoGoDecision");
        Assert.True(decision.GetProperty("blastRadiusDefined").GetBoolean());
    }

    [Fact]
    public void Monitoring_period_at_least_30_days()
    {
        var path = RepoPaths.Resolve("docs", "onb", "wave-readiness-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var days = doc.RootElement.GetProperty("goNoGoDecision")
            .GetProperty("monitoringPeriodDays").GetInt32();
        Assert.True(days >= 30);
    }

    [Fact]
    public void Doc_references_waves_and_readiness()
    {
        var path = RepoPaths.Resolve("docs", "ONB_002_ACCREDITATION.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("ONB-002", content);
        Assert.Matches("(?i)wave", content);
        Assert.Matches("(?i)readiness", content);
    }
}
