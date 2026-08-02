using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-005: tests for Decree 67/68 wage/compensation rule pack structure.</summary>
public class Syr005WageTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr005-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Pack_references_decrees_67_and_68()
    {
        var path = RepoPaths.Resolve("docs", "syr", "decree-67-68-wage-tables.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("SYR-005", root.GetProperty("card").GetString());

        var sources = root.GetProperty("legalSources").EnumerateArray()
            .Select(s => s.GetProperty("legalSourceId").GetString()).ToList();
        Assert.Contains("SYR-DEC-67-2026", sources);
        Assert.Contains("SYR-DEC-68-2026", sources);
    }

    [Fact]
    public void Pack_separates_base_compensation_and_bases()
    {
        var path = RepoPaths.Resolve("docs", "syr", "decree-67-68-wage-tables.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var sep = doc.RootElement.GetProperty("separationPolicy");

        Assert.Equal("baseWage", sep.GetProperty("baseWageField").GetString());
        Assert.Equal("compensationTotal", sep.GetProperty("compensationField").GetString());
        Assert.Equal("taxableBase", sep.GetProperty("taxableBaseField").GetString());
        Assert.Equal("insurableBase", sep.GetProperty("insurableBaseField").GetString());
        Assert.True(sep.GetProperty("forbidDoubleStacking67And68").GetBoolean());
    }

    [Fact]
    public void Pack_defines_wage_tables_with_grades()
    {
        var path = RepoPaths.Resolve("docs", "syr", "decree-67-68-wage-tables.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var tables = doc.RootElement.GetProperty("wageTables").EnumerateArray().ToList();
        Assert.True(tables.Count >= 1);

        var grades = tables[0].GetProperty("grades").EnumerateArray().ToList();
        Assert.True(grades.Count >= 2);
        Assert.True(grades[0].GetProperty("baseWage").GetInt64() > 0);
    }

    [Fact]
    public void Pack_forbids_proposed_tax_reform_before_enactment()
    {
        var path = RepoPaths.Resolve("docs", "syr", "decree-67-68-wage-tables.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var reform = doc.RootElement.GetProperty("proposedTaxReform");

        Assert.Equal("not_effective", reform.GetProperty("status").GetString());
        Assert.True(reform.GetProperty("forbidApplicationBeforeEffectiveDate").GetBoolean());
    }

    [Fact]
    public void Pack_requires_governance_controls()
    {
        var path = RepoPaths.Resolve("docs", "syr", "decree-67-68-wage-tables.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var gov = doc.RootElement.GetProperty("governance");

        Assert.True(gov.GetProperty("fourEyesRequired").GetBoolean());
        Assert.True(gov.GetProperty("simulationBeforeActivation").GetBoolean());
        Assert.True(gov.GetProperty("rollbackStopsPayment").GetBoolean());
        Assert.True(gov.GetProperty("rollbackPreservesHistory").GetBoolean());
    }

    [Fact]
    public void Pack_forbids_float_arithmetic()
    {
        var path = RepoPaths.Resolve("docs", "syr", "decree-67-68-wage-tables.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("roundingRules")
            .GetProperty("forbidFloatArithmetic").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_005_WAGE_PACKAGES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "decree-67-68-wage-tables.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr005-validate.ps1");
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
