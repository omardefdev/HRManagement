using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Mig007AgencyOnboardingPackTests
{

    [Fact]
    public void Matrix_has_gates_nofork_agencies_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-onboarding", "onboarding-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("MIG-007", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ReadinessChecklist", journeys);
        Assert.Contains("NoForkPlaybook", journeys);
        Assert.Contains("WaveSignOffGate", journeys);
        Assert.Contains("DualAgencyE2E", journeys);
        Assert.Contains("LeadTimeDefects", journeys);
        Assert.Contains("ExitRollback", journeys);

        Assert.True(root.GetProperty("readinessGates").GetArrayLength() >= 7);
        Assert.True(root.GetProperty("playbook").GetProperty("noforkRequired").GetBoolean());
        Assert.True(root.GetProperty("waveCriteria").GetProperty("signOffsRequiredBeforeWave").GetBoolean());
        Assert.True(root.GetProperty("agencies").GetArrayLength() >= 2);

        var owners = root.GetProperty("requiredOwners").EnumerateArray()
            .Select(x => x.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("data-owner", owners);
        Assert.Contains("security-owner", owners);
        Assert.Contains("ops-owner", owners);

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["MIG-006"]);
        Assert.Equal("Satisfied", blockers["STR-004"]);
        Assert.Equal("NotStarted", blockers["EXT-006"]);
        Assert.Equal("Satisfied", blockers["PROD-002"]);
        Assert.Equal("Satisfied", blockers["ONB-001"]);
        Assert.Equal("Blocked", blockers["agency-extract-access"]);
        Assert.Equal("Blocked", blockers["live-dual-agency-onboard"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var chk = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig007", "checklist", "readiness-checklist-pass.json")));
        using var wave = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig007", "scenarios", "wave-without-signoff-fail.json")));
        using var fork = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig007", "scenarios", "fork-attempt-fail.json")));
        using var a = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig007", "agencies", "agency-a-e2e.json")));
        using var b = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig007", "agencies", "agency-b-e2e.json")));
        using var lt = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig007", "metrics", "lead-time-pass.json")));
        using var def = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig007", "metrics", "defects-telemetry.json")));

        Assert.Equal("PASS", chk.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("FAIL", wave.RootElement.GetProperty("expectedValidate").GetString());
        Assert.False(wave.RootElement.GetProperty("signOffsComplete").GetBoolean());
        Assert.Equal("FAIL", fork.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(fork.RootElement.GetProperty("proposedFork").GetBoolean());
        Assert.Equal("PASS", a.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("PASS", b.RootElement.GetProperty("expectedValidate").GetString());
        Assert.NotEqual(
            a.RootElement.GetProperty("profile").GetString(),
            b.RootElement.GetProperty("profile").GetString());
        Assert.False(a.RootElement.GetProperty("forkUsed").GetBoolean());
        Assert.Equal("PASS", lt.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(lt.RootElement.GetProperty("withinTarget").GetBoolean());
        Assert.Equal("PASS", def.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(def.RootElement.GetProperty("withinBudget").GetBoolean());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-007-agency-onboarding-factory.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_READINESS_ASSESSMENT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_PLAYBOOK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_SANDBOX_CERT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_WAVE_CRITERIA.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_OWNERS_AND_ROLES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_TELEMETRY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_EXIT_ROLLBACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "mig007-onboarding-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig007-onboarding-validate.ps1");
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
