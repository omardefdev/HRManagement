using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-25 (PLAN-58, D-PAY-4): a fraud hold holds its employee's order, and the others are sent; a four-eyes release lets it be paid.
/// Two employees are paid into one account (PLAN-55's two people register and verify the same number for both): the scan of the
/// sending raises FR-04 for each and holds their two orders (FR-04 holds since PLAN-58); a third employee, with his own account, is
/// sent in the batch's file. payroll-approver asks for each release; a-payroll-approver-2 decides it from his own session; then
/// a-payroll-poster sends the released orders alone, by a supplementary file. Negative: the requester deciding his own request answers
/// 400, and an approver in the release's body is ignored (the decider is the caller). At the end the second employee gets an account
/// of his own, so the class leaves no shared account. (PLAN-58 reads FR-07's hire date from the employee's first pay profile: the old
/// fixture — a salary change from the run's first day — no longer looks like a new hire; FR-04 is the fraud rule a live stack can
/// raise through the screens.) The month is August 2027 of agency A (lab), which no other class uses (AC-12 takes June, AC-24 April).
/// </summary>
[RequiredAcceptance]
public class Ac25FraudHoldTests
{
    private const string Payroll = "payroll_db";

    [SkippableFact]
    public async Task Fraud_hold_blocks_pay_until_four_eyes_release()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var approver = AcceptanceHarness.RequirePersona("payroll-approver");
        var secondApprover = AcceptanceHarness.RequirePersona(AcceptancePayrollReview.SecondApprover);
        var poster = AcceptanceHarness.RequirePersona("a-payroll-poster");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-25", correlationId);
        recorder.WriteMetadata("AC-25 fraud hold per employee, four-eyes release (FRD-001, PLAN-58)", processor.Id);

        using var httpProcessor = await AcceptanceHarness.CreatePayrollClientAsync(processor, correlationId);
        using var httpApprover = await AcceptanceHarness.CreatePayrollClientAsync(approver, correlationId);
        using var httpSecond = await AcceptanceHarness.CreatePayrollClientAsync(secondApprover, correlationId);
        using var httpPoster = await AcceptanceHarness.CreatePayrollClientAsync(poster, correlationId);

