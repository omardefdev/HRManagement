using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>SYR-003: tests for LegalRegime domain entity and service regimes definitions.</summary>
public class Syr003ServiceRegimeTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr003-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void LegalRegime_extends_BaseEntity()
    {
        Assert.True(typeof(LegalRegime).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void LegalRegime_has_required_properties()
    {
        var props = typeof(LegalRegime).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("LegalRegimeId", props);
        Assert.Contains("RegimeName", props);
        Assert.Contains("Type", props);
        Assert.Contains("LegislationRef", props);
        Assert.Contains("AllowedEmploymentTypes", props);
        Assert.Contains("GradeStructureId", props);
        Assert.Contains("HasPromotionRules", props);
        Assert.Contains("CoversPension", props);
        Assert.Contains("EffectiveFrom", props);
        Assert.Contains("EffectiveTo", props);
        Assert.Contains("TransitionDecisionRef", props);
        Assert.Contains("IsActive", props);
        Assert.Contains("ConflictNotes", props);
    }

    [Fact]
    public void LegalRegime_IsEffectiveOn_returns_correct_result()
    {
        var regime = new LegalRegime
        {
            IsActive = true,
            EffectiveFrom = new DateTime(2004, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = null
        };

        Assert.True(regime.IsEffectiveOn(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(regime.IsEffectiveOn(new DateTime(2003, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void LegalRegime_IsEffectiveOn_respects_end_date()
    {
        var regime = new LegalRegime
        {
            IsActive = true,
            EffectiveFrom = new DateTime(2004, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = new DateTime(2021, 7, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.True(regime.IsEffectiveOn(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(regime.IsEffectiveOn(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void LegalRegime_type_helpers()
    {
        var law50 = new LegalRegime { Type = RegimeType.Law50 };
        Assert.True(law50.IsLaw50);
        Assert.False(law50.IsLaw53);
        Assert.False(law50.IsSpecial);

        var law53 = new LegalRegime { Type = RegimeType.Law53 };
        Assert.False(law53.IsLaw50);
        Assert.True(law53.IsLaw53);
        Assert.False(law53.IsSpecial);

        var special = new LegalRegime { Type = RegimeType.Special };
        Assert.False(special.IsLaw50);
        Assert.False(special.IsLaw53);
        Assert.True(special.IsSpecial);
    }

    [Fact]
    public void RegimeType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(RegimeType), RegimeType.Law50));
        Assert.True(Enum.IsDefined(typeof(RegimeType), RegimeType.Law53));
        Assert.True(Enum.IsDefined(typeof(RegimeType), RegimeType.Special));
    }

    [Fact]
    public void EmploymentType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(EmploymentType), EmploymentType.Permanent));
        Assert.True(Enum.IsDefined(typeof(EmploymentType), EmploymentType.Temporary));
        Assert.True(Enum.IsDefined(typeof(EmploymentType), EmploymentType.Daily));
        Assert.True(Enum.IsDefined(typeof(EmploymentType), EmploymentType.Seasonal));
        Assert.True(Enum.IsDefined(typeof(EmploymentType), EmploymentType.PartTime));
        Assert.True(Enum.IsDefined(typeof(EmploymentType), EmploymentType.Production));
        Assert.True(Enum.IsDefined(typeof(EmploymentType), EmploymentType.Contractual));
    }

    [Fact]
    public void ServiceRegimes_json_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "syr", "service-regimes.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc.RootElement.GetProperty("regimes"));
        Assert.NotNull(doc.RootElement.GetProperty("employment_types"));
        Assert.NotNull(doc.RootElement.GetProperty("transition_rules"));
        Assert.NotNull(doc.RootElement.GetProperty("exception_handling"));
    }

    [Fact]
    public void ServiceRegimes_json_has_law50_and_law53()
    {
        var path = RepoPaths.Resolve("docs", "syr", "service-regimes.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var regimes = doc.RootElement.GetProperty("regimes");
        var ids = new List<string>();
        foreach (var r in regimes.EnumerateArray())
        {
            ids.Add(r.GetProperty("legal_regime_id").GetString()!);
        }

        Assert.Contains("law50", ids);
        Assert.Contains("law53", ids);
    }

    [Fact]
    public void ServiceRegimes_json_exception_blocks_payroll()
    {
        var path = RepoPaths.Resolve("docs", "syr", "service-regimes.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var exc = doc.RootElement.GetProperty("exception_handling");
        Assert.True(exc.GetProperty("blocks_final_payroll").GetBoolean());
        Assert.Equal("exception_queue", exc.GetProperty("unclassified_action").GetString());
    }

    [Fact]
    public void ServiceRegimes_json_transitions_preserve_history()
    {
        var path = RepoPaths.Resolve("docs", "syr", "service-regimes.json");
        var json = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        var trans = doc.RootElement.GetProperty("transition_rules");
        Assert.True(trans.GetProperty("preserves_history").GetBoolean());
        Assert.True(trans.GetProperty("requires_decision_ref").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_003_SERVICE_REGIMES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "service-regimes.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Legal", "LegalRegime.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr003-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
