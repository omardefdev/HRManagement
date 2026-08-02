using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str006OwnershipPackTests
{

    [Fact]
    public void Matrix_has_journeys_components_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-ownership", "ownership-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("STR-006", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("OwnershipQuad", journeys);
        Assert.Contains("BusFactor", journeys);
        Assert.Contains("StaffingOnCall", journeys);
        Assert.Contains("BudgetTco", journeys);
        Assert.Contains("VendorSla", journeys);

        Assert.True(root.GetProperty("components").GetArrayLength() >= 6);

        var minBf = root.GetProperty("busFactorRule").GetProperty("criticalMinBusFactor").GetInt32();
        foreach (var c in root.GetProperty("components").EnumerateArray())
        {
            if (!c.GetProperty("critical").GetBoolean())
                continue;
            Assert.True(c.GetProperty("busFactor").GetInt32() >= minBf);
            Assert.True(c.GetProperty("backupRoles").GetArrayLength() >= 1);
        }

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["STR-005"]);
        Assert.Equal("Satisfied", blockers["SOV-007"]);
        Assert.Equal("NotStarted", blockers["FIN-001"]);
        Assert.Equal("Blocked", blockers["SRE-001"]);

        Assert.False(root.GetProperty("staffing").GetProperty("national24x7Claim").GetBoolean());
    }

    [Fact]
    public void Budget_funds_ops_security_backup_not_dev_only()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-ownership", "ownership-matrix.json")));
        var must = doc.RootElement.GetProperty("budgetRule").GetProperty("mustFundCategories")
            .EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var funded = doc.RootElement.GetProperty("budgetLines").EnumerateArray()
            .Where(l => l.GetProperty("funded").GetBoolean())
            .Select(l => l.GetProperty("category").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var c in must)
            Assert.Contains(c, funded);
        Assert.False(funded.Count == 1 && funded.Contains("dev"));
    }

    [Fact]
    public void Vendors_have_required_clauses()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-ownership", "ownership-matrix.json")));
        var required = doc.RootElement.GetProperty("requiredVendorClauses")
            .EnumerateArray().Select(x => x.GetString()!).ToArray();
        foreach (var v in doc.RootElement.GetProperty("vendors").EnumerateArray())
        {
            var clauses = v.GetProperty("clauses").EnumerateArray()
                .Select(x => x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var need in required)
                Assert.Contains(need, clauses);
            Assert.True(v.GetProperty("exitAssistance").GetBoolean());
        }
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var pass = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str006", "scenarios", "bus-factor-pass.json")));
        using var fail = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str006", "scenarios", "bus-factor-fail.json")));
        using var tcoBad = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str006", "scenarios", "tco-dev-only.json")));
        using var tcoGood = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str006", "scenarios", "tco-ops-security-backup.json")));
        using var vend = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str006", "vendors", "missing-exit-clause.json")));
        using var staff = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "str006", "scenarios", "staffing-oncall.json")));

        Assert.Equal("PASS", pass.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", fail.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(1, fail.RootElement.GetProperty("busFactor").GetInt32());
        Assert.Equal("FAIL", tcoBad.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", tcoGood.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", vend.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(staff.RootElement.GetProperty("national24x7Claim").GetBoolean());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-006-permanent-ownership-funding-skills-vendors.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_OWNERSHIP_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_SKILL_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_SUCCESSION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_STAFFING_ONCALL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_BUDGET_5Y.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_TCO_APPROVAL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_VENDOR_SLA.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_IP_SOURCE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_KNOWLEDGE_TRANSFER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_EOL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_BUS_FACTOR_EXERCISE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_SIMULATION_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "str006-ownership-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str006-ownership-validate.ps1");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{repoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