        var periodStart = new DateTime(2027, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2027, 8, 31, 0, 0, 0, DateTimeKind.Utc);
        var covering = await AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Payroll,
            $"SELECT count(*) FROM \"PayrollRuns\" WHERE \"TenantId\"='{AcceptanceSeedIds.AgencyAId:D}' AND \"PeriodStart\"::date = '2027-08-01' AND \"Status\" NOT IN (5, 8);");
        AcceptanceGate.RequireData(recorder, covering == 0,
            "AC-25: للجهة A تشغيل رواتب لآب 2027: الاختبار يحتاج مكدساً جديداً",
            "AC-25: agency A already has a payroll run of August 2027: start a fresh stack.");

        // Two employees paid into one account, and a third with his own (before the calculation, whose snapshot pins them).
        var picked = await AcceptancePayrollAccounts.PickWithoutAccountAsync(recorder, "ac25", 3);
        var (first, second, third) = (picked[0], picked[1], picked[2]);
        var shared = AcceptancePayrollAccounts.Iban(first, "2525");
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(recorder, httpProcessor, httpApprover, first, shared, "كتاب المصرف — AC-25/1", "ac25");
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(recorder, httpProcessor, httpApprover, second, shared, "كتاب المصرف — AC-25/2", "ac25");
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(recorder, httpProcessor, httpApprover, third,
            AcceptancePayrollAccounts.Iban(third, "2526"), "كتاب المصرف — AC-25/3", "ac25");

        var create = await httpProcessor.PostAsync("api/payroll/runs", JsonContent(new { periodStart, periodEnd, runDate = periodStart }));
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.True(create.IsSuccessStatusCode, createBody);
        var runId = JsonSerializer.Deserialize<JsonElement>(createBody).GetProperty("id").GetGuid();

        await RequireSuccessAsync(await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/calculate", null), recorder, "calculate", $"/api/payroll/runs/{runId:D}/calculate");
        await RequireSuccessAsync(await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/validate", null), recorder, "validate", $"/api/payroll/runs/{runId:D}/validate");
        await RequireSuccessAsync(await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/submit-for-review", null), recorder, "submit-for-review", $"/api/payroll/runs/{runId:D}/submit-for-review");
        var approved = await RequireSuccessAsync(await httpApprover.PostAsync($"api/payroll/runs/{runId:D}/approve", null), recorder, "approve", $"/api/payroll/runs/{runId:D}/approve");
        var approvals = await AcceptancePayrollReview.CompleteApprovalAsync(recorder, runId.ToString("D"), approved, "ac25");
        var review = await AcceptancePayrollReview.ReviewAsync(recorder, runId.ToString("D"), "ac25");
        await RequireSuccessAsync(await httpPoster.PostAsync($"api/payroll/runs/{runId:D}/post", null), recorder, "post", $"/api/payroll/runs/{runId:D}/post");

        var batchId = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll,
            $"SELECT \"Id\"::text FROM \"PaymentBatches\" WHERE \"PayrollRunId\"='{runId:D}' AND \"BatchKind\"='Disbursement';");
        recorder.WriteBefore(new { runId, batchId, sharedAccountEmployees = new[] { first, second }, ownAccountEmployee = third, approvals, review });

        // The sending: its fraud scan raises FR-04 for the two and holds their orders; the third is in the file.
        var sent = await RequireSuccessAsync(await httpPoster.PostAsync($"api/payroll/payment-batches/{batchId}/process", null), recorder, "process", $"/api/payroll/payment-batches/{batchId}/process");
        var orders = await OrdersAsync(batchId);
        var holds = (await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll,
                "SELECT COALESCE(string_agg(h.\"Id\"::text || '|' || h.\"EmployeeId\"::text, ',' ORDER BY h.\"EmployeeId\"), '') FROM \"PaymentHolds\" h " +
                $"JOIN \"FraudAlerts\" a ON a.\"Id\" = h.\"FraudAlertId\" WHERE h.\"PayrollRunId\"='{runId:D}' AND h.\"Status\"='Active' AND a.\"RuleId\"='FR-04';"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split('|')).ToDictionary(r => r[1], r => r[0]);
        var firstFile = await FilesAsync(httpPoster, recorder, batchId, "files-after-sending");

        Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal), holds.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Held", orders[first]);
        Assert.Equal("Held", orders[second]);
        Assert.Equal("Submitted", orders[third]);
        var initial = Assert.Single(firstFile);
        Assert.Equal("Initial", initial.GetProperty("kind").GetString());

        // Negative: an approver named in the body is ignored — the route asks for a release, and nothing is released by one person.
        var holdOfFirst = holds[first];
        var asked = await httpApprover.PostAsJsonAsync($"api/payroll/payment-holds/{holdOfFirst}/release",
            new { releaseReason = "طابق المعتمدان الحساب مع كتاب المصرف", approverUserId = Guid.NewGuid() });
        var askedBody = await asked.Content.ReadAsStringAsync();
        recorder.CaptureHttp("release-request-first", "POST", $"/api/payroll/payment-holds/{holdOfFirst}/release", (int)asked.StatusCode, Parse(askedBody));
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        var firstRequest = JsonSerializer.Deserialize<JsonElement>(askedBody).GetProperty("requestId").GetGuid();
        Assert.Equal("Active", await HoldStatusAsync(holdOfFirst));

        // Negative: the requester does not decide his own request (400), and the hold stays.
        var own = await httpApprover.PostAsJsonAsync($"api/payroll/dual-control/{firstRequest:D}/decide", new { approve = true, notes = "موافق" });
        var ownBody = await own.Content.ReadAsStringAsync();
        recorder.CaptureHttp("decide-own-request-negative", "POST", $"/api/payroll/dual-control/{firstRequest:D}/decide", (int)own.StatusCode, Parse(ownBody));
        Assert.Equal(HttpStatusCode.BadRequest, own.StatusCode);
        Assert.Equal("Active", await HoldStatusAsync(holdOfFirst));

        // The second approver decides each request from his own session: the holds are released.
        await RequireSuccessAsync(await httpSecond.PostAsJsonAsync($"api/payroll/dual-control/{firstRequest:D}/decide", new { approve = true, notes = "موافق" }),
            recorder, "decide-first", $"/api/payroll/dual-control/{firstRequest:D}/decide");
        var holdOfSecond = holds[second];
        var askedSecond = await httpApprover.PostAsJsonAsync($"api/payroll/payment-holds/{holdOfSecond}/release", new { releaseReason = "طابق المعتمدان الحساب مع كتاب المصرف" });
        var askedSecondBody = await askedSecond.Content.ReadAsStringAsync();
        recorder.CaptureHttp("release-request-second", "POST", $"/api/payroll/payment-holds/{holdOfSecond}/release", (int)askedSecond.StatusCode, Parse(askedSecondBody));
        Assert.Equal(HttpStatusCode.Accepted, askedSecond.StatusCode);
        var secondRequest = JsonSerializer.Deserialize<JsonElement>(askedSecondBody).GetProperty("requestId").GetGuid();
        await RequireSuccessAsync(await httpSecond.PostAsJsonAsync($"api/payroll/dual-control/{secondRequest:D}/decide", new { approve = true, notes = "موافق" }),
            recorder, "decide-second", $"/api/payroll/dual-control/{secondRequest:D}/decide");
        Assert.Equal("Released", await HoldStatusAsync(holdOfFirst));
        Assert.Equal("Released", await HoldStatusAsync(holdOfSecond));

        // The released orders are sent alone, by a supplementary file.
        var supplementary = await RequireSuccessAsync(await httpPoster.PostAsync($"api/payroll/payment-batches/{batchId}/submit-held", null),
            recorder, "submit-held", $"/api/payroll/payment-batches/{batchId}/submit-held");
        var after = await OrdersAsync(batchId);
        var files = await FilesAsync(httpPoster, recorder, batchId, "files-after-release");
        Assert.Equal("Submitted", after[first]);
        Assert.Equal("Submitted", after[second]);
        Assert.Equal(2, files.Count);
        Assert.Equal(("HeldRelease", 2), (files[1].GetProperty("kind").GetString(), files[1].GetProperty("controlCount").GetInt32()));

        // Isolation: the second employee gets an account of his own; no shared account is left in agency A.
        await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(recorder, httpProcessor, httpApprover, second,
            AcceptancePayrollAccounts.Iban(second, "2527"), "كتاب المصرف — AC-25/4", "ac25-own-account");
        var sharedLeft = await AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Payroll,
            $"SELECT count(*) FROM (SELECT \"AccountNumber\" FROM \"EmployeeBankAccounts\" WHERE \"TenantId\"='{AcceptanceSeedIds.AgencyAId:D}' AND \"Status\" = 1 " +
            "GROUP BY \"AccountNumber\" HAVING count(DISTINCT \"EmployeeId\") > 1) shared;");

        recorder.WriteDbChecks(new
        {
            sent,
            supplementary,
            ordersAfterSending = orders,
            ordersAfterRelease = after,
            fr04Holds = holds.Count,
            files = files.Select(f => new { sequence = f.GetProperty("sequence").GetInt32(), kind = f.GetProperty("kind").GetString(), controlCount = f.GetProperty("controlCount").GetInt32() }),
            sharedAccountsLeft = sharedLeft,
        });
        Assert.Equal(0L, sharedLeft);
        recorder.WriteResult("Pass");
    }

    private static async Task<Dictionary<string, string>> OrdersAsync(string batchId) =>
        (await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll,
            $"SELECT COALESCE(string_agg(\"EmployeeId\"::text || '|' || \"Status\", ','), '') FROM \"PaymentOrders\" WHERE \"PaymentBatchId\"='{batchId}';"))
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split('|')).ToDictionary(r => r[0], r => r[1]);

    private static Task<string> HoldStatusAsync(string holdId) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Payroll, $"SELECT \"Status\" FROM \"PaymentHolds\" WHERE \"Id\"='{holdId}';");

    private static async Task<List<JsonElement>> FilesAsync(HttpClient client, EvidenceRecorder recorder, string batchId, string label)
    {
        var response = await client.GetAsync($"api/payroll/payment-batches/{batchId}/files");
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp(label, "GET", $"/api/payroll/payment-batches/{batchId}/files", (int)response.StatusCode, Parse(body));
        Assert.True(response.IsSuccessStatusCode, $"{label}: {body}");
        return JsonSerializer.Deserialize<JsonElement>(body).EnumerateArray().ToList();
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static object? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonSerializer.Deserialize<object>(body);
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }

    private static async Task<JsonElement> RequireSuccessAsync(HttpResponseMessage response, EvidenceRecorder recorder, string label, string path)
    {
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp(label, "POST", path, (int)response.StatusCode, Parse(body));
        Assert.True(response.IsSuccessStatusCode, $"{label}: {body}");
        // An empty answer reads as an empty object (a default JsonElement cannot be written into the evidence).
        return JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }
}
