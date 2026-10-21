using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-09: Shadow mode records diffs without changing effective Allowed.</summary>
public class Ac09ShadowPolicyTests
{
    [SkippableFact]
    public async Task Shadow_enforce_keeps_allowed_true_and_writes_ledger()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-09", correlationId);
        recorder.WriteMetadata("AC-09 policy shadow", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var domain = PolicyDomains.AssetsCustody;
        var facts = new Dictionary<string, string>
        {
            ["isPrivileged"] = "true",
            ["value"] = "500",
            ["action"] = "Assign"
        };

        var simulateResp = await http.PostAsync(
            $"api/hr-core/api/policies/templates/{AcceptanceSeedIds.PolicyVersion2Id:D}/simulate",
            JsonContent(facts));
        var simulateBody = await simulateResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("simulate-v2", "POST",
            $"/api/hr-core/api/policies/templates/{AcceptanceSeedIds.PolicyVersion2Id:D}/simulate",
            (int)simulateResp.StatusCode, JsonSerializer.Deserialize<object>(simulateBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, simulateResp.StatusCode);

        var enforceReq = new PolicyEnforceRequest
        {
            Domain = domain,
            TenantId = AcceptanceSeedIds.AgencyAId,
            SubjectType = "Asset",
            Action = "Assign",
            Facts = facts,
            CorrelationId = correlationId,
            Mode = PolicyEnforcementMode.Shadow
        };
        var enforceResp = await http.PostAsync(
            "api/hr-core/api/policies/enforce",
            JsonContent(enforceReq));
        var enforceBody = await enforceResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("enforce-shadow", "POST", "/api/hr-core/api/policies/enforce",
            (int)enforceResp.StatusCode, JsonSerializer.Deserialize<object>(enforceBody));
        Assert.Equal(System.Net.HttpStatusCode.OK, enforceResp.StatusCode);

        using var enforceDoc = JsonDocument.Parse(enforceBody);
        Assert.True(enforceDoc.RootElement.GetProperty("allowed").GetBoolean());

        var ledgerResp = await http.GetAsync($"api/hr-core/api/policies/ledger?domain={domain}&correlationId={correlationId}");
        var ledgerBody = await ledgerResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("ledger", "GET", $"/api/hr-core/api/policies/ledger?domain={domain}",
            (int)ledgerResp.StatusCode, JsonSerializer.Deserialize<object>(ledgerBody));
        recorder.WriteAudit(new { correlationId, mode = "Shadow", domain });
        recorder.WriteDbChecks(new
        {
            simulationRunExpected = true,
            ledgerMode = "Shadow",
            effectiveAllowedMustRemainTrue = true
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, ledgerResp.StatusCode);
        Assert.Contains("Shadow", ledgerBody, StringComparison.OrdinalIgnoreCase);

        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
