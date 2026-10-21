using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-23: frozen run unchanged after pay profile edit; later run reflects change.</summary>
public class Ac23PayrollFreezeTests
{
    [SkippableFact]
    public async Task Frozen_run_isolated_from_profile_edit()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-23", correlationId);
        recorder.WriteMetadata("AC-23 payroll freeze isolation (PAY-003)", processor.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(processor, correlationId);

        var periodStartA = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndA = new DateTime(2027, 2, 28, 0, 0, 0, DateTimeKind.Utc);
        var createA = await http.PostAsync("api/payroll/runs", JsonContent(new
        {
            periodStart = periodStartA,
            periodEnd = periodEndA,
            runDate = DateTime.UtcNow
        }));
        var createBody = await createA.Content.ReadAsStringAsync();
        recorder.CaptureHttp("create-run-a", "POST", "/api/payroll/runs", (int)createA.StatusCode,
            JsonSerializer.Deserialize<object>(createBody));

        Assert.True(createA.IsSuccessStatusCode, createBody);

        Guid runAId;
        using (var doc = JsonDocument.Parse(createBody))
            runAId = doc.RootElement.GetProperty("id").GetGuid();

        var calculateA = await http.PostAsync($"api/payroll/runs/{runAId:D}/calculate", null);
        Assert.True(calculateA.IsSuccessStatusCode, await calculateA.Content.ReadAsStringAsync());
        var validateA = await http.PostAsync($"api/payroll/runs/{runAId:D}/validate", null);
        Assert.True(validateA.IsSuccessStatusCode, await validateA.Content.ReadAsStringAsync());

        var freezeResp = await http.GetAsync($"api/payroll/runs/{runAId:D}/freeze-status");
        var freezeBody = await freezeResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("freeze-status", "GET", $"/api/payroll/runs/{runAId:D}/freeze-status",
            (int)freezeResp.StatusCode, JsonSerializer.Deserialize<object>(freezeBody));
        Assert.True(freezeResp.IsSuccessStatusCode);

        var runResp = await http.GetAsync($"api/payroll/runs/{runAId:D}");
        var runJson = await runResp.Content.ReadAsStringAsync();
        recorder.WriteBefore(JsonSerializer.Deserialize<object>(runJson) ?? new { runAId });

        using var runDoc = JsonDocument.Parse(runJson);
        var employeeId = runDoc.RootElement.GetProperty("salaryLines")[0].GetProperty("employeeId").GetGuid();
        var netBefore = runDoc.RootElement.GetProperty("salaryLines")[0].GetProperty("netPay").GetDecimal();

        var profileResp = await http.PutAsJsonAsync($"api/payroll/employees/{employeeId:D}/pay-profile", new
        {
            baseSalary = netBefore + 25_000m,
            payGradeId = (Guid?)null,
            effectiveFrom = DateTime.UtcNow,
            effectiveTo = (DateTime?)null,
            isActive = true,
            changeReason = "AC-23 profile raise"
        });
        recorder.CaptureHttp("after-profile-edit", "PUT",
            $"/api/payroll/employees/{employeeId:D}/pay-profile",
            (int)profileResp.StatusCode,
            JsonSerializer.Deserialize<object>(await profileResp.Content.ReadAsStringAsync()));

        var recalc = await http.PostAsync($"api/payroll/runs/{runAId:D}/calculate", null);
        recorder.CaptureHttp("recalc-frozen-negative", "POST",
            $"/api/payroll/runs/{runAId:D}/calculate", (int)recalc.StatusCode,
            JsonSerializer.Deserialize<object>(await recalc.Content.ReadAsStringAsync()));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, recalc.StatusCode);

        var runAfter = await http.GetAsync($"api/payroll/runs/{runAId:D}");
        var afterJson = await runAfter.Content.ReadAsStringAsync();
        using var afterDoc = JsonDocument.Parse(afterJson);
        var netAfter = afterDoc.RootElement.GetProperty("salaryLines")[0].GetProperty("netPay").GetDecimal();
        recorder.WriteAfter(new { netBefore, netAfter, unchanged = netBefore == netAfter });
        Assert.Equal(netBefore, netAfter);

        var periodStartB = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndB = new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var createB = await http.PostAsync("api/payroll/runs", JsonContent(new
        {
            periodStart = periodStartB,
            periodEnd = periodEndB,
            runDate = DateTime.UtcNow
        }));
        var createBBody = await createB.Content.ReadAsStringAsync();
        Assert.True(createB.IsSuccessStatusCode, createBBody);

        Guid runBId;
        using (var doc = JsonDocument.Parse(createBBody))
            runBId = doc.RootElement.GetProperty("id").GetGuid();

        var calculateB = await http.PostAsync($"api/payroll/runs/{runBId:D}/calculate", null);
        Assert.True(calculateB.IsSuccessStatusCode, await calculateB.Content.ReadAsStringAsync());
        var runBResp = await http.GetAsync($"api/payroll/runs/{runBId:D}");
        var runBJson = await runBResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("run-b-new-salary", "GET", $"/api/payroll/runs/{runBId:D}",
            (int)runBResp.StatusCode, JsonSerializer.Deserialize<object>(runBJson));

        using var runBDoc = JsonDocument.Parse(runBJson);
        var netB = runBDoc.RootElement.GetProperty("salaryLines")[0].GetProperty("netPay").GetDecimal();
        recorder.WriteDbChecks(new { netBefore, netB, runBDiffers = netB != netBefore });
        Assert.NotEqual(netBefore, netB);

        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
