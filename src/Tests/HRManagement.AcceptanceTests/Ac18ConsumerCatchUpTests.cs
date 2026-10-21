using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-18: stopped Auth.Directory consumer catches up after restart.</summary>
public class Ac18ConsumerCatchUpTests
{
    [SkippableFact]
    public async Task Auth_directory_catch_up_after_consumer_stop()
    {
        AcceptanceEnv.RequireEnabled();
        var correlationId = Guid.NewGuid().ToString("D");
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, _) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-18", correlationId);
        recorder.WriteMetadata("AC-18 consumer catch-up", persona.Id);

        var employeeId = Guid.NewGuid();
        var tenantId = AcceptanceSeedIds.AgencyAId;

        var scriptTask = AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-consumer-catchup.ps1",
            ("EmployeeId", employeeId.ToString("D")),
            ("TenantId", tenantId.ToString("D")));

        var publishTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            await AcceptanceMessagingHelper.PublishEmployeeCreatedAsync(
                employeeId, tenantId, correlationId);
        });

        await Task.WhenAll(scriptTask, publishTask);
        var (exitCode, output) = await scriptTask;
        recorder.WriteDbChecks(new { employeeId, scriptOutput = output });
        Assert.Equal(0, exitCode);
        recorder.WriteResult("Pass");
    }
}
