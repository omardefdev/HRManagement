namespace HRManagement.AcceptanceTests;

/// <summary>AC-22: kill/pause mid-partition, resume completes with no duplicate lines.</summary>
public class Ac22PayrollResumeAfterKillTests
{
    public static bool ChaosEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_CHAOS"), "true", StringComparison.OrdinalIgnoreCase);

    [SkippableFact]
    public async Task Resume_after_kill_completes_without_duplicate_lines()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var (token, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-22", correlationId);
        recorder.WriteMetadata("AC-22 payroll resume after kill (PAY-003)", processor.Id);

        Environment.SetEnvironmentVariable("ACCESS_TOKEN", token);
        Environment.SetEnvironmentVariable("BASE_URL", HRManagement.E2E.E2eEnv.BaseUrl.TrimEnd('/'));

        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-payroll-resume-chaos.ps1",
            ("OutputDir", recorder.Folder));

        recorder.WriteBefore(new { runChaos = ChaosEnabled });
        recorder.WriteDbChecks(new { scriptOutput = output });
        recorder.FlushTiming();

        Assert.True(exitCode == 0, $"AC-22 resume script failed: {output}");
        var resultPath = Path.Combine(recorder.Folder, "result.json");
        Assert.True(File.Exists(resultPath));
        using var result = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(result.RootElement.GetProperty("midPartitionObserved").GetBoolean());
        Assert.True(result.RootElement.GetProperty("interruptionApplied").GetBoolean());
        Assert.Equal(0, result.RootElement.GetProperty("salaryLines")
            .GetProperty("duplicateEmployeeGroups").GetInt32());
        recorder.WriteAfter(result.RootElement.Clone());
        recorder.WriteResult("Pass");
    }
}
