using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>TRN-002: tests for unions/worker representation artifacts.</summary>
public class Trn002UnionsTests
{

    [Fact]
    public void Worker_representation_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "trn", "worker-representation.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("TRN-002", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Worker_representation_has_bodies()
    {
        var path = RepoPaths.Resolve("docs", "trn", "worker-representation.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("representationBodies").GetArrayLength() >= 2);
    }

    [Fact]
    public void Worker_representation_has_engagement_topics()
    {
        var path = RepoPaths.Resolve("docs", "trn", "worker-representation.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("engagementClassification").GetArrayLength() >= 5);
    }

    [Fact]
    public void Worker_representation_has_protocol_steps()
    {
        var path = RepoPaths.Resolve("docs", "trn", "worker-representation.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("engagementProtocol").GetArrayLength() >= 4);
    }

    [Fact]
    public void Monitoring_boundaries_require_disclosure()
    {
        var path = RepoPaths.Resolve("docs", "trn", "worker-representation.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var boundaries = doc.RootElement.GetProperty("monitoringBoundaries");
        Assert.True(boundaries.GetProperty("scopeDisclosed").GetBoolean());
    }

    [Fact]
    public void Wave_readiness_blocks_without_engagement()
    {
        var path = RepoPaths.Resolve("docs", "trn", "worker-representation.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var readiness = doc.RootElement.GetProperty("waveReadiness");
        Assert.True(readiness.GetProperty("noWaveWithoutEngagementEvidence").GetBoolean());
    }

    [Fact]
    public void Doc_references_negotiation_and_monitoring()
    {
        var path = RepoPaths.Resolve("docs", "TRN_002_UNIONS.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("TRN-002", content);
        Assert.Matches("(?i)negotiat", content);
        Assert.Matches("(?i)monitor", content);
    }
}
