using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-006: tests for insurance/pension jurisdiction map structure.</summary>
public class Syr006InsuranceTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr006-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Map_defines_GOSI_and_GOIP_institutions()
    {
        var path = RepoPaths.Resolve("docs", "syr", "insurance-jurisdiction-map.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("SYR-006", root.GetProperty("card").GetString());

        var institutions = root.GetProperty("institutions").EnumerateArray().ToList();
        Assert.Equal(2, institutions.Count);

        var ids = institutions.Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Contains("GOSI", ids);
        Assert.Contains("GOIP", ids);
    }

    [Fact]
    public void Active_employees_route_to_GOSI_not_GOIP()
    {
        var path = RepoPaths.Resolve("docs", "syr", "insurance-jurisdiction-map.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var rule = doc.RootElement.GetProperty("jurisdictionRules").EnumerateArray()
            .First(r => r.GetProperty("ruleId").GetString() == "ACTIVE_EMPLOYEE_CONTRIBUTION");

        Assert.Equal("GOSI", rule.GetProperty("routeTo").GetString());
        var forbidden = rule.GetProperty("forbidRouteTo").EnumerateArray()
            .Select(f => f.GetString()).ToList();
        Assert.Contains("GOIP", forbidden);
    }

    [Fact]
    public void Retired_employees_route_to_GOIP()
    {
        var path = RepoPaths.Resolve("docs", "syr", "insurance-jurisdiction-map.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var rule = doc.RootElement.GetProperty("jurisdictionRules").EnumerateArray()
            .First(r => r.GetProperty("ruleId").GetString() == "RETIRED_PENSION_PAYMENT");

        Assert.Equal("GOIP", rule.GetProperty("routeTo").GetString());
    }

    [Fact]
    public void Map_defines_contribution_rates_from_decree_135()
    {
        var path = RepoPaths.Resolve("docs", "syr", "insurance-jurisdiction-map.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var rates = doc.RootElement.GetProperty("contributionRates").EnumerateArray().ToList();
        Assert.True(rates.Count >= 1);

        var rate = rates[0];
        Assert.Equal("SYR-DEC-135-2026", rate.GetProperty("legalSourceId").GetString());
        Assert.True(rate.GetProperty("employeePercent").GetInt32() > 0);
        Assert.True(rate.GetProperty("employerPercent").GetInt32() > 0);
    }

    [Fact]
    public void Map_forbids_duplicate_beneficiary()
    {
        var path = RepoPaths.Resolve("docs", "syr", "insurance-jurisdiction-map.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("beneficiaryRules")
            .GetProperty("forbidDuplicateBeneficiary").GetBoolean());
    }

    [Fact]
    public void Law_amendment_requires_new_versioned_pack()
    {
        var path = RepoPaths.Resolve("docs", "syr", "insurance-jurisdiction-map.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("lawAmendmentPolicy");

        Assert.True(policy.GetProperty("forbidManualPatch").GetBoolean());
        Assert.True(policy.GetProperty("requiresNewVersionedPack").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_006_INSURANCE_PENSIONS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "insurance-jurisdiction-map.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr006-validate.ps1");
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
