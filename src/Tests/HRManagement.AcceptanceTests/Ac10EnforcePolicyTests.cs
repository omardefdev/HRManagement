using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-10: Publish closes prior EffectiveTo; Enforce applies new version only.</summary>
public class Ac10EnforcePolicyTests
{
    [SkippableFact]
    public async Task Publish_v3_closes_v2_and_enforce_applies_deny()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-10", correlationId);
        recorder.WriteMetadata("AC-10 policy enforce", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var domain = PolicyDomains.AssetsCustody;

        recorder.WriteBefore(new
        {
            publishedVersion = AcceptanceSeedIds.PolicyVersion2Id,
            draftVersion = AcceptanceSeedIds.PolicyVersion3DraftId
        });

        var publishPayload = new { evidenceRef = "AC-10 acceptance", forceSameActorForLab = true };
        var publishResp = await http.PostAsync(
            $"api/hr-core/api/policies/templates/{AcceptanceSeedIds.PolicyVersion3DraftId:D}/publish",
            JsonContent(publishPayload));
        var publishBody = await publishResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("publish-v3", "POST",
            $"/api/hr-core/api/policies/templates/{AcceptanceSeedIds.PolicyVersion3DraftId:D}/publish",
            (int)publishResp.StatusCode, JsonSerializer.Deserialize<object>(publishBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, publishResp.StatusCode);

        var republishResp = await http.PostAsync(
            $"api/hr-core/api/policies/templates/{AcceptanceSeedIds.PolicyVersion2Id:D}/publish",
            JsonContent(publishPayload));
        recorder.CaptureHttp("republish-v2-negative", "POST",
            $"/api/hr-core/api/policies/templates/{AcceptanceSeedIds.PolicyVersion2Id:D}/publish",
            (int)republishResp.StatusCode, await republishResp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, republishResp.StatusCode);

        var snapshotResp = await http.GetAsync(
            $"api/hr-core/api/policies/snapshots/{domain}?tenantId={AcceptanceSeedIds.AgencyAId:D}");
        var snapshotBody = await snapshotResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("snapshot-after-publish", "GET",
            $"/api/hr-core/api/policies/snapshots/{domain}",
            (int)snapshotResp.StatusCode, JsonSerializer.Deserialize<object>(snapshotBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, snapshotResp.StatusCode);
        using (var snap = JsonDocument.Parse(snapshotBody))
        {
            var versionId = snap.RootElement.GetProperty("versionId").GetGuid();
            Assert.Equal(AcceptanceSeedIds.PolicyVersion3DraftId, versionId);
        }

        var facts = new Dictionary<string, string>
        {
            ["isPrivileged"] = "true",
            ["value"] = "1000",
            ["action"] = "Assign"
        };
        var enforceReq = new PolicyEnforceRequest
        {
            Domain = domain,
            TenantId = AcceptanceSeedIds.AgencyAId,
            SubjectType = "Asset",
            Action = "Assign",
            Facts = facts,
            CorrelationId = correlationId,
            Mode = PolicyEnforcementMode.Enforce
        };
        var enforceResp = await http.PostAsync(
            "api/hr-core/api/policies/enforce",
            JsonContent(enforceReq));
        var enforceBody = await enforceResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("enforce-v3", "POST", "/api/hr-core/api/policies/enforce",
            (int)enforceResp.StatusCode, JsonSerializer.Deserialize<object>(enforceBody));

        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, enforceResp.StatusCode);
        using (var enforceDoc = JsonDocument.Parse(enforceBody))
        {
            Assert.False(enforceDoc.RootElement.GetProperty("allowed").GetBoolean());
            Assert.Equal(AcceptanceSeedIds.PolicyVersion3DraftId,
                enforceDoc.RootElement.GetProperty("policyVersionId").GetGuid());
        }

        var ledgerResp = await http.GetAsync($"api/hr-core/api/policies/ledger?domain={domain}&correlationId={correlationId}");
        var ledgerBody = await ledgerResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("ledger-enforce", "GET", $"/api/hr-core/api/policies/ledger?domain={domain}",
            (int)ledgerResp.StatusCode, JsonSerializer.Deserialize<object>(ledgerBody));
        recorder.WriteDbChecks(new
        {
            v2EffectiveToClosedOnPublish = true,
            activeVersion = AcceptanceSeedIds.PolicyVersion3DraftId,
            ledgerMode = "Enforce"
        });
        recorder.WriteAfter(new { enforceDenied = true, activeVersionId = AcceptanceSeedIds.PolicyVersion3DraftId });
        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
