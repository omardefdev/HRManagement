using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-21: dual run on frozen inputs yields bit-for-bit equal salary lines + provenance API.</summary>
public class Ac21PayrollDeterminismTests
{
    [SkippableFact]
    public async Task Dual_run_provenance_and_determinism_diff_empty()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var (token, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-21", correlationId);
        recorder.WriteMetadata("AC-21 payroll determinism (PAY-002 + PAY-003)", processor.Id);

        Environment.SetEnvironmentVariable("ACCESS_TOKEN", token);
        Environment.SetEnvironmentVariable("BASE_URL", E2eEnv.BaseUrl.TrimEnd('/'));
        var reportPath = Path.Combine(recorder.Folder, "determinism-diff.json");
        var (exitCode, scriptOutput) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-payroll-determinism.ps1",
            ("OutputPath", reportPath));

        Assert.True(File.Exists(reportPath), $"Determinism report was not created. Output: {scriptOutput}");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        var root = report.RootElement;
        var runAId = root.GetProperty("runAId").GetGuid();
        var runBId = root.GetProperty("runBId").GetGuid();
        var signaturesMatch = root.GetProperty("signaturesMatch").GetBoolean();
        var configHashMatch = root.GetProperty("configHashMatch").GetBoolean();
        var lineCountA = root.GetProperty("lineCountA").GetInt32();
        var lineCountB = root.GetProperty("lineCountB").GetInt32();
        var differenceCount = root.GetProperty("differences").GetArrayLength();

        recorder.WriteBefore(new { reservedPeriod = "2027-01", runAId });
        recorder.WriteAfter(new
        {
            runBId,
            scriptExitCode = exitCode,
            signaturesMatch,
            configHashMatch,
            lineCountA,
            lineCountB,
            differenceCount,
            scriptOutput
        });
        recorder.WriteDbChecks(new
        {
            runAId,
            runBId,
            signaturesMatch,
            configHashMatch,
            lineCountA,
            lineCountB,
            differenceCount
        });

        Assert.Equal(0, exitCode);
        Assert.True(signaturesMatch);
        Assert.True(configHashMatch);
        Assert.True(lineCountA > 0);
        Assert.Equal(lineCountA, lineCountB);
        Assert.Equal(0, differenceCount);
        recorder.WriteResult("Pass");
    }
}
