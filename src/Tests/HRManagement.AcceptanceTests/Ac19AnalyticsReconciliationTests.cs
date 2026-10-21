using System.Net;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-19: analytics read-model drift detection + reconcile + documented report.</summary>
public class Ac19AnalyticsReconciliationTests
{
    [SkippableFact]
    public async Task Reconcile_detects_corrects_and_idempotent_second_run()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("a-analytics");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-19", correlationId);
        recorder.WriteMetadata("AC-19 analytics read-model reconciliation", persona.Id);

        var discoveryPath = Path.Combine(recorder.Folder, "read-model-discovery.json");
        var startedAt = DateTime.UtcNow;
        var (scriptExit, scriptOutput) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-analytics-reconcile.ps1",
            ("OutputPath", discoveryPath));
        Assert.Equal(0, scriptExit);
        Assert.True(File.Exists(discoveryPath), $"Read-model discovery output missing: {scriptOutput}");

        using var discovery = JsonDocument.Parse(await File.ReadAllTextAsync(discoveryPath));
        var readModelId = discovery.RootElement.GetProperty("readModelId").GetGuid();
        Assert.True(discovery.RootElement.GetProperty("driftInjected").GetBoolean());
        recorder.WriteBefore(discovery.RootElement.GetProperty("before").Clone());

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var reconcileBase = $"api/dev-analytics/api/analytics/read-models/{readModelId:D}/reconcile";

        var dryResp = await http.PostAsync($"{reconcileBase}?dryRun=true", null);
        var dryBody = await dryResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-reconcile-dryrun", "POST", reconcileBase + "?dryRun=true",
            (int)dryResp.StatusCode, JsonSerializer.Deserialize<object>(dryBody));

        Assert.Equal(HttpStatusCode.OK, dryResp.StatusCode);
        using var dryDoc = JsonDocument.Parse(dryBody);
        Assert.True(dryDoc.RootElement.GetProperty("driftDetected").GetBoolean());
        Assert.True(dryDoc.RootElement.GetProperty("dryRun").GetBoolean());

        var liveResp = await http.PostAsync($"{reconcileBase}?dryRun=false", null);
        var liveBody = await liveResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-reconcile-live", "POST", reconcileBase + "?dryRun=false",
            (int)liveResp.StatusCode, JsonSerializer.Deserialize<object>(liveBody));
        Assert.Equal(HttpStatusCode.OK, liveResp.StatusCode);
        using var liveDoc = JsonDocument.Parse(liveBody);
        Assert.True(liveDoc.RootElement.GetProperty("driftDetected").GetBoolean());
        Assert.True(liveDoc.RootElement.GetProperty("corrected").GetBoolean());

        var secondResp = await http.PostAsync($"{reconcileBase}?dryRun=false", null);
        var secondBody = await secondResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-reconcile-second", "POST", reconcileBase + "?dryRun=false",
            (int)secondResp.StatusCode, JsonSerializer.Deserialize<object>(secondBody));

        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);
        using var secondDoc = JsonDocument.Parse(secondBody);
        Assert.False(secondDoc.RootElement.GetProperty("driftDetected").GetBoolean());
        Assert.False(secondDoc.RootElement.GetProperty("corrected").GetBoolean());

        recorder.WriteAfter(new
        {
            readModelId,
            liveCorrected = true,
            secondRunDriftDetected = false
        });
        recorder.WriteDbChecks(new { scriptExitCode = scriptExit, scriptOutput, readModelId });
        recorder.RecordTiming("elapsedMs", (DateTime.UtcNow - startedAt).TotalMilliseconds);
        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }
}
