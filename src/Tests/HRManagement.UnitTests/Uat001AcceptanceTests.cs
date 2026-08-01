using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>UAT-001: tests for formal acceptance testing artifacts.</summary>
public class Uat001AcceptanceTests
{

    [Fact]
    public void Matrix_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "uat", "requirement-test-evidence-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("UAT-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Matrix_has_phases()
    {
        var path = RepoPaths.Resolve("docs", "uat", "requirement-test-evidence-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("phases").GetArrayLength() >= 4);
    }

    [Fact]
    public void Matrix_has_acceptance_criteria()
    {
        var path = RepoPaths.Resolve("docs", "uat", "requirement-test-evidence-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("acceptanceCriteria").GetArrayLength() >= 5);
    }

    [Fact]
    public void Matrix_requires_per_capability_signoff()
    {
        var path = RepoPaths.Resolve("docs", "uat", "requirement-test-evidence-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("signOff").GetProperty("perCapability").GetBoolean());
    }

    [Fact]
    public void Matrix_has_traceability_rules()
    {
        var path = RepoPaths.Resolve("docs", "uat", "requirement-test-evidence-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var trace = doc.RootElement.GetProperty("traceability");
        Assert.Equal("mandatory", trace.GetProperty("requirementToTestMapping").GetString());
        Assert.Equal("mandatory", trace.GetProperty("testToEvidenceMapping").GetString());
    }

    [Fact]
    public void Matrix_has_defect_severity_levels()
    {
        var path = RepoPaths.Resolve("docs", "uat", "requirement-test-evidence-matrix.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("defectSeverity").GetArrayLength() >= 3);
    }

    [Fact]
    public void Doc_references_signoff_and_traceability()
    {
        var path = RepoPaths.Resolve("docs", "UAT_001_ACCEPTANCE.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("UAT-001", content);
        Assert.Matches("(?i)sign.off", content);
        Assert.Matches("(?i)traceab", content);
    }
}
