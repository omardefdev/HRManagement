using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-24 (PLAN-58, D-PAY-2): a partial disbursement is a funding decision after the posting. The run is approved (twice above the
/// agency's threshold), reviewed by two auditors and posted, which makes its batch and orders; payroll-approver funds 60% of the
/// <c>Pending</c> batch; a-payroll-poster sends it, and its one bank file carries the funded orders (<c>ControlTotal</c> = what was
/// funded), the rest being arrears. No acceptance seed gives an employee a bank account (PLAN-58: an order with none is held
/// <c>NO-ACCOUNT</c>), so two employees first get a verified account by PLAN-55's two people; the others' orders stay held with their
/// funded amounts, and the file carries the two. Negative: a funding decision on a run that is only approved answers 400; the removed
/// <c>partial-disburse</c> answers 404; the processor sending answers 403; a second sending answers 409 and makes no second file.
/// The month is April 2027 of agency A (lab), as before.
/// </summary>
public class Ac24PartialDisbursementTests
{
    [SkippableFact]
    public async Task Partial_disbursement_http_path_invariant_and_idempotency()
    {
        AcceptanceEnv.RequireEnabled();
        var processor = AcceptanceHarness.RequirePersona("payroll-processor");
        var approver = AcceptanceHarness.RequirePersona("payroll-approver");
        var poster = AcceptanceHarness.RequirePersona("a-payroll-poster");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(processor);
        var recorder = new EvidenceRecorder("AC-24", correlationId);
        recorder.WriteMetadata("AC-24 partial disbursement: a funding decision after the posting (PAY-004, PLAN-58)", processor.Id);

        using var httpProcessor = await AcceptanceHarness.CreatePayrollClientAsync(processor, correlationId);
        using var httpApprover = await AcceptanceHarness.CreatePayrollClientAsync(approver, correlationId);
        using var httpPoster = await AcceptanceHarness.CreatePayrollClientAsync(poster, correlationId);

        // Two employees with a verified account of their own (before the calculation, whose snapshot pins it).
        var paid = await AcceptancePayrollAccounts.PickWithoutAccountAsync(recorder, "ac24", 2, lowest: true);
        foreach (var employee in paid)
            await AcceptancePayrollAccounts.GiveVerifiedAccountAsync(recorder, httpProcessor, httpApprover, employee,
                AcceptancePayrollAccounts.Iban(employee, "2424"), "كتاب المصرف — AC-24", "ac24");

        var periodStart = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        var create = await httpProcessor.PostAsync("api/payroll/runs", JsonContent(new
        {
            periodStart,
            periodEnd,
            runDate = DateTime.UtcNow
        }));
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.True(create.IsSuccessStatusCode, createBody);

        Guid runId;
        using (var doc = JsonDocument.Parse(createBody))
            runId = doc.RootElement.GetProperty("id").GetGuid();

        await RequireSuccessAsync(
            await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/calculate", null),
            recorder, "calculate", $"/api/payroll/runs/{runId:D}/calculate");
        await RequireSuccessAsync(
            await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/validate", null),
            recorder, "validate", $"/api/payroll/runs/{runId:D}/validate");
        await RequireSuccessAsync(
            await httpProcessor.PostAsync($"api/payroll/runs/{runId:D}/submit-for-review", null),
            recorder, "submit-for-review", $"/api/payroll/runs/{runId:D}/submit-for-review");
        var approved = await RequireSuccessAsync(
            await httpApprover.PostAsync($"api/payroll/runs/{runId:D}/approve", null),
            recorder, "approve", $"/api/payroll/runs/{runId:D}/approve");
        var approvals = await AcceptancePayrollReview.CompleteApprovalAsync(recorder, runId.ToString("D"), approved, "ac24");

        // Negative (D-PAY-2): no funding before the posting — the run is approved, not posted: 400, nothing made.
        var early = await httpApprover.PostAsJsonAsync($"api/payroll/runs/{runId:D}/funding-decisions", new { fundingRatio = 0.60m });
        var earlyBody = await early.Content.ReadAsStringAsync();
        recorder.CaptureHttp("funding-before-the-posting-negative", "POST", $"/api/payroll/runs/{runId:D}/funding-decisions", (int)early.StatusCode, ParseEvidenceBody(earlyBody));
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);

        // Negative: the partial disbursement is gone (one payment path).
        var removed = await httpProcessor.PostAsJsonAsync($"api/payroll/runs/{runId:D}/partial-disburse", new { fundingDecisionId = Guid.NewGuid() });
        recorder.CaptureHttp("partial-disburse-removed-negative", "POST", $"/api/payroll/runs/{runId:D}/partial-disburse", (int)removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);

        var review = await AcceptancePayrollReview.ReviewAsync(recorder, runId.ToString("D"), "ac24");
        var posted = await RequireSuccessAsync(
            await httpPoster.PostAsync($"api/payroll/runs/{runId:D}/post", null),
            recorder, "post", $"/api/payroll/runs/{runId:D}/post");

        var batchId = await BatchOfAsync(httpPoster, runId);
        var before = await BatchAsync(httpPoster, recorder, batchId, "batch-after-posting");
        var totalNet = before.GetProperty("totalAmount").GetDecimal();
        Assert.Equal("Pending", before.GetProperty("status").GetString());
        recorder.WriteBefore(new { runId, batchId, totalNet, approvals, review, posted });

        // The treasury funds 60% of the posted batch: the approved amount is spread over the orders, the rest is arrears.
        var funding = await RequireSuccessAsync(
            await httpApprover.PostAsJsonAsync($"api/payroll/payment-batches/{batchId:D}/funding-decisions", new { fundingRatio = 0.60m }),
            recorder, "funding-decision", $"/api/payroll/payment-batches/{batchId:D}/funding-decisions");

        // Negative: the processor does not send (the poster's act): 403.
        var byProcessor = await httpProcessor.PostAsync($"api/payroll/payment-batches/{batchId:D}/process", null);
        recorder.CaptureHttp("process-by-processor-negative", "POST", $"/api/payroll/payment-batches/{batchId:D}/process", (int)byProcessor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byProcessor.StatusCode);

        var sent = await RequireSuccessAsync(
            await httpPoster.PostAsync($"api/payroll/payment-batches/{batchId:D}/process", null),
            recorder, "process", $"/api/payroll/payment-batches/{batchId:D}/process");

        // A second sending: 409, and no second file.
        var replay = await httpPoster.PostAsync($"api/payroll/payment-batches/{batchId:D}/process", null);
        var replayBody = await replay.Content.ReadAsStringAsync();
        recorder.CaptureHttp("process-replay-negative", "POST", $"/api/payroll/payment-batches/{batchId:D}/process", (int)replay.StatusCode, ParseEvidenceBody(replayBody));
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        var after = await BatchAsync(httpPoster, recorder, batchId, "batch-after-sending");
        var files = after.GetProperty("files").EnumerateArray().ToList();
        var file = Assert.Single(files);
        var disbursed = after.GetProperty("disbursedAmount").GetDecimal();
        var arrears = after.GetProperty("arrearsAmount").GetDecimal();
        var orders = after.GetProperty("orders").EnumerateArray().ToList();
        var submitted = orders.Where(o => o.GetProperty("status").GetString() == "Submitted").Sum(o => o.GetProperty("amount").GetDecimal());
        var funded = orders.Where(o => o.GetProperty("status").GetString() != "Cancelled").Sum(o => o.GetProperty("amount").GetDecimal());
        var controlTotal = file.GetProperty("controlTotal").GetDecimal();

        recorder.WriteDbChecks(new
        {
            totalNet,
            fundingRatio = 0.60m,
            funding,
            sent,
            disbursed,
            arrears,
            submittedOrders = submitted,
            fundedOrders = funded,
            employeesWithAnAccount = paid,
            file = new { sequence = file.GetProperty("sequence").GetInt32(), kind = file.GetProperty("kind").GetString(), controlTotal, controlCount = file.GetProperty("controlCount").GetInt32() },
            invariant = "ControlTotal = Σ sent orders; Σ funded orders (sent and held) + arrears = the batch's total",
        });

        Assert.Equal("Submitted", after.GetProperty("status").GetString());
        Assert.True(after.GetProperty("isPartial").GetBoolean());
        Assert.Equal(submitted, controlTotal);
        // The file carries every sent order: these two employees', and those of any employee another class gave an account to.
        Assert.Equal(orders.Where(o => o.GetProperty("status").GetString() == "Submitted").Sum(o => o.GetProperty("count").GetInt32()),
            file.GetProperty("controlCount").GetInt32());
        var paidStatuses = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            $"SELECT COALESCE(string_agg(DISTINCT \"Status\", ','), '') FROM \"PaymentOrders\" WHERE \"PaymentBatchId\"='{batchId:D}' AND \"EmployeeId\" IN ({string.Join(",", paid.Select(e => $"'{e}'"))});");
        Assert.Equal("Submitted", paidStatuses);
        Assert.Equal(totalNet, funded + arrears);
        Assert.InRange(funded, totalNet * 0.60m - 1m, totalNet * 0.60m + 1m);
        Assert.True(arrears > 0m, "the unfunded part is arrears");
        recorder.WriteResult("Pass");
    }

    private static async Task<Guid> BatchOfAsync(HttpClient client, Guid runId)
    {
        var batchId = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            $"SELECT \"Id\"::text FROM \"PaymentBatches\" WHERE \"PayrollRunId\"='{runId:D}' AND \"BatchKind\"='Disbursement';");
        Assert.True(Guid.TryParse(batchId, out var id), $"the posting made no batch for {runId:D}: «{batchId}»");
        return id;
    }

    private static async Task<JsonElement> BatchAsync(HttpClient client, EvidenceRecorder recorder, Guid batchId, string label)
    {
        var response = await client.GetAsync($"api/payroll/payment-batches/{batchId:D}");
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp(label, "GET", $"/api/payroll/payment-batches/{batchId:D}", (int)response.StatusCode, ParseEvidenceBody(body));
        Assert.True(response.IsSuccessStatusCode, $"{label}: {body}");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static object? ParseEvidenceBody(string body)
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
        recorder.CaptureHttp(label, "POST", path, (int)response.StatusCode, ParseEvidenceBody(body));
        Assert.True(response.IsSuccessStatusCode, $"{label}: {body}");
        // An empty answer reads as an empty object (a default JsonElement cannot be written into the evidence).
        return JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }
}
