using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Mig006CutoverRehearsalPackTests
{

    [Fact]
    public void Matrix_has_rehearsals_rollback_gonogo_and_blockers()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "mig-cutover", "cutover-matrix.json")));
        var root = doc.RootElement;
        Assert.Equal("MIG-006", root.GetProperty("card").GetString());

        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TimedRehearsals", journeys);
        Assert.Contains("DeltaIntegrity", journeys);
        Assert.Contains("SurpriseRollback", journeys);
        Assert.Contains("GoNoGoGate", journeys);
        Assert.Contains("LegacySecured", journeys);
        Assert.Contains("PostCutoverRecon", journeys);

        Assert.True(root.GetProperty("minRehearsals").GetInt32() >= 3);
        Assert.True(root.GetProperty("rehearsals").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("rollback").GetProperty("mustBeExercised").GetBoolean());
        Assert.True(root.GetProperty("rollback").GetProperty("paperOnlyForbidden").GetBoolean());
        Assert.True(root.GetProperty("deltaCapture").GetProperty("noLossRequired").GetBoolean());
        Assert.True(root.GetProperty("goNoGo").GetProperty("decisionRequired").GetBoolean());

        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["MIG-005"]);
        Assert.Equal("Satisfied", blockers["OPS-003"]);
        Assert.Equal("Satisfied", blockers["REL-001"]);
        Assert.Equal("Blocked", blockers["agency-extract-access"]);
        Assert.Equal("Blocked", blockers["live-cutover-perf"]);
        Assert.Equal("Blocked", blockers["live-agency-recon"]);
        Assert.Equal("Blocked", blockers["live-rehearsal-at-scale"]);
    }

    [Fact]
    public void Simulation_fixtures_expected_outcomes()
    {
        using var timed = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig006", "rehearsals", "timed-rehearsal-pass.json")));
        using var dPass = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig006", "scenarios", "delta-capture-pass.json")));
        using var dFail = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig006", "scenarios", "delta-loss-fail.json")));
        using var rbPass = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig006", "scenarios", "surprise-rollback-pass.json")));
        using var rbFail = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig006", "scenarios", "paper-rollback-fail.json")));
        using var go = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig006", "decision", "go-nogo-decision.json")));
        using var t0 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("testdata", "mig006", "recon", "recon-t0.json")));

        Assert.Equal("PASS", timed.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(timed.RootElement.GetProperty("rehearsalIds").GetArrayLength() >= 3);
        Assert.Equal("PASS", dPass.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal(0, dPass.RootElement.GetProperty("lost").GetInt32());
        Assert.Equal("FAIL", dFail.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(dFail.RootElement.GetProperty("lost").GetInt32() > 0);
        Assert.Equal("PASS", rbPass.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(rbPass.RootElement.GetProperty("exercised").GetBoolean());
        Assert.Equal("FAIL", rbFail.RootElement.GetProperty("expectedValidate").GetString());
        Assert.True(rbFail.RootElement.GetProperty("paperOnly").GetBoolean());
        Assert.Equal("PASS", go.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("Go", go.RootElement.GetProperty("decision").GetString());
        Assert.Equal("PASS", t0.RootElement.GetProperty("expectedValidate").GetString());
        Assert.Equal("T+0", t0.RootElement.GetProperty("checkpoint").GetString());
    }

    [Fact]
    public void Required_docs_and_script_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-006-cutover-rehearsal-rollback.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_JOURNEY_MATRIX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_REHEARSAL_PLAYBOOK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_CHANGE_FREEZE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_DELTA_CAPTURE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_PARALLEL_AND_LOCK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_ROLLBACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_GO_NOGO.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_POST_CUTOVER_RECON.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_EXCEPTION_PATHS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_RECONCILIATION_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_READINESS_CHECKLIST.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_UAT_PACK.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_006_VERIFICATION.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_002_MIGRATION_REHEARSAL.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "mig006-cutover-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig006-cutover-validate.ps1");
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
