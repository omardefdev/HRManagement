using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-21: dual run on frozen inputs yields bit-for-bit equal salary lines + provenance API.
/// PLAN-59 (P-8): no SQL write. The month is AC-21's own, the first of its window in <c>testdata/acceptance/payroll-months.json</c>
/// (2024-01..06: past months governed by the seed's law-50 version from 2000, with no run and no attendance) that agency A has no
/// live regular run of, found by creating the run (<see cref="AcceptancePayrollMonths"/>). The script cancels run A through PLAN-56's
/// API (which frees the period and every input its calculation took, so run B reads the same frozen inputs) and run B at the end;
/// the class cancels whatever the script did not.
/// </summary>
[RequiredAcceptance]
public class Ac21PayrollDeterminismTests
{
    private const string Owner = "AC-21";

    [SkippableFact]
    public async Task Dual_run_provenance_and_determinism_diff_empty()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var (token, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-21", correlationId);
        recorder.WriteMetadata("AC-21 payroll determinism (PAY-002 + PAY-003)", processor.Id);
        using var http = await AcceptanceHarness.CreatePayrollClientAsync(processor, correlationId);

        // A class that ran earlier on this stack may have added an employment record after the seed; the regime gate reads them.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(HRManagement.ServiceDefaults.AcceptanceSeedIds.AgencyAId.ToString("D"));
        var (month, runAId, refused) = await AcceptancePayrollMonths.CreateRunAsync(http, recorder, "ac21", AcceptancePayrollMonths.WindowOf(Owner));
        var periodStart = month.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        recorder.WriteBefore(new { reservedPeriod = AcceptancePayrollMonths.Ym(month), window = "2024-01..2024-06", refused, runAId });

        var reportPath = Path.Combine(recorder.Folder, "determinism-diff.json");
        int exitCode;
        string scriptOutput;
        try
        {
            Environment.SetEnvironmentVariable("ACCESS_TOKEN", token);
            Environment.SetEnvironmentVariable("BASE_URL", E2eEnv.BaseUrl.TrimEnd('/'));
            (exitCode, scriptOutput) = await AcceptanceMessagingHelper.RunScriptAsync(
                "acceptance-payroll-determinism.ps1",
                ("PeriodStart", periodStart),
                ("RunAId", runAId),
                ("OutputPath", reportPath));
        }
        finally
        {
            // The script cancels both runs; if it stopped before, run A is cancelled here (a second cancellation is refused, harmlessly).
            await AcceptancePayrollMonths.CancelRunAsync(http, recorder, "ac21-finally", runAId, "AC-21: determinism experiment ended (PLAN-59)");
        }

        Assert.True(File.Exists(reportPath), $"Determinism report was not created. Output: {scriptOutput}");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        var root = report.RootElement;
        var runBId = root.GetProperty("runBId").GetGuid();
        var signaturesMatch = root.GetProperty("signaturesMatch").GetBoolean();
        var configHashMatch = root.GetProperty("configHashMatch").GetBoolean();
        var lineCountA = root.GetProperty("lineCountA").GetInt32();
        var lineCountB = root.GetProperty("lineCountB").GetInt32();
        var differenceCount = root.GetProperty("differences").GetArrayLength();
        var cancelledThroughApi = root.GetProperty("cancelledThroughApi").GetInt32();

        // Both runs gave their month back: each reads «cancelled» through the API.
        var statusA = await RunStatusAsync(http, runAId);
        var statusB = await RunStatusAsync(http, runBId.ToString("D"));

        recorder.WriteAfter(new
        {
            runBId,
            scriptExitCode = exitCode,
            signaturesMatch,
            configHashMatch,
            lineCountA,
            lineCountB,
            differenceCount,
            cancelledThroughApi,
            statusA,
            statusB,
            scriptOutput
        });
        recorder.WriteDbChecks(new
        {
            source = "API reads (GET api/payroll/runs/{id}, provenance)",
            month = AcceptancePayrollMonths.Ym(month),
            runAId,
            runBId,
            signaturesMatch,
            configHashMatch,
            lineCountA,
            lineCountB,
            differenceCount,
            cancelledThroughApi,
            statusA,
            statusB
        });

        Assert.Equal(0, exitCode);
        Assert.True(signaturesMatch);
        Assert.True(configHashMatch);
        Assert.True(lineCountA > 0);
        Assert.Equal(lineCountA, lineCountB);
        Assert.Equal(0, differenceCount);
        Assert.Equal(2, cancelledThroughApi);
        Assert.Equal(("Cancelled", "Cancelled"), (statusA, statusB));
        recorder.WriteResult("Pass");
    }

    private static async Task<string> RunStatusAsync(HttpClient http, string runId) =>
        AcceptancePayrollMonths.RunStatus(await AcceptancePayrollMonths.ReadRunAsync(http, runId));
}
