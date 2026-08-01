using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Prod003EmployeeSelfServicePackTests
{

    [Fact]
    public void Matrix_has_employee_selfservice_contracts_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-ess", "employee-selfservice-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("PROD-003", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("highVolumeJourneys").GetProperty("required").GetBoolean());
        Assert.True(root.GetProperty("highVolumeJourneys").GetProperty("exceptionOnlyBackOffice").GetBoolean());
        Assert.True(root.GetProperty("correctionVisibility").GetProperty("sourceVisible").GetBoolean());
        Assert.True(root.GetProperty("correctionVisibility").GetProperty("statusVisible").GetBoolean());
        Assert.True(root.GetProperty("managerScope").GetProperty("enforced").GetBoolean());
        Assert.True(root.GetProperty("managerScope").GetProperty("outsideScopeForbidden").GetBoolean());
        Assert.True(root.GetProperty("usageTelemetry").GetProperty("liveForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveEmployeePortalForbidden").GetBoolean());
        Assert.True(root.GetProperty("mockSandbox").GetProperty("liveManagerPortalForbidden").GetBoolean());

        var hv = root.GetProperty("highVolumeJourneys").GetProperty("journeys").EnumerateArray()
            .Select(s => s.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in new[]
                 {
                     "profile-correction", "leave-request", "payslip-tax", "documents",
                     "manager-approvals", "delegation", "inbox"
                 })
            Assert.Contains(need, hv);

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("HighVolumeE2E", journeys);
        Assert.Contains("CorrectionVisibility", journeys);
        Assert.Contains("ManagerScope", journeys);
        Assert.Contains("UsageAndSession", journeys);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["IAM-003"]);
        Assert.Equal("Satisfied", blockers["FUN-001"]);
        Assert.Equal("Satisfied", blockers["UX-002"]);
        Assert.Equal("NotStarted", blockers["IAM-002"]);
        Assert.Equal("Blocked", blockers["live-employee-portal"]);
        Assert.Equal("Blocked", blockers["live-manager-portal"]);
        Assert.Equal("Blocked", blockers["live-usage-telemetry"]);
        Assert.Equal("NotStarted", blockers["ESS-001"]);
        Assert.Equal("NotStarted", blockers["MSS-001"]);
        Assert.Equal("Satisfied", blockers["PROD-004"]);
    }

    [Fact]
    public void Pack_docs_and_fixtures_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-003-employee-selfservice.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_003_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_003_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod003", "scenarios", "backoffice-required-fail.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("testdata", "prod003", "harness", "ess-run-ledger.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "prod003-employee-selfservice-validate.ps1")));
    }

    [Fact]
    public void Pack_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod003-employee-selfservice-validate.ps1");
        var matrix = RepoPaths.Resolve("docs", "prod-ess", "employee-selfservice-matrix.json");
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
