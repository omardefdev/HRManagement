using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>DEC-001: tests for legacy system decommission artifacts.</summary>
public class Dec001DecommissionTests
{

    [Fact]
    public void Checklist_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "dec", "decommission-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("DEC-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Checklist_has_phases()
    {
        var path = RepoPaths.Resolve("docs", "dec", "decommission-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("phases").GetArrayLength() >= 6);
    }

    [Fact]
    public void Checklist_requires_archive_manifest()
    {
        var path = RepoPaths.Resolve("docs", "dec", "decommission-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("archiveRequirements").GetProperty("manifestRequired").GetBoolean());
    }

    [Fact]
    public void Checklist_requires_shadow_audit()
    {
        var path = RepoPaths.Resolve("docs", "dec", "decommission-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("shadowOperationDetection").GetProperty("fieldAuditRequired").GetBoolean());
    }

    [Fact]
    public void Checklist_requires_destruction_certificate()
    {
        var path = RepoPaths.Resolve("docs", "dec", "decommission-checklist.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("mediaDestruction").GetProperty("destructionCertificateRequired").GetBoolean());
    }

    [Fact]
    public void Doc_references_decommission_and_shadow()
    {
        var path = RepoPaths.Resolve("docs", "DEC_001_DECOMMISSION.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("DEC-001", content);
        Assert.Matches("(?i)decommiss", content);
        Assert.Matches("(?i)shadow", content);
        Assert.Matches("(?i)archive", content);
    }
}
