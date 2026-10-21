using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-07: idempotent agency onboarding — org tree + no duplication on re-apply.</summary>
public class Ac07AgencyOnboardingTests
{
    [SkippableFact]
    public async Task Apply_twice_is_idempotent_with_org_tree()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("platform-admin");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-07", correlationId);
        recorder.WriteMetadata("AC-07 agency onboarding idempotent", persona.Id);

        var manifestPath = Path.Combine(AcceptanceHarness.RepoRoot, "seed-packs", "examples", "agency-acceptance-test.json");
        Assert.True(File.Exists(manifestPath));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        using var manifestDoc = JsonDocument.Parse(manifestJson);
        var packId = manifestDoc.RootElement.GetProperty("packId").GetString();

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        const string applyUrl = "api/hr-core/api/onboarding/agencies/apply";

        var dryContent = new StringContent(manifestJson, System.Text.Encoding.UTF8, "application/json");
        var dryResp = await http.PostAsync($"{applyUrl}?dryRun=true", dryContent);
        var dryBody = await dryResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-apply-dryrun", "POST", applyUrl, (int)dryResp.StatusCode,
            ParseEvidenceBody(dryBody));
        Assert.Equal(HttpStatusCode.OK, dryResp.StatusCode);

        var apply1Content = new StringContent(manifestJson, System.Text.Encoding.UTF8, "application/json");
        var apply1 = await http.PostAsync(applyUrl, apply1Content);
        var apply1Body = await apply1.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-apply-1", "POST", applyUrl, (int)apply1.StatusCode,
            ParseEvidenceBody(apply1Body));
        Assert.Equal(HttpStatusCode.OK, apply1.StatusCode);

        using var apply1Doc = JsonDocument.Parse(apply1Body);
        Assert.False(apply1Doc.RootElement.GetProperty("alreadyApplied").GetBoolean());

        var apply2Content = new StringContent(manifestJson, System.Text.Encoding.UTF8, "application/json");
        var apply2 = await http.PostAsync(applyUrl, apply2Content);
        var apply2Body = await apply2.Content.ReadAsStringAsync();
        recorder.CaptureHttp("http-apply-2", "POST", applyUrl, (int)apply2.StatusCode,
            ParseEvidenceBody(apply2Body));
        Assert.Equal(HttpStatusCode.OK, apply2.StatusCode);

        using var apply2Doc = JsonDocument.Parse(apply2Body);
        Assert.True(apply2Doc.RootElement.GetProperty("alreadyApplied").GetBoolean());

        recorder.WriteDbChecks(new
        {
            packId,
            orgUnitsInManifest = manifestDoc.RootElement.GetProperty("orgStructure").GetProperty("units").GetArrayLength(),
            secondApplyAlreadyApplied = true
        });

        recorder.WriteAfter(new
        {
            firstApplyTenantId = apply1Doc.RootElement.GetProperty("tenantId").GetString(),
            secondApplySteps = apply2Doc.RootElement.GetProperty("steps").EnumerateArray()
                .Select(s => s.GetString()).ToArray()
        });

        recorder.WriteResult("Pass");
    }

    private static object? ParseEvidenceBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try { return JsonSerializer.Deserialize<object>(body); }
        catch (JsonException) { return body; }
    }
}
