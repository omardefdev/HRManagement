using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-11: Policy Enforce says an approval is needed, and a real handover goes through both steps on the pages a person uses, with its
/// signatures, and stage-skip blocked. The requester acts on no step.
/// </summary>
[RequiredAcceptance]
public class Ac11PolicyWorkflowTests
{
    [SkippableFact]
    public async Task Enforce_requires_an_approval_and_a_real_handover_goes_through_both_steps_to_active()
    {
        AcceptanceEnv.RequireEnabled();
        // AC-10 intentionally publishes Agency A's deny version. Use Agency B's
        // independent published policy so this workflow scenario is order-safe.
        var requester = AcceptanceHarness.RequirePersona("b-logistics");
        var (requesterToken, correlationId) = await AcceptanceHarness.LoginAsync(requester);
        var requesterId = Guid.Parse(AcceptanceHarness.ParseJwt(requesterToken).Subject);
        var recorder = new EvidenceRecorder("AC-11", correlationId);
        recorder.WriteMetadata("AC-11 policy decision, a real handover through both steps, signatures", requester.Id);

        // PLAN-42: enforce belongs to the service that owns the domain, on its own identity; the user travels in X-Acting-User.
        using var logistics = await AcceptanceHarness.CreateServiceClientAsync(
            "svc-admin-logistics", ServiceScopes.HrCoreWorkflow, recorder, AcceptanceSeedIds.AgencyBId, requesterId);

        var assignmentId = Guid.NewGuid();
        var enforceReq = new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            TenantId = AcceptanceSeedIds.AgencyBId,
            SubjectType = "AssetAssignment",
            EntityId = assignmentId,
            Action = "Assign",
            Facts = new Dictionary<string, string>
            {
                ["isPrivileged"] = "true",
                ["value"] = "15000",
                ["action"] = "Assign"
            },
            CorrelationId = correlationId
        };

        var enforceResp = await logistics.PostAsync(
            "api/internal/policies/enforce",
            JsonContent(enforceReq));
        var enforceBody = await enforceResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("enforce-trigger-workflow", "POST", "/api/internal/policies/enforce",
            (int)enforceResp.StatusCode, JsonSerializer.Deserialize<object>(enforceBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, enforceResp.StatusCode);

        using (var doc = JsonDocument.Parse(enforceBody))
        {
            Assert.True(doc.RootElement.GetProperty("allowed").GetBoolean());
            Assert.Contains(doc.RootElement.GetProperty("decision").GetProperty("effects").EnumerateArray(), IsTriggerWorkflow);
            Assert.True(
                !doc.RootElement.TryGetProperty("workflowInstanceId", out var noInstance) || noInstance.ValueKind == JsonValueKind.Null,
                "PLAN-43: enforce only says an approval is needed");
        }

        // The journey runs in agency A, which holds the logistics maker and checker. admin-logistics-web decides by itself on this stack, so
        // AC-10's deny version does not reach it.
        var (asset, assignment, workflowInstanceId) = await CustodyJourney.PrivilegedHandoverAsync();
        using var httpCentral = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("a-management-approver"), correlationId);

        var instanceResp = await httpCentral.GetAsync($"api/hr-core/api/workflow-instances/{workflowInstanceId:D}");
        var instanceBody = await instanceResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("get-instance", "GET", $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}",
            (int)instanceResp.StatusCode, JsonSerializer.Deserialize<object>(instanceBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, instanceResp.StatusCode);

        var skipResp = await httpCentral.PostAsync(
            $"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            JsonContent(new { comments = "skip attempt", targetStepLevel = 2 }));
        var skipBody = await skipResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("approve-skip-negative", "POST",
            $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            (int)skipResp.StatusCode, skipBody);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, skipResp.StatusCode);

        await CustodyJourney.ApproveFromInboxAsync("a-logistics-checker", workflowInstanceId, recorder);

        using var checker = await AcS2s42TrustBoundaryTests.UiClientAsync("a-logistics-checker");
        Assert.False(await CustodyJourney.HasFormAsync(checker, CustodyJourney.ApprovalPage(workflowInstanceId), CustodyJourney.ApproveAction));

        await CustodyJourney.ApproveFromInboxAsync("a-management-approver", workflowInstanceId, recorder);
        Assert.Equal(CustodyJourney.Active, await CustodyJourney.PollAsync(() => CustodyJourney.AssignmentStatusAsync(assignment), CustodyJourney.Active));
        Assert.Equal(CustodyJourney.AssetInUse, await CustodyJourney.AssetStatusAsync(asset));

        var historyResp = await httpCentral.GetAsync($"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/history");
        var historyBody = await historyResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("history", "GET", $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/history",
            (int)historyResp.StatusCode, JsonSerializer.Deserialize<object>(historyBody));

        var sigResp = await httpCentral.GetAsync(
            $"api/hr-core/api/signatures?subjectType=AssetAssignment&subjectId={assignment:D}");
        var sigBody = await sigResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("signatures", "GET", $"/api/hr-core/api/signatures?subjectType=AssetAssignment",
            (int)sigResp.StatusCode, JsonSerializer.Deserialize<object>(sigBody));

        var ledgerResp = await httpCentral.GetAsync(
            $"api/hr-core/api/policies/ledger?domain={PolicyDomains.AssetsCustody}&correlationId={correlationId}");
        recorder.CaptureHttp("ledger", "GET", "/api/hr-core/api/policies/ledger",
            (int)ledgerResp.StatusCode, await ledgerResp.Content.ReadAsStringAsync());

        using var histDoc = JsonDocument.Parse(historyBody);
        Assert.Equal(2, histDoc.RootElement.GetArrayLength());

        using var sigDoc = JsonDocument.Parse(sigBody);
        Assert.True(sigDoc.RootElement.GetArrayLength() >= 2);

        recorder.WriteDbChecks(new
        {
            workflowInstanceId,
            assignment,
            asset,
            signatureCount = sigDoc.RootElement.GetArrayLength(),
            historySteps = 2,
            stageSkipBlocked = true,
            firstApproverBlockedOnStep2 = true,
            status = "Active"
        });
        recorder.WriteAfter(new { status = "Completed", workflowInstanceId });
        recorder.WriteResult("Pass");
    }

    private static bool IsTriggerWorkflow(JsonElement effect) =>
        effect.ValueKind == JsonValueKind.String
            ? effect.GetString() == nameof(PolicyEffect.TriggerWorkflow)
            : effect.GetInt32() == (int)PolicyEffect.TriggerWorkflow;

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
