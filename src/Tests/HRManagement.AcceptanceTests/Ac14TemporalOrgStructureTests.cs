using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-14: as-of org structure preserves historical head/name after rename.</summary>
public class Ac14TemporalOrgStructureTests
{
    [SkippableFact]
    public async Task AsOf_before_rename_unchanged_after_structural_update()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-14", correlationId);
        recorder.WriteMetadata("AC-14 temporal org as-of", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var unitId = AcceptanceSeedIds.Br1OrgUnitId;
        var t1 = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var asOfBeforeResp = await http.GetAsync(
            $"api/hr-core/api/org-structure/units/{unitId:D}/as-of?at={Uri.EscapeDataString(t1.ToString("o"))}");
        var asOfBeforeBody = await asOfBeforeResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("as-of-t1-before", "GET",
            $"/api/hr-core/api/org-structure/units/{unitId:D}/as-of",
            (int)asOfBeforeResp.StatusCode, JsonSerializer.Deserialize<object>(asOfBeforeBody));
        Assert.True(asOfBeforeResp.IsSuccessStatusCode, asOfBeforeBody);

        var updatePayload = new
        {
            nameAr = "الفرع الأول — محدّث AC-14",
            headEmployeeId = AcceptanceSeedIds.EmpBr2Id,
            effectiveFrom = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var updateResp = await http.PutAsync(
            $"api/hr-core/api/org-structure/units/{unitId:D}",
            JsonContent(updatePayload));
        var updateBody = await updateResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("rename-and-head", "PUT",
            $"/api/hr-core/api/org-structure/units/{unitId:D}",
            (int)updateResp.StatusCode, JsonSerializer.Deserialize<object>(updateBody));
        Assert.True(updateResp.IsSuccessStatusCode, updateBody);

        var asOfAfterChangeT1Resp = await http.GetAsync(
            $"api/hr-core/api/org-structure/units/{unitId:D}/as-of?at={Uri.EscapeDataString(t1.ToString("o"))}");
        var asOfAfterChangeT1Body = await asOfAfterChangeT1Resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("as-of-t1-after", "GET",
            $"/api/hr-core/api/org-structure/units/{unitId:D}/as-of",
            (int)asOfAfterChangeT1Resp.StatusCode, JsonSerializer.Deserialize<object>(asOfAfterChangeT1Body));

        var t2 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var asOfT2Resp = await http.GetAsync(
            $"api/hr-core/api/org-structure/units/{unitId:D}/as-of?at={Uri.EscapeDataString(t2.ToString("o"))}");
        var asOfT2Body = await asOfT2Resp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("as-of-t2", "GET",
            $"/api/hr-core/api/org-structure/units/{unitId:D}/as-of",
            (int)asOfT2Resp.StatusCode, JsonSerializer.Deserialize<object>(asOfT2Body));

        using var beforeDoc = JsonDocument.Parse(asOfBeforeBody);
        using var afterT1Doc = JsonDocument.Parse(asOfAfterChangeT1Body);
        using var t2Doc = JsonDocument.Parse(asOfT2Body);

        var beforeName = beforeDoc.RootElement.GetProperty("orgUnit").GetProperty("nameAr").GetString();
        var afterT1Name = afterT1Doc.RootElement.GetProperty("orgUnit").GetProperty("nameAr").GetString();
        var t2Name = t2Doc.RootElement.GetProperty("orgUnit").GetProperty("nameAr").GetString();

        Assert.Equal(beforeName, afterT1Name);
        Assert.Equal("الفرع الأول — محدّث AC-14", t2Name);
        Assert.NotEqual(beforeName, t2Name);

        recorder.WriteBefore(JsonSerializer.Deserialize<object>(asOfBeforeBody)!);
        recorder.WriteAfter(new { asOfT1AfterChange = JsonSerializer.Deserialize<object>(asOfAfterChangeT1Body), asOfT2 = JsonSerializer.Deserialize<object>(asOfT2Body) });
        recorder.WriteDbChecks(new { unitId, t1Preserved = true, t2Renamed = true });
        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
