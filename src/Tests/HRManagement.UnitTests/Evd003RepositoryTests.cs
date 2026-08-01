using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>EVD-003: tests for evidence repository artifacts.</summary>
public class Evd003RepositoryTests
{

    [Fact]
    public void Evidence_store_model_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "evd", "evidence-store-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("EVD-003", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Evidence_store_enforces_immutability()
    {
        var path = RepoPaths.Resolve("docs", "evd", "evidence-store-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var imm = doc.RootElement.GetProperty("immutability");
        Assert.True(imm.GetProperty("appendOnly").GetBoolean());
        Assert.True(imm.GetProperty("adminCannotModify").GetBoolean());
        Assert.True(imm.GetProperty("wormStorage").GetBoolean());
    }

    [Fact]
    public void Evidence_store_supports_automated_collection()
    {
        var path = RepoPaths.Resolve("docs", "evd", "evidence-store-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var auto = doc.RootElement.GetProperty("automatedCollection");
        Assert.True(auto.GetProperty("ciCdPublish").GetBoolean());
    }

    [Fact]
    public void Evidence_store_blocks_pii()
    {
        var path = RepoPaths.Resolve("docs", "evd", "evidence-store-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var pii = doc.RootElement.GetProperty("piiProtection");
        Assert.True(pii.GetProperty("piiBlocked").GetBoolean());
        Assert.True(pii.GetProperty("secretsBlocked").GetBoolean());
    }

    [Fact]
    public void Evidence_store_has_freshness_rules()
    {
        var path = RepoPaths.Resolve("docs", "evd", "evidence-store-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var fresh = doc.RootElement.GetProperty("freshness");
        Assert.True(fresh.GetProperty("expiredCreatesVisibleGap").GetBoolean());
        Assert.True(fresh.GetProperty("gapNotification").GetBoolean());
    }

    [Fact]
    public void Evidence_store_within_sovereign_boundary()
    {
        var path = RepoPaths.Resolve("docs", "evd", "evidence-store-model.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var sov = doc.RootElement.GetProperty("sovereignBoundary");
        Assert.True(sov.GetProperty("storedWithinSovereignTerritory").GetBoolean());
    }

    [Fact]
    public void Doc_references_immutability_and_worm()
    {
        var path = RepoPaths.Resolve("docs", "EVD_003_REPOSITORY.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("EVD-003", content);
        Assert.Matches("(?i)immutab", content);
        Assert.Contains("WORM", content);
    }
}
