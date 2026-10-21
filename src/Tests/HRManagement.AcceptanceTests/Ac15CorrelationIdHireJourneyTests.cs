using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-15: one CorrelationId visible across gateway + services + Rabbit.</summary>
public class Ac15CorrelationIdHireJourneyTests
{
    [SkippableFact]
    public async Task Hire_journey_preserves_correlation_id_across_hops()
    {
        AcceptanceEnv.RequireEnabled();
        var correlationId = Guid.NewGuid().ToString("D");
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, _) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-15", correlationId);
        recorder.WriteMetadata("AC-15 correlation trace hire journey", persona.Id);
        recorder.WriteSteps("""
            1. Gateway requests with fixed X-Correlation-ID.
            2. Publish IEmployeeCreated with same correlation (Auth.Directory consumer).
            3. Run acceptance-correlation-trace.ps1 (audit + logs + rabbit).
            """);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var lookupResp = await http.GetAsync("api/hr-core/api/lookup/employees?q=فرع&page=1&pageSize=5");
        var lookupBody = await lookupResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("lookup-employees", "GET", "/api/hr-core/api/lookup/employees",
            (int)lookupResp.StatusCode, JsonSerializer.Deserialize<object>(lookupBody));
        Assert.True(lookupResp.IsSuccessStatusCode, lookupBody);
        Assert.Contains(correlationId, lookupResp.Headers.GetValues(CorrelationIdMiddleware.HeaderName));

        var contractsResp = await http.GetAsync("api/hr-contracts/health/live");
        recorder.CaptureHttp("contracts-health", "GET", "/api/hr-contracts/health/live",
            (int)contractsResp.StatusCode, null);
        Assert.Contains(correlationId, contractsResp.Headers.GetValues(CorrelationIdMiddleware.HeaderName));

        var employeeId = Guid.NewGuid();
        var messageId = await AcceptanceMessagingHelper.PublishEmployeeCreatedAsync(
            employeeId, AcceptanceSeedIds.AgencyAId, correlationId);

        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-correlation-trace.ps1",
            ("CorrelationId", correlationId),
            ("EmployeeId", employeeId.ToString("D")),
            ("MessageId", messageId.ToString("D")),
            ("SinceMinutes", "5"));

        var trace = AcceptanceMessagingHelper.TryParseJson(output);
        recorder.WriteAudit(new { correlationId, traceFound = trace?.RootElement.GetProperty("foundCount").GetInt32() });
        if (trace is not null)
            File.WriteAllText(Path.Combine(recorder.Folder, "correlation-trace.json"), trace.RootElement.GetRawText());

        Assert.Equal(0, exitCode);
        Assert.NotNull(trace);
        Assert.True(trace.RootElement.GetProperty("ok").GetBoolean());
        recorder.WriteDbChecks(new { employeeId, messageId, correlationId, hopsRequired = 5 });
        recorder.WriteResult("Pass");
    }
}
