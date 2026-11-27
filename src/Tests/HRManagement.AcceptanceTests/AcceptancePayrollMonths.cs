using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.TestSupport;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-59 (P-8, D-PAY-22): the payroll months of the acceptance classes, from one registry
/// (<c>testdata/acceptance/payroll-months.json</c>), and one picker that finds a class's month by the product's own API — it creates the
/// regular run of the first candidate month (201 makes it the class's run) and moves on at the live-period refusal (400 with its text).
/// No SQL. A class that finds no month stops with <see cref="AcceptanceGate.RequireData"/>, asking for <c>scripts/acceptance-reset.ps1</c>.
/// What a class does not keep it cancels through PLAN-56's API (<see cref="CancelRunAsync"/>), in <c>finally</c>.
/// </summary>
internal static class AcceptancePayrollMonths
{
    /// <summary>The live-period refusal (<c>CreatePayrollRunHandler</c>): agency A already has a live regular run of the month.</summary>
    public const string LivePeriodRefusal = "يوجد تشغيل رواتب حي لنفس الفترة والنوع مسبقاً.";

    /// <summary>D-PAY-22's first month to try (<c>yyyy-MM</c>); default 12 months before today (Damascus).</summary>
    public const string CycleFromVariable = "ACCEPTANCE_PAYROLL_CYCLE_FROM";

    public const string MainStack = "prodlike-main";
    public const string CycleStack = "prodlike-cycle";

    private static readonly string[] SeedOwners = ["seed", "seed-scenarios"];

    internal sealed record Entry(string Owner, IReadOnlyList<string> Stacks, IReadOnlyList<DateOnly> Months, string? Rule);

    private static readonly Lazy<IReadOnlyList<Entry>> Registry = new(() =>
        LoadRegistry(File.ReadAllText(Path.Combine(RepoPaths.Root, "testdata", "acceptance", "payroll-months.json"))));

