using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>SYR-002: tests for census register structure and validation.</summary>
public class Syr002CensusTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Census_register_has_14_governorates()
    {
        var path = RepoPaths.Resolve("docs", "syr", "census-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("SYR-002", root.GetProperty("card").GetString());

        var govs = root.GetProperty("governorates").EnumerateArray().ToList();
        Assert.Equal(14, govs.Count);

        var ids = govs.Select(g => g.GetProperty("id").GetString()).ToList();
        Assert.Contains("SY-DI", ids);
        Assert.Contains("SY-HL", ids);
        Assert.Contains("SY-LA", ids);
    }

    [Fact]
    public void Census_has_all_classification_categories()
    {
        var path = RepoPaths.Resolve("docs", "syr", "census-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var categories = doc.RootElement.GetProperty("censusCategories")
            .EnumerateArray()
            .Select(c => c.GetProperty("code").GetString())
            .ToList();

        Assert.Contains("current", categories);
        Assert.Contains("historical", categories);
        Assert.Contains("beneficiary", categories);
        Assert.Contains("duplicate", categories);
    }

    [Fact]
    public void Headroom_policy_at_least_50_percent()
    {
        var path = RepoPaths.Resolve("docs", "syr", "census-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var headroom = doc.RootElement.GetProperty("headroomPolicy");
        Assert.True(headroom.GetProperty("minimumPercent").GetInt32() >= 50);
        Assert.False(string.IsNullOrEmpty(headroom.GetProperty("overrideAuthority").GetString()));
    }

    [Fact]
    public void Census_defines_source_authorities()
    {
        var path = RepoPaths.Resolve("docs", "syr", "census-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var sources = doc.RootElement.GetProperty("sourceAuthorities").EnumerateArray().ToList();
        Assert.True(sources.Count >= 3);

        var ids = sources.Select(s => s.GetProperty("id").GetString()).ToList();
        Assert.Contains("mof", ids);
        Assert.Contains("mad", ids);
    }

    [Fact]
    public void Census_has_employee_classifications()
    {
        var path = RepoPaths.Resolve("docs", "syr", "census-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var classifications = doc.RootElement.GetProperty("employeeClassifications")
            .EnumerateArray().ToList();
        Assert.True(classifications.Count >= 5);

        var regimes = classifications
            .Select(c => c.GetProperty("legalRegime").GetString())
            .Distinct().ToList();
        Assert.Contains("law50", regimes);
    }

    [Fact]
    public void Census_defines_reconciliation_rules()
    {
        var path = RepoPaths.Resolve("docs", "syr", "census-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var rules = doc.RootElement.GetProperty("reconciliationRules").EnumerateArray().ToList();
        Assert.True(rules.Count >= 2);

        var ruleNames = rules.Select(r => r.GetProperty("rule").GetString()).ToList();
        Assert.Contains("no_duplicate_national_id", ruleNames);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_002_CENSUS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "census-register.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr002-validate.ps1");
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
