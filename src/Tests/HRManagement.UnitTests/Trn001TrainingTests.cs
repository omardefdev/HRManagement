using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>TRN-001: tests for training and change management artifacts.</summary>
public class Trn001TrainingTests
{

    [Fact]
    public void Persona_curricula_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "trn", "persona-curricula.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("TRN-001", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Persona_curricula_has_personas()
    {
        var path = RepoPaths.Resolve("docs", "trn", "persona-curricula.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("personas").GetArrayLength() >= 5);
    }

    [Fact]
    public void Persona_curricula_has_curriculum_modules()
    {
        var path = RepoPaths.Resolve("docs", "trn", "persona-curricula.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("curriculumStructure").GetArrayLength() >= 4);
    }

    [Fact]
    public void Readiness_thresholds_are_reasonable()
    {
        var path = RepoPaths.Resolve("docs", "trn", "persona-curricula.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var thresholds = doc.RootElement.GetProperty("readinessThresholds");
        Assert.True(thresholds.GetProperty("trainingCompletionRate").GetDouble() >= 0.80);
        Assert.True(thresholds.GetProperty("assessmentPassRate").GetDouble() >= 0.70);
    }

    [Fact]
    public void Change_management_includes_resistance_register()
    {
        var path = RepoPaths.Resolve("docs", "trn", "persona-curricula.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var cm = doc.RootElement.GetProperty("changeManagement");
        Assert.True(cm.GetProperty("resistanceRegister").GetBoolean());
    }

    [Fact]
    public void Doc_references_personas_and_readiness()
    {
        var path = RepoPaths.Resolve("docs", "TRN_001_TRAINING.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("TRN-001", content);
        Assert.Matches("(?i)persona", content);
        Assert.Matches("(?i)readiness", content);
    }
}
