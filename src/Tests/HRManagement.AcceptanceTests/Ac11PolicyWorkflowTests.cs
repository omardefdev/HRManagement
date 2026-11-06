using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-11: Policy Enforce says an approval is needed, the owning service starts the multi-step workflow, signatures are stored and
/// stage-skip is blocked. PLAN-43: the requester is a third person, because a requester acts on no step. PLAN-45 makes this a UI
/// journey.
/// </summary>
public class Ac11PolicyWorkflowTests
{
    [SkippableFact]
    public async Task Enforce_requires_a_workflow_the_owner_starts_two_approvals_signatures_stage_skip_blocked()
    {
        AcceptanceEnv.RequireEnabled();
        // AC-10 intentionally publishes Agency A's deny version. Use Agency B's
        // independent published policy so this workflow scenario is order-safe.
        var requester = AcceptanceHarness.RequirePersona("b-logistics");
        var initiator = AcceptanceHarness.RequirePersona("b-branch-manager");
        var finalApprover = AcceptanceHarness.RequirePersona("b-approver");
        var (requesterToken, correlationId) = await AcceptanceHarness.LoginAsync(requester);
        var requesterId = Guid.Parse(AcceptanceHarness.ParseJwt(requesterToken).Subject);
        var recorder = new EvidenceRecorder("AC-11", correlationId);
        recorder.WriteMetadata("AC-11 policy workflow + signatures", initiator.Id);

        using var httpInitiator = await AcceptanceHarness.CreateAuthorizedClientAsync(initiator, correlationId);
        using var httpCentral = await AcceptanceHarness.CreateAuthorizedClientAsync(finalApprover, correlationId);

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

        // PLAN-43 (rule 10): the owner starts, and the server takes the requester from X-Acting-User.
        var startResp = await logistics.PostAsync("api/workflow-instances", JsonContent(new WorkflowApprovalStartRequest(
            "AssetAssignment",
            assignmentId,
            "ASSET-ASSIGN-APPROVAL",
            $"AssetAssignment:{assignmentId:D}",
            [],
            null,
            "AC-11 custody approval",
            $"/ui/admin-logistics/Assets/Details/{assignmentId:D}",
            new Dictionary<string, string> { ["assetId"] = Guid.NewGuid().ToString("D"), ["employeeId"] = Guid.NewGuid().ToString("D") })));
        var startBody = await startResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("start-as-owner", "POST", "/api/workflow-instances",
            (int)startResp.StatusCode, JsonSerializer.Deserialize<object>(startBody));
        Assert.Equal(System.Net.HttpStatusCode.Created, startResp.StatusCode);

        Guid workflowInstanceId;
        using (var doc = JsonDocument.Parse(startBody))
        {
            workflowInstanceId = doc.RootElement.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, workflowInstanceId);
        }

        var instanceResp = await httpInitiator.GetAsync($"api/hr-core/api/workflow-instances/{workflowInstanceId:D}");
        var instanceBody = await instanceResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("get-instance", "GET", $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}",
            (int)instanceResp.StatusCode, JsonSerializer.Deserialize<object>(instanceBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, instanceResp.StatusCode);

        var skipPayload = new { comments = "skip attempt", targetStepLevel = 2 };
        var skipResp = await httpInitiator.PostAsync(
            $"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            JsonContent(skipPayload));
        var skipBody = await skipResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("approve-skip-negative", "POST",
            $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            (int)skipResp.StatusCode, skipBody);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, skipResp.StatusCode);

        var step1Payload = new { comments = "step1 ok", targetStepLevel = 1 };
        var step1Resp = await httpInitiator.PostAsync(
            $"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            JsonContent(step1Payload));
        var step1Body = await step1Resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("approve-step1", "POST",
            $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            (int)step1Resp.StatusCode, JsonSerializer.Deserialize<object>(step1Body));
        Assert.Equal(System.Net.HttpStatusCode.OK, step1Resp.StatusCode);

        var firstApproverStep2Resp = await httpInitiator.PostAsync(
            $"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            JsonContent(new { comments = "first approver on step 2, negative" }));
        recorder.CaptureHttp("approve-step2-first-approver-negative", "POST",
            $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            (int)firstApproverStep2Resp.StatusCode, await firstApproverStep2Resp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, firstApproverStep2Resp.StatusCode);

        var step2Resp = await httpCentral.PostAsync(
            $"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            JsonContent(new { comments = "step2 ok", targetStepLevel = 2 }));
        var step2Body = await step2Resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("approve-step2", "POST",
            $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            (int)step2Resp.StatusCode, ParseEvidenceBody(step2Body));
        Assert.Equal(System.Net.HttpStatusCode.OK, step2Resp.StatusCode);

        var historyResp = await httpCentral.GetAsync($"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/history");
        var historyBody = await historyResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("history", "GET", $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/history",
            (int)historyResp.StatusCode, JsonSerializer.Deserialize<object>(historyBody));

        var sigResp = await httpCentral.GetAsync(
            $"api/hr-core/api/signatures?subjectType=AssetAssignment&subjectId={assignmentId:D}");
        var sigBody = await sigResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("signatures", "GET", $"/api/hr-core/api/signatures?subjectType=AssetAssignment",
            (int)sigResp.StatusCode, JsonSerializer.Deserialize<object>(sigBody));

        var ledgerResp = await httpInitiator.GetAsync(
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
            assignmentId,
            signatureCount = sigDoc.RootElement.GetArrayLength(),
            historySteps = 2,
            stageSkipBlocked = true,
            firstApproverBlockedOnStep2 = true,
            requesterId
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

    private static object? ParseEvidenceBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try { return JsonSerializer.Deserialize<object>(body); }
        catch (JsonException) { return body; }
    }
}
