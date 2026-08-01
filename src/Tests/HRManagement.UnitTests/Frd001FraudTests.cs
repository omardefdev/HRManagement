using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>FRD-001: tests for fraud prevention anomaly rules artifacts.</summary>
public class Frd001FraudTests
{

    [Fact]
    public void Anomaly_rules_exist_and_are_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "frd", "anomaly-rules.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("FRD-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Anomaly_rules_have_at_least_five_detection_rules()
    {
        var path = RepoPaths.Resolve("docs", "frd", "anomaly-rules.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("detectionRules").GetArrayLength() >= 5);
    }

    [Fact]
    public void Anomaly_rules_define_proof_of_life()
    {
        var path = RepoPaths.Resolve("docs", "frd", "anomaly-rules.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var pol = doc.RootElement.GetProperty("proofOfLife");
        Assert.True(pol.TryGetProperty("pensioners", out _));
        Assert.True(pol.GetProperty("holdOnExpiry").GetBoolean());
    }

    [Fact]
    public void Payment_hold_blocks_single_operator_release()
    {
        var path = RepoPaths.Resolve("docs", "frd", "anomaly-rules.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var hold = doc.RootElement.GetProperty("paymentHold");
        Assert.True(hold.GetProperty("singleOperatorReleaseBlocked").GetBoolean());
        Assert.True(hold.GetProperty("releaseRequiresEvidence").GetBoolean());
    }

    [Fact]
    public void Verification_requires_three_audited_cycles()
    {
        var path = RepoPaths.Resolve("docs", "frd", "anomaly-rules.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var v = doc.RootElement.GetProperty("verification");
        Assert.True(v.GetProperty("consecutiveCyclesWithAudit").GetInt32() >= 3);
    }

    [Fact]
    public void Each_detection_rule_has_required_fields()
    {
        var path = RepoPaths.Resolve("docs", "frd", "anomaly-rules.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        foreach (var rule in doc.RootElement.GetProperty("detectionRules").EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(rule.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrEmpty(rule.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrEmpty(rule.GetProperty("severity").GetString()));
            Assert.False(string.IsNullOrEmpty(rule.GetProperty("action").GetString()));
        }
    }

    [Fact]
    public void Doc_references_ghost_and_anomaly()
    {
        var path = RepoPaths.Resolve("docs", "FRD_001_FRAUD.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("FRD-001", content);
        Assert.Matches("(?i)ghost", content);
        Assert.Matches("(?i)anomal", content);
    }
}
