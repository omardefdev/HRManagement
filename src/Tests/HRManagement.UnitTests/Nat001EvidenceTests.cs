using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>NAT-001: tests for national control evidence pack artifacts.</summary>
public class Nat001EvidenceTests
{

    [Fact]
    public void Evidence_index_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "nat", "control-evidence-index.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Evidence_index_has_sections()
    {
        var path = RepoPaths.Resolve("docs", "nat", "control-evidence-index.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("sections").GetArrayLength() >= 8);
    }

    [Fact]
    public void Evidence_index_requires_immutability()
    {
        var path = RepoPaths.Resolve("docs", "nat", "control-evidence-index.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var req = doc.RootElement.GetProperty("evidenceRequirements");
        Assert.True(req.GetProperty("immutability").GetBoolean());
        Assert.True(req.GetProperty("piiBlocked").GetBoolean());
    }

    [Fact]
    public void Evidence_index_has_authorization_gate()
    {
        var path = RepoPaths.Resolve("docs", "nat", "control-evidence-index.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var gate = doc.RootElement.GetProperty("authorizationGate");
        Assert.True(gate.GetProperty("independentAssessor").GetBoolean());
        Assert.True(gate.GetProperty("zeroCriticalGaps").GetBoolean());
    }

    [Fact]
    public void Evidence_index_has_lifecycle_phases()
    {
        var path = RepoPaths.Resolve("docs", "nat", "control-evidence-index.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("lifecycle").GetArrayLength() >= 4);
    }

    [Fact]
    public void Evidence_index_has_reproducibility_threshold()
    {
        var path = RepoPaths.Resolve("docs", "nat", "control-evidence-index.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var threshold = doc.RootElement.GetProperty("evidenceRequirements")
            .GetProperty("reproducibilityThreshold").GetDouble();
        Assert.True(threshold >= 0.10);
    }

    [Fact]
    public void Doc_references_evidence_and_authorization()
    {
        var path = RepoPaths.Resolve("docs", "NAT_001_EVIDENCE_PACK.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("NAT-001", content);
        Assert.Matches("(?i)immutab", content);
        Assert.Matches("(?i)authorization", content);
    }
}