    private static readonly Lazy<IReadOnlyList<DateOnly>> Lva057 = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "testdata", "acceptance", "lva057-periods.json")));
        return doc.RootElement.GetProperty("months").EnumerateObject().Select(p => Month(p.Value.GetString()!)).Distinct().Order().ToList();
    });

    public static DateOnly Month(string yyyyMm) => DateOnly.ParseExact(yyyyMm + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Ym(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static IReadOnlyList<DateOnly> Range(string from, string to)
    {
        var months = new List<DateOnly>();
        for (var m = Month(from); m <= Month(to); m = m.AddMonths(1))
            months.Add(m);
        return months;
    }

    internal static IReadOnlyList<Entry> LoadRegistry(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("entries").EnumerateArray().Select(e => new Entry(
                e.GetProperty("owner").GetString()!,
                e.GetProperty("stacks").EnumerateArray().Select(s => s.GetString()!).ToList(),
                e.TryGetProperty("from", out var from) ? Range(from.GetString()!, e.GetProperty("to").GetString()!)
                : e.TryGetProperty("window", out var w) ? Range(w.GetProperty("from").GetString()!, w.GetProperty("to").GetString()!)
                : [],
                e.TryGetProperty("rule", out var rule) ? rule.GetString() : null))
            .ToList();
    }

    /// <summary>Today on the service's clock (Localization:TimeZone, Asia/Damascus).</summary>
    public static DateOnly Today => DateOnly.FromDateTime(
        HRManagement.ServiceDefaults.Localization.HrLocalTime.ToLocal(DateTime.UtcNow, HRManagement.ServiceDefaults.Localization.HrLocalTime.DefaultZone));

    public static DateOnly CurrentMonth => new(Today.Year, Today.Month, 1);

    /// <summary>The window an entry gives its class (AC-21, AC-22).</summary>
    public static IReadOnlyList<DateOnly> WindowOf(string owner) => Registry.Value.Single(e => e.Owner == owner).Months;

    /// <summary>
    /// The months the registry gives on <paramref name="stack"/> to owners other than <paramref name="owner"/>: their absolute months and
    /// windows, and the current month for a <c>current-month</c> entry. The seed's months only when <paramref name="withSeed"/>.
    /// </summary>
    public static IReadOnlySet<DateOnly> TakenByOthers(string owner, string stack, bool withSeed)
    {
        var taken = new HashSet<DateOnly>();
        foreach (var e in Registry.Value.Where(e => e.Owner != owner && e.Stacks.Contains(stack)))
        {
            if (!withSeed && SeedOwners.Contains(e.Owner))
                continue;
            taken.UnionWith(e.Months);
            if (e.Rule == "current-month")
            {
                taken.Add(CurrentMonth);
                taken.Add(CurrentMonth.AddMonths(1));
            }
        }

        return taken;
    }

    /// <summary>
    /// D-PAY-22's candidates for <paramref name="owner"/> on <paramref name="stack"/>: every ended month from
    /// <see cref="CycleFromVariable"/> (default 12 months before today) to the last ended month, less PLAN-57's months and the months
    /// another class's entry gives on the stack. (A month with a closed attendance summary is skipped by the caller, through its read.)
    /// </summary>
    public static IReadOnlyList<DateOnly> CycleCandidates(string owner, string stack)
    {
        var configured = Environment.GetEnvironmentVariable(CycleFromVariable);
        var from = string.IsNullOrWhiteSpace(configured) ? CurrentMonth.AddMonths(-12) : Month(configured.Trim());
        var taken = TakenByOthers(owner, stack, withSeed: false);
        var months = new List<DateOnly>();
        for (var m = from; m < CurrentMonth; m = m.AddMonths(1))
        {
            if (!Lva057.Value.Contains(m) && !taken.Contains(m))
                months.Add(m);
        }

        return months;
    }

    /// <summary>
    /// The <c>current+2</c> rule's candidates: a month from the current month + 2 on (for 48 months) such that it and the
    /// <paramref name="count"/> - 1 months after it are given by no other entry on the stack, the seed's included.
    /// </summary>
    public static IReadOnlyList<DateOnly> AfterNextCandidates(string owner, string stack, int count)
    {
        var taken = TakenByOthers(owner, stack, withSeed: true);
        var first = CurrentMonth.AddMonths(2);
        return Enumerable.Range(0, 48)
            .Select(i => first.AddMonths(i))
            .Where(m => Enumerable.Range(0, count).All(k => !taken.Contains(m.AddMonths(k))))
            .ToList();
    }

    public static object RunBody(DateOnly month)
    {
        var end = month.AddMonths(1).AddDays(-1);
        return new
        {
            periodStart = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            periodEnd = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            runDate = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };
    }

    /// <summary>
    /// Creates, as <paramref name="processor"/>, the regular run of the first of <paramref name="candidates"/> agency A has no live regular
    /// run of (201), skipping a month at the live-period refusal and any month <paramref name="skip"/> says to skip. Returns the month and
    /// the run's id; no month left → <see cref="AcceptanceGate.RequireData"/> (start a fresh stack: <c>scripts/acceptance-reset.ps1</c>).
    /// </summary>
    public static async Task<(DateOnly Month, string RunId, IReadOnlyList<string> Refused)> CreateRunAsync(
        HttpClient processor, EvidenceRecorder recorder, string step, IEnumerable<DateOnly> candidates, Func<DateOnly, Task<bool>>? skip = null)
    {
        var refused = new List<string>();
        foreach (var month in candidates)
        {
            if (skip is not null && await skip(month))
            {
                refused.Add($"{Ym(month)}: skipped");
                continue;
            }

            using var response = await processor.PostAsJsonAsync("api/payroll/runs", RunBody(month));
            var text = await response.Content.ReadAsStringAsync();
            var json = text.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<JsonElement>(text) : default;
            recorder.CaptureHttp($"{step}-create-run-{Ym(month)}", "POST", "/api/payroll/runs", (int)response.StatusCode,
                json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json);
            if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
                return (month, json.GetProperty("id").GetGuid().ToString("D"), refused);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest && text.Contains(LivePeriodRefusal, StringComparison.Ordinal),
                $"{step}: creating the run of {Ym(month)} answered {(int)response.StatusCode}: {(text.Length > 300 ? text[..300] : text)}");
            refused.Add($"{Ym(month)}: live regular run");
        }

        AcceptanceGate.RequireData(recorder, false,
            $"{step}: لا يبقى شهر بلا تشغيل رواتب اعتيادي حي في الجهة A ضمن نافذة الصنف؛ ابدأ مكدساً جديداً بـ scripts/acceptance-reset.ps1",
            $"{step}: no month of the class's window is free of a live regular run in agency A ({string.Join("; ", refused)}): start a fresh stack (scripts/acceptance-reset.ps1).");
        throw new InvalidOperationException("unreachable");
    }

    private static readonly string[] RunStatuses =
        ["Draft", "Calculated", "Posted", "UnderReview", "Approved", "Reversed", "Calculating", "Validating", "Cancelled"];

    /// <summary>A run's state by name, from <c>GET api/payroll/runs/{id}</c> (the read answers the enum's number).</summary>
    public static string RunStatus(JsonElement run)
    {
        var status = run.GetProperty("status");
        return status.ValueKind == JsonValueKind.String ? status.GetString() ?? "" : RunStatuses[status.GetInt32()];
    }

    /// <summary><c>GET api/payroll/runs/{id}</c>: the run, its lines, its version, its approvals and its batch (PLAN-59, P-9).</summary>
    public static async Task<JsonElement> ReadRunAsync(HttpClient http, string runId)
    {
        using var response = await http.GetAsync($"api/payroll/runs/{runId}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET api/payroll/runs/{runId}: {(int)response.StatusCode} {(body.Length > 300 ? body[..300] : body)}");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    /// <summary>
    /// Cancels a run through PLAN-56's API (<c>POST api/payroll/runs/{id}/cancel</c>, with a reason and the antiforgery token the client
    /// carries): the processor before the review, the approver after it. Frees the month and the inputs. Returns the answer's status.
    /// </summary>
    public static async Task<HttpStatusCode> CancelRunAsync(HttpClient client, EvidenceRecorder recorder, string step, string runId, string reason)
    {
        using var response = await client.PostAsJsonAsync($"api/payroll/runs/{runId}/cancel", new { reason });
        var text = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp($"{step}-cancel-run", "POST", $"/api/payroll/runs/{runId}/cancel", (int)response.StatusCode,
            text.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<JsonElement>(text) : (text.Length > 300 ? text[..300] : text));
        return response.StatusCode;
    }
}
