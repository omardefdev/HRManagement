using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-23: a frozen run is isolated from a pay change. Since PLAN-53 a salary changes by a request another person approves, so the
/// isolation is proved twice: the change cannot reach the frozen month at all (the approver's approval is refused and the request
/// stays pending), and the frozen run refuses recalculation and keeps its line. A run of the next month is still computed while the request is
/// pending, and the request is still pending after it. That a run holds an applied change is AC-IAM-53a's (the prod-like stack) and
/// Pay023FrozenRunIsolationTests' (the automatic path): no change can be applied here for a month the clock has not reached.
/// </summary>
[RequiredAcceptance]
public class Ac23PayrollFreezeTests
{
    /// <summary>The employee's current salary and a salary inside his grade's bounds that differs from it.</summary>
    internal static async Task<(decimal Current, decimal Raised)> SalaryInsideBoundsAsync(Guid employeeId)
    {
        var row = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            "SELECT p.\"BaseSalary\"::text || '|' || coalesce(g.\"MinSalary\", 0)::text || '|' || coalesce(g.\"MaxSalary\", 0)::text " +
            "FROM \"EmployeePayProfiles\" p LEFT JOIN \"PayGrades\" g ON g.\"Id\" = p.\"PayGradeId\" " +
            $"WHERE p.\"EmployeeId\" = '{employeeId:D}' AND p.\"IsActive\"");
        var parts = row.Split('|');
        Assert.True(parts.Length == 3, $"No active pay profile for {employeeId}: {row}");
        var current = decimal.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        var min = decimal.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        var max = decimal.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        // A raise of 1000 when the grade allows it; else 1000 less (still not below the minimum); a grade with no maximum allows any raise.
        var raised = max <= 0 || current + 1000m <= max ? current + 1000m : current - 1000m;
        Assert.True(raised > 0 && (min <= 0 || raised >= min), $"No salary inside the bounds [{min}, {max}] differs from {current} by 1000.");
        return (current, raised);
    }

    [SkippableFact]
    public async Task Frozen_run_isolated_from_profile_edit()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var approver = AcceptanceHarness.RequirePersona("payroll-approver");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        await AcceptanceHarness.LoginAsync(approver);
        var recorder = new EvidenceRecorder("AC-23", correlationId);
        recorder.WriteMetadata("AC-23 payroll freeze isolation (PAY-003)", processor.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(processor, correlationId);
        using var httpApprover = await AcceptanceHarness.CreateAuthorizedClientAsync(approver, correlationId);

        // The two months follow the service's clock (Localization:TimeZone, Asia/Damascus): a request cannot name a month before the
        // current one, so fixed months would end the scenario's life at their date. Thirteen and fourteen months ahead meet no month
        // another lab class uses (AC-25 the current month; AC-24 and AC-12 fixed months of 2027) and no seeded Regular run.
        var today = HRManagement.ServiceDefaults.Localization.HrLocalTime.ToLocal(DateTime.UtcNow, HRManagement.ServiceDefaults.Localization.HrLocalTime.DefaultZone);
        var periodStartA = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(13);
        var periodEndA = periodStartA.AddMonths(1).AddDays(-1);
        var frozenMonth = periodStartA.ToString("yyyy/MM", System.Globalization.CultureInfo.InvariantCulture);
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

        // PLAN-53: the processor requests a raise from the first day of the frozen month, inside the grade's bounds, through the PUT alias. It is a
        // request: 202, pending, and the pay profile is not written.
        var (currentSalary, raisedSalary) = await SalaryInsideBoundsAsync(employeeId);
        var profileResp = await http.PutAsJsonAsync($"api/payroll/employees/{employeeId:D}/pay-profile", new
        {
            baseSalary = raisedSalary,
            payGradeId = (Guid?)null,
            effectiveFrom = periodStartA,
            changeReason = "AC-23: زيادة في شهر مجمّد",
            legalReference = "القرار 23"
        });
        var requestBody = await profileResp.Content.ReadAsStringAsync();
        recorder.CaptureHttp("after-profile-edit", "PUT",
            $"/api/payroll/employees/{employeeId:D}/pay-profile",
            (int)profileResp.StatusCode,
            JsonSerializer.Deserialize<object>(requestBody));
        Assert.Equal(System.Net.HttpStatusCode.Accepted, profileResp.StatusCode);
        Guid requestId;
        using (var requestDoc = JsonDocument.Parse(requestBody))
        {
            Assert.Equal("PendingApproval", requestDoc.RootElement.GetProperty("state").GetString());
            requestId = requestDoc.RootElement.GetProperty("requestId").GetGuid();
        }

        // The approver, another person, cannot approve it: the month's run is frozen. The request stays pending.
        var approve = await httpApprover.PostAsJsonAsync($"api/payroll/pay-profile-changes/{requestId:D}/approve", new { note = "AC-23" });
        var approveBody = await approve.Content.ReadAsStringAsync();
        recorder.CaptureHttp("approve-frozen-month-negative", "POST", $"/api/payroll/pay-profile-changes/{requestId:D}/approve",
            (int)approve.StatusCode, JsonSerializer.Deserialize<object>(approveBody));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, approve.StatusCode);
        using (var refusal = JsonDocument.Parse(approveBody))
        {
            Assert.Contains(frozenMonth, refusal.RootElement.GetProperty("error").GetString() ?? "", StringComparison.Ordinal);
            Assert.Equal("PendingApproval", refusal.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("PendingApproval", await RequestStateAsync(http, requestId));
        var (salaryAfterRefusal, _) = await SalaryInsideBoundsAsync(employeeId);
        Assert.Equal(currentSalary, salaryAfterRefusal);

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

        var periodStartB = periodStartA.AddMonths(1);
        var periodEndB = periodStartB.AddMonths(1).AddDays(-1);
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
        recorder.CaptureHttp("run-b-request-still-pending", "GET", $"/api/payroll/runs/{runBId:D}",
            (int)runBResp.StatusCode, JsonSerializer.Deserialize<object>(runBJson));

        using var runBDoc = JsonDocument.Parse(runBJson);
        Assert.NotEqual(0, runBDoc.RootElement.GetProperty("salaryLines").GetArrayLength());
        // A pending request reaches no run (rule 2): after the next month was computed it is still pending and unapplied, and the pay profile
        // still holds the salary it held. (The old assertion compared the frozen month's net with the next month's after a direct write.)
        var stateAfterRunB = await RequestStateAsync(http, requestId);
        var (salaryAfterRunB, _) = await SalaryInsideBoundsAsync(employeeId);
        recorder.WriteDbChecks(new { netBefore, requestId, stateAfterRunB, currentSalary, salaryAfterRunB, raisedSalary });
        Assert.Equal("PendingApproval", stateAfterRunB);
        Assert.Equal(currentSalary, salaryAfterRunB);

        // The request is closed by its requester, so the employee is left with no open request.
        var cancel = await http.PostAsJsonAsync($"api/payroll/pay-profile-changes/{requestId:D}/cancel", new { reason = "AC-23: انتهى الاختبار" });
        recorder.CaptureHttp("cancel-request", "POST", $"/api/payroll/pay-profile-changes/{requestId:D}/cancel", (int)cancel.StatusCode,
            JsonSerializer.Deserialize<object>(await cancel.Content.ReadAsStringAsync()));
        Assert.True(cancel.IsSuccessStatusCode);
        Assert.Equal("Cancelled", await RequestStateAsync(http, requestId));

        recorder.WriteResult("Pass");
    }

    private static async Task<string> RequestStateAsync(HttpClient http, Guid requestId)
    {
        var response = await http.GetAsync($"api/payroll/pay-profile-changes/{requestId:D}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("appliedProfileId").ValueKind);
        return doc.RootElement.GetProperty("state").GetString() ?? "";
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
