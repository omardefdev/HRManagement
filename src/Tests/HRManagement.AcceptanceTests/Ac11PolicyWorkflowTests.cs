using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-11: Policy Enforce triggers multi-step workflow; signatures stored; stage-skip blocked.</summary>
public class Ac11PolicyWorkflowTests
{
    [SkippableFact]
    public async Task Enforce_starts_workflow_two_approvals_signatures_stage_skip_blocked()
    {
        AcceptanceEnv.RequireEnabled();
        // AC-10 intentionally publishes Agency A's deny version. Use Agency B's
        // independent published policy so this workflow scenario is order-safe.
        var initiator = AcceptanceHarness.RequirePersona("b-branch-manager");
        var finalApprover = AcceptanceHarness.RequirePersona("b-approver");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(initiator);
        var recorder = new EvidenceRecorder("AC-11", correlationId);
        recorder.WriteMetadata("AC-11 policy workflow + signatures", initiator.Id);

        using var httpInitiator = await AcceptanceHarness.CreateAuthorizedClientAsync(initiator, correlationId);
        using var httpCentral = await AcceptanceHarness.CreateAuthorizedClientAsync(finalApprover, correlationId);

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
            CorrelationId = correlationId,
            Mode = PolicyEnforcementMode.Enforce
        };

        var enforceResp = await httpInitiator.PostAsync(
            "api/hr-core/api/policies/enforce",
            JsonContent(enforceReq));
        var enforceBody = await enforceResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("enforce-trigger-workflow", "POST", "/api/hr-core/api/policies/enforce",
            (int)enforceResp.StatusCode, JsonSerializer.Deserialize<object>(enforceBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, enforceResp.StatusCode);

        Guid workflowInstanceId;
        using (var doc = JsonDocument.Parse(enforceBody))
        {
            Assert.True(doc.RootElement.GetProperty("allowed").GetBoolean());
            workflowInstanceId = doc.RootElement.GetProperty("workflowInstanceId").GetGuid();
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

        var initiatorStep2Resp = await httpInitiator.PostAsync(
            $"api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            JsonContent(new { comments = "initiator checker negative" }));
        recorder.CaptureHttp("approve-step2-initiator-negative", "POST",
            $"/api/hr-core/api/workflow-instances/{workflowInstanceId:D}/approve",
            (int)initiatorStep2Resp.StatusCode, await initiatorStep2Resp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, initiatorStep2Resp.StatusCode);

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
            initiatorCheckerBlocked = true
        });
        recorder.WriteAfter(new { status = "Completed", workflowInstanceId });
        recorder.WriteResult("Pass");
    }

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
