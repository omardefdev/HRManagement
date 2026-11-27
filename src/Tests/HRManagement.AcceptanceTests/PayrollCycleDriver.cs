using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-59 (P-10): one person's HTTP client for the payroll cycle — Payroll's API with its antiforgery token (a Payroll token is accepted
/// by every Payroll API write), the independent reviewer's paths, or HR.Core's attendance API — each call captured in the evidence
/// (<see cref="EvidenceRecorder.CaptureHttp"/>). A bulk step passes <c>capture: false</c> and captures its first and last calls itself.
/// No SQL anywhere: every check is a read of the product's own API.
/// </summary>
internal sealed class PayrollApi(HttpClient http, EvidenceRecorder recorder, string persona, string tag) : IDisposable
{
    public string Persona => persona;

    public static async Task<PayrollApi> PayrollAsync(EvidenceRecorder recorder, string persona, string tag) =>
        new(await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")), recorder, persona, tag);

    public static async Task<PayrollApi> ReviewerAsync(EvidenceRecorder recorder, string persona, string tag) =>
        new(await AcceptanceHarness.CreatePayrollAuditClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")), recorder, persona, tag);

    public static async Task<PayrollApi> AttendanceAsync(EvidenceRecorder recorder, string persona, string tag) =>
        new(await AcceptanceHarness.CreateAttendanceClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D")), recorder, persona, tag);

    /// <summary>The underlying client (the months' picker posts with it).</summary>
    public HttpClient Http => http;

    public async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(
        string step, HttpMethod method, string path, object? body = null, bool capture = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement json = default;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            json = JsonSerializer.Deserialize<JsonElement>(text);
        if (capture)
        {
            object captured = json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json;
            recorder.CaptureHttp($"{tag}-{step}-{persona}", method.Method, "/" + path, (int)response.StatusCode, captured);
        }

        return (response.StatusCode, json, text);
    }

    public Task<(HttpStatusCode Status, JsonElement Body, string Text)> PostAsync(string step, string path, object? body = null, bool capture = true) =>
        SendAsync(step, HttpMethod.Post, path, body ?? new { }, capture);

    public Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string step, string path, bool capture = true) =>
        SendAsync(step, HttpMethod.Get, path, capture: capture);

    /// <summary>A GET that must answer 200; returns its body.</summary>
    public async Task<JsonElement> ReadAsync(string step, string path, bool capture = true)
    {
        var (status, body, text) = await GetAsync(step, path, capture);
        Assert.True(status == HttpStatusCode.OK, $"{step}: GET {path} as {persona} answered {(int)status}: {Snip(text)}");
        return body;
    }

    public static string Error(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";

    public static string Snip(string text) => text.Length > 400 ? text[..400] : text;

    public void Dispose() => http.Dispose();
}

/// <summary>
/// PLAN-59 (P-10): the payroll cycle's acts and reads, shared by AC-PAY-59 and AC-12. Each act is one HTTP call (or one screen post, with
/// the URL and the token the page renders); each check is an API read. No SQL: no psql helper, no <c>QueryAcceptancePostgres*</c>, no
/// classification of records by SQL.
/// </summary>
internal static partial class PayrollCycleDriver
{
    /// <summary>HR.Core's attendance periods API, through the gateway.</summary>
    public const string PeriodsApi = "api/hr-core/api/attendance/periods";

    /// <summary>
    /// D-PAY-22: the first ended month of the class's candidates with no live regular run in agency A, skipping a month whose attendance
    /// <paramref name="central"/> reads as closed for anyone in agency A (<c>GET api/attendance/periods/{M}</c>: <c>closedEmployees</c>
    /// above 0). Creating the regular run of the month (201) makes it the class's run; the live-period refusal moves on. No month left:
    /// <see cref="AcceptanceGate.RequireData"/>, asking for <c>scripts/acceptance-reset.ps1</c>.
    /// </summary>
    public static async Task<(DateOnly Month, string RunId, IReadOnlyList<string> Refused)> CycleMonthAsync(
        PayrollApi processor, PayrollApi central, EvidenceRecorder recorder, string step, string owner, string stack) =>
        await AcceptancePayrollMonths.CreateRunAsync(processor.Http, recorder, step, AcceptancePayrollMonths.CycleCandidates(owner, stack),
            skip: async month => await ClosedEmployeesAsync(central, $"{step}-attendance-{AcceptancePayrollMonths.Ym(month)}", month) > 0);

    /// <summary>How many employees of agency A have a closed attendance summary for the month (0 for a month nobody closed).</summary>
    public static async Task<int> ClosedEmployeesAsync(PayrollApi central, string step, DateOnly month)
    {
        var (status, body, text) = await central.GetAsync(step, $"{PeriodsApi}/{AcceptancePayrollMonths.Ym(month)}");
        if (status == HttpStatusCode.NotFound)
            return 0;
        Assert.True(status == HttpStatusCode.OK, $"{step}: the attendance period of {AcceptancePayrollMonths.Ym(month)} answered {(int)status}: {PayrollApi.Snip(text)}");
        return body.TryGetProperty("closedEmployees", out var closed) && closed.ValueKind == JsonValueKind.Number ? closed.GetInt32() : 0;
    }

    /// <summary>
    /// The run's cancellation in a class's <c>finally</c> (PLAN-56): by the processor before the review, by the approver after it; a
    /// posted or already cancelled run is left as it is. Returns what was done.
    /// </summary>
    public static async Task<string> CancelUnlessPostedAsync(PayrollApi processor, PayrollApi approver, string step, string runId, string reason)
    {
        var (status, run, _) = await processor.GetAsync($"{step}-state", $"api/payroll/runs/{runId}");
        if (status != HttpStatusCode.OK)
            return $"not read ({(int)status})";
        var state = AcceptancePayrollMonths.RunStatus(run);
        if (state is "Posted" or "Cancelled" or "Reversed")
            return state;
        var actor = state is "UnderReview" or "Approved" ? approver : processor;
        var (cancelled, body, _) = await actor.PostAsync($"{step}-cancel", $"api/payroll/runs/{runId}/cancel", new { reason });
        return cancelled == HttpStatusCode.OK ? $"cancelled from {state} by {actor.Persona}" : $"cancel refused ({(int)cancelled}): {PayrollApi.Error(body)}";
    }
}
