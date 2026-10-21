using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-16: duplicate MessageId => single inbox receipt + single AuthUser.</summary>
public class Ac16DuplicateMessageTests
{
    [SkippableFact]
    public async Task Duplicate_IEmployeeCreated_single_auth_user_and_receipt()
    {
        AcceptanceEnv.RequireEnabled();
        var correlationId = Guid.NewGuid().ToString("D");
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, _) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-16", correlationId);
        recorder.WriteMetadata("AC-16 duplicate message idempotency", persona.Id);

        var messageId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var tenantId = AcceptanceSeedIds.AgencyAId;

        recorder.WriteBefore(new { messageId, employeeId, tenantId, receipts = 0 });

        await AcceptanceMessagingHelper.PublishEmployeeCreatedAsync(
            employeeId, tenantId, correlationId, messageId, duplicateCount: 2);

        await Task.Delay(TimeSpan.FromSeconds(5));

        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-inbox-duplicate.ps1",
            ("MessageId", messageId.ToString("D")),
            ("EmployeeId", employeeId.ToString("D")),
            ("TenantId", tenantId.ToString("D")));

        recorder.WriteAfter(new { scriptOutput = output });
        Assert.Equal(0, exitCode);
        recorder.WriteDbChecks(new { messageId, employeeId, expectedReceipts = 1, expectedAuthUsers = 1 });
        recorder.WriteResult("Pass");
    }
}
