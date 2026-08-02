using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod002AgencySelfAdminPackTests
{

    [Fact]
    public void Matrix_has_agency_selfadmin_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-agency", "agency-selfadmin-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("PROD-002", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("delegatedAdmin").GetProperty("noDbaDeveloperRequired").GetBoolean());
        Assert.True(root.GetProperty("nationalControlsImmutable").GetBoolean());
        Assert.True(root.GetProperty("crossTenantAdminForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveSelfAdminForbidden").GetBoolean());

        var scopes = root.GetProperty("delegatedAdmin").GetProperty("scopes").EnumerateArray()
            .Select(s => s.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in new[] { "structure", "roles", "templates", "calendars", "notifications", "quotas" })
            Assert.Contains(need, scopes);

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DailyDelegatedChange", journeys);
        Assert.Contains("NationalAndTenantCeiling", journeys);
        Assert.Contains("Accountability", journeys);
        Assert.Contains("SensitiveFourEyes", journeys);
        Assert.Contains("HostileConcurrent", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["IAM-003"]);
        Assert.Equal("Satisfied", blockers["TEN-003"]);
        Assert.Equal("Satisfied", blockers["PROD-001"]);
        Assert.Equal("Blocked", blockers["live-agency-selfadmin"]);
        Assert.Equal("Blocked", blockers["live-delegated-admin-ui"]);
        Assert.Equal("Blocked", blockers["cross-tenant-admin"]);
        Assert.Equal("Satisfied", blockers["ONB-001"]);
        Assert.Equal("Satisfied", blockers["PROD-003"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-002-agency-selfadmin.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_002_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_002_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod002", "scenarios", "national-control-mutate-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod002", "harness", "selfadmin-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "prod002-agency-selfadmin-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod002-agency-selfadmin-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "prod-agency", "agency-selfadmin-matrix.json");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
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
        Assert.True(proc.ExitCode == 0, $"pack validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
