using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>SYR-012: tests for legacy record unification and provenance quarantine artifacts.</summary>
public class Syr012LegacyTests
{

    [Fact]
    public void Provenance_model_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "syr", "provenance-quarantine-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("SYR-012", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Provenance_model_has_quarantine_reasons()
    {
        var path = RepoPaths.Resolve("docs", "syr", "provenance-quarantine-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var reasons = doc.RootElement.GetProperty("quarantineReasons");
        Assert.True(reasons.GetArrayLength() >= 5);
    }

    [Fact]
    public void Provenance_model_has_approved_source_systems()
    {
        var path = RepoPaths.Resolve("docs", "syr", "provenance-quarantine-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var sources = doc.RootElement.GetProperty("approvedSourceSystems");
        Assert.True(sources.GetArrayLength() >= 3);
    }

    [Fact]
    public void Provenance_model_requires_hash_verification()
    {
        var path = RepoPaths.Resolve("docs", "syr", "provenance-quarantine-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var rules = doc.RootElement.GetProperty("unificationRules");
        var points = rules.GetProperty("hashVerificationPoints");
        Assert.True(points.GetArrayLength() >= 3);
    }

    [Fact]
    public void Provenance_model_blocks_silent_merge()
    {
        var path = RepoPaths.Resolve("docs", "syr", "provenance-quarantine-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var rules = doc.RootElement.GetProperty("unificationRules");
        Assert.True(rules.GetProperty("silentMergeBlocked").GetBoolean());
    }

    [Fact]
    public void Provenance_model_has_control_total_invariant()
    {
        var path = RepoPaths.Resolve("docs", "syr", "provenance-quarantine-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var recon = doc.RootElement.GetProperty("reconciliation");
        Assert.False(string.IsNullOrEmpty(recon.GetProperty("controlTotalInvariant").GetString()));
    }

    [Fact]
    public void Doc_references_quarantine_and_provenance()
    {
        var path = RepoPaths.Resolve("docs", "SYR_012_LEGACY_UNIFICATION.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("SYR-012", content);
        Assert.Matches("(?i)quarantine", content);
        Assert.Matches("(?i)provenance", content);
    }
}
