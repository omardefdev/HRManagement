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
/// PLAN-59 (P-8): on the prod-like stack's main group. Its two months follow the registry's <c>current+2</c> rule
/// (<c>testdata/acceptance/payroll-months.json</c>: two consecutive months from the current month + 2 on that no other entry gives, found
/// by creating the run), and it leaves nothing: after its assertions both runs are cancelled through PLAN-56's API and its request by
/// its requester, in <c>finally</c>.
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

        // The salary change API validates an antiforgery token (PLAN-53): the payroll client carries it.
        using var http = await AcceptanceHarness.CreatePayrollClientAsync(processor, correlationId);
        using var httpApprover = await AcceptanceHarness.CreatePayrollClientAsync(approver, correlationId);

        // The two months follow the service's clock (Localization:TimeZone, Asia/Damascus): a request cannot name a month before the
        // current one. PLAN-59: the registry's current+2 rule gives two consecutive months no other class (and no seeded run) takes.
        await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(HRManagement.ServiceDefaults.AcceptanceSeedIds.AgencyAId.ToString("D"));
        var (monthA, runA, refused) = await AcceptancePayrollMonths.CreateRunAsync(http, recorder, "ac23",
            AcceptancePayrollMonths.AfterNextCandidates("AC-23", AcceptancePayrollMonths.MainStack, 2));
        var runAId = Guid.Parse(runA);
        var periodStartA = monthA.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var frozenMonth = periodStartA.ToString("yyyy/MM", System.Globalization.CultureInfo.InvariantCulture);
        Guid? runBId = null;
        Guid? requestId = null;
        try
        {
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
            // PLAN-59: the first of the run's employees with no open salary change request (the main stack's other classes leave some).
            var employeeId = await WithoutOpenRequestAsync(http, runDoc.RootElement.GetProperty("salaryLines"));
            var netBefore = NetOf(runDoc.RootElement, employeeId);

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
            Guid request;
            using (var requestDoc = JsonDocument.Parse(requestBody))
            {
                Assert.Equal("PendingApproval", requestDoc.RootElement.GetProperty("state").GetString());
                request = requestDoc.RootElement.GetProperty("requestId").GetGuid();
            }

            requestId = request;

            // The approver, another person, cannot approve it: the month's run is frozen. The request stays pending.
            var approve = await httpApprover.PostAsJsonAsync($"api/payroll/pay-profile-changes/{request:D}/approve", new { note = "AC-23" });
            var approveBody = await approve.Content.ReadAsStringAsync();
            recorder.CaptureHttp("approve-frozen-month-negative", "POST", $"/api/payroll/pay-profile-changes/{request:D}/approve",
                (int)approve.StatusCode, JsonSerializer.Deserialize<object>(approveBody));
            Assert.Equal(System.Net.HttpStatusCode.Conflict, approve.StatusCode);
            using (var refusal = JsonDocument.Parse(approveBody))
            {
                Assert.Contains(frozenMonth, refusal.RootElement.GetProperty("error").GetString() ?? "", StringComparison.Ordinal);
                Assert.Equal("PendingApproval", refusal.RootElement.GetProperty("state").GetString());
            }

            Assert.Equal("PendingApproval", await RequestStateAsync(http, request));
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
            var netAfter = NetOf(afterDoc.RootElement, employeeId);
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

            Guid runB;
            using (var doc = JsonDocument.Parse(createBBody))
                runB = doc.RootElement.GetProperty("id").GetGuid();
            runBId = runB;

            var calculateB = await http.PostAsync($"api/payroll/runs/{runB:D}/calculate", null);
            Assert.True(calculateB.IsSuccessStatusCode, await calculateB.Content.ReadAsStringAsync());
            var runBResp = await http.GetAsync($"api/payroll/runs/{runB:D}");
            var runBJson = await runBResp.Content.ReadAsStringAsync();
            recorder.CaptureHttp("run-b-request-still-pending", "GET", $"/api/payroll/runs/{runB:D}",
                (int)runBResp.StatusCode, JsonSerializer.Deserialize<object>(runBJson));

            using var runBDoc = JsonDocument.Parse(runBJson);
            Assert.NotEqual(0, runBDoc.RootElement.GetProperty("salaryLines").GetArrayLength());
            // A pending request reaches no run (rule 2): after the next month was computed it is still pending and unapplied, and the pay profile
            // still holds the salary it held. (The old assertion compared the frozen month's net with the next month's after a direct write.)
            var stateAfterRunB = await RequestStateAsync(http, request);
            var (salaryAfterRunB, _) = await SalaryInsideBoundsAsync(employeeId);
            recorder.WriteDbChecks(new { netBefore, requestId = request, stateAfterRunB, currentSalary, salaryAfterRunB, raisedSalary });
            Assert.Equal("PendingApproval", stateAfterRunB);
            Assert.Equal(currentSalary, salaryAfterRunB);

            // The request is closed by its requester, so the employee is left with no open request.
            var cancel = await http.PostAsJsonAsync($"api/payroll/pay-profile-changes/{request:D}/cancel", new { reason = "AC-23: انتهى الاختبار" });
            recorder.CaptureHttp("cancel-request", "POST", $"/api/payroll/pay-profile-changes/{request:D}/cancel", (int)cancel.StatusCode,
                JsonSerializer.Deserialize<object>(await cancel.Content.ReadAsStringAsync()));
            Assert.True(cancel.IsSuccessStatusCode);
            Assert.Equal("Cancelled", await RequestStateAsync(http, request));

            recorder.WriteResult("Pass");
        }
        finally
        {
            // Nothing is left for another class: the request (if still open) by its requester, and both runs through PLAN-56's API.
            if (requestId is Guid open && await RequestStateAsync(http, open) is "PendingApproval" or "Scheduled")
            {
                using var cancelled = await http.PostAsJsonAsync($"api/payroll/pay-profile-changes/{open:D}/cancel", new { reason = "AC-23: انتهى الاختبار" });
                recorder.CaptureHttp("cancel-request-finally", "POST", $"/api/payroll/pay-profile-changes/{open:D}/cancel", (int)cancelled.StatusCode,
                    await cancelled.Content.ReadAsStringAsync());
            }

            foreach (var run in new[] { runBId, (Guid?)runAId })
            {
                if (run is Guid id)
                    await AcceptancePayrollMonths.CancelRunAsync(http, recorder, $"ac23-{id:N}"[..14], id.ToString("D"), "AC-23: انتهى اختبار التجميد (PLAN-59)");
            }
        }
    }

    /// <summary>The first employee of the run's lines (in their order) with no pending or scheduled salary change request (an API read).</summary>
    private static async Task<Guid> WithoutOpenRequestAsync(HttpClient http, JsonElement lines)
    {
        foreach (var line in lines.EnumerateArray().Take(50))
        {
            var employee = line.GetProperty("employeeId").GetGuid();
            using var response = await http.GetAsync($"api/payroll/pay-profile-changes?employeeId={employee:D}");
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.EnumerateArray().Any(r => r.GetProperty("state").GetString() is "PendingApproval" or "Scheduled"))
                return employee;
        }

        Assert.Fail("AC-23: none of the run's first 50 employees is free of an open salary change request.");
        return Guid.Empty;
    }

    private static decimal NetOf(JsonElement run, Guid employee) =>
        run.GetProperty("salaryLines").EnumerateArray().Single(l => l.GetProperty("employeeId").GetGuid() == employee).GetProperty("netPay").GetDecimal();

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
