namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-22: kill/pause mid-partition, resume completes with no duplicate lines.
/// PLAN-59 (P-8, P-11): no SQL write. The month is AC-22's own, the first of its window in <c>testdata/acceptance/payroll-months.json</c>
/// (2024-07..12) that agency A has no live regular run of, found by creating the run (<see cref="AcceptancePayrollMonths"/>); the script
/// cancels the run through PLAN-56's API at the end, and the class cancels it if the script did not. The class stays out of CI
/// (<c>excluded[]</c>: it kills Payroll on a shared stack, QA-G's drill) and is proved on the local prod-like stack.
/// </summary>
[RequiredAcceptance]
public class Ac22PayrollResumeAfterKillTests
{
    private const string Owner = "AC-22";

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
        using var http = await AcceptanceHarness.CreatePayrollClientAsync(processor, correlationId);

        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(HRManagement.ServiceDefaults.AcceptanceSeedIds.AgencyAId.ToString("D"));
        var (month, runId, refused) = await AcceptancePayrollMonths.CreateRunAsync(http, recorder, "ac22", AcceptancePayrollMonths.WindowOf(Owner));
        recorder.WriteBefore(new { runChaos = ChaosEnabled, reservedPeriod = AcceptancePayrollMonths.Ym(month), window = "2024-07..2024-12", refused, runId });

        int exitCode;
        string output;
        try
        {
            Environment.SetEnvironmentVariable("ACCESS_TOKEN", token);
            Environment.SetEnvironmentVariable("BASE_URL", HRManagement.E2E.E2eEnv.BaseUrl.TrimEnd('/'));
            (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
                "acceptance-payroll-resume-chaos.ps1",
                ("PeriodStart", month.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                ("RunId", runId),
                ("OutputDir", recorder.Folder));
        }
        finally
        {
            // Payroll was killed and started again: wait until it answers its own read of the run, then cancel the run if the script did
            // not (a second cancellation is refused, harmlessly).
            await WaitForPayrollAsync(http, runId);
            await AcceptancePayrollMonths.CancelRunAsync(http, recorder, "ac22-finally", runId, "AC-22: resume drill ended (PLAN-59)");
        }

        recorder.WriteDbChecks(new { month = AcceptancePayrollMonths.Ym(month), runId, scriptExitCode = exitCode, scriptOutput = output });
        var status = AcceptancePayrollMonths.RunStatus(await AcceptancePayrollMonths.ReadRunAsync(http, runId));
        recorder.WriteDbChecks(new { month = AcceptancePayrollMonths.Ym(month), runId, statusAfter = status, scriptExitCode = exitCode, scriptOutput = output });
        recorder.FlushTiming();

        Assert.True(exitCode == 0, $"AC-22 resume script failed: {output}");
        var resultPath = Path.Combine(recorder.Folder, "result.json");
        Assert.True(File.Exists(resultPath));
        using var result = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(result.RootElement.GetProperty("midPartitionObserved").GetBoolean());
        Assert.True(result.RootElement.GetProperty("interruptionApplied").GetBoolean());
        Assert.True(result.RootElement.GetProperty("cancelledThroughApi").GetBoolean());
        Assert.Equal(0, result.RootElement.GetProperty("salaryLines")
            .GetProperty("duplicateEmployeeGroups").GetInt32());
        Assert.Equal("Cancelled", status);
        recorder.WriteAfter(result.RootElement.Clone());
        recorder.WriteResult("Pass");
    }

    /// <summary>Waits (up to three minutes) until Payroll, started again after the kill, answers its read of the run.</summary>
    private static async Task WaitForPayrollAsync(HttpClient http, string runId)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await http.GetAsync($"api/payroll/runs/{runId}");
            if (response.IsSuccessStatusCode)
                return;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
