using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-13 (PLAN-48 §5, AC-TEN-48h): 120 official numbers generated at once through the service path (PLAN-42: the owning service's
/// token and the tenant header), in two tenants and three kinds: AssetTag (svc-admin-logistics), ServiceTransaction with two type
/// codes (svc-hr-contracts) and ArchiveReference with two prefixes (svc-records-archive). Every number is unique in its tenant (D-TEN-3: no agency segment), carries its prefix
/// and its tenant; the database holds one issuance per number in the right tenant, and each counter moved by exactly the numbers of
/// its scope (the type codes and prefixes are this class's own, so no other class moves those counters). No number crosses scopes.
/// </summary>
[RequiredAcceptance]
public class Ac13ParallelCodeGenerationTests
{
    private const string Ac = "AC-13";
    private static readonly int Year = DateTime.UtcNow.Year;

    private sealed record Plan(Guid Tenant, string TenantLabel, CodeEntityKind Kind, string Client, string? ScopeValue, int Count, Regex Format);

    private sealed record Issued(Plan Plan, int Status, string? Code);

    private static Plan[] Plans()
    {
        var plans = new List<Plan>();
        foreach (var (tenant, label) in new[] { (AcceptanceSeedIds.AgencyAId, "AGENCY-A"), (AcceptanceSeedIds.AgencyBId, "AGENCY-B") })
        {
            plans.Add(new(tenant, label, CodeEntityKind.AssetTag, "svc-admin-logistics", null, 20, new($@"^AH-{Year}-\d{{6}}$")));
            foreach (var type in new[] { "A13X", "A13Y" })
                plans.Add(new(tenant, label, CodeEntityKind.ServiceTransaction, "svc-hr-contracts", type, 10, new($@"^SRV-{type}-{Year}-\d{{5}}$")));
            foreach (var prefix in new[] { "A13P", "A13Q" })
                plans.Add(new(tenant, label, CodeEntityKind.ArchiveReference, "svc-records-archive", prefix, 10, new($@"^ARC-{prefix}-{Year}-\d{{5}}$")));
        }

        return [.. plans];
    }

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);

    /// <summary>
    /// The counter of a scope, 0 when it has none yet. The key is CodeScopeKey's: tenant (N), kind, scope value, year; the default
    /// asset tag's key has no policy key, so the privileged (VIP) counter is never read for it.
    /// </summary>
    private static async Task<long> CounterAsync(Plan plan)
    {
        var key = plan.ScopeValue is null
            ? $"{plan.Tenant:N}|{plan.Kind}|{Year}"
            : $"{plan.Tenant:N}|{plan.Kind}|{plan.ScopeValue}|{Year}";
        var text = (await Sql($"""
            SELECT coalesce(max("LastValue"), 0) FROM "CodeSequenceCounters"
            WHERE "TenantId" = '{plan.Tenant:D}' AND "EntityKind" = '{plan.Kind}' AND "ScopeKey" = '{key}';
            """)).Trim();
        return long.Parse(text, CultureInfo.InvariantCulture);
    }

    [SkippableFact]
    public async Task Parallel_generation_in_two_tenants_and_three_kinds_is_unique_prefixed_and_counted()
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D"));
        recorder.WriteMetadata("AC-13 parallel code generation: 120 at once, two tenants, three kinds, five scopes per tenant", "svc-admin-logistics, svc-hr-contracts, svc-records-archive");
        AcceptanceGate.RequireEnabled(recorder);

        var plans = Plans();
        Assert.Equal(120, plans.Sum(p => p.Count));

        var clients = new Dictionary<(string Client, Guid Tenant), HttpClient>();
        foreach (var key in plans.Select(p => (p.Client, p.Tenant)).Distinct())
            clients[key] = await AcceptanceHarness.CreateServiceClientAsync(key.Client, ServiceScopes.HrCoreWorkflow, recorder, key.Tenant);

        try
        {
            var before = new Dictionary<Plan, long>();
            foreach (var plan in plans)
                before[plan] = await CounterAsync(plan);
            recorder.WriteBefore(plans.Select(p => new { tenant = p.TenantLabel, kind = p.Kind.ToString(), scope = p.ScopeValue, counter = before[p] }));

            var work = plans.SelectMany(p => Enumerable.Range(0, p.Count).Select(_ => p)).ToList();
            var issued = new ConcurrentBag<Issued>();
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = 30 }, async (plan, ct) =>
            {
                var request = new CodeGenerationRequest { EntityKind = plan.Kind, Preview = false };
                if (plan.Kind == CodeEntityKind.ServiceTransaction)
                    request.Facts["TypeCode"] = plan.ScopeValue!;
                if (plan.Kind == CodeEntityKind.ArchiveReference)
                    request.Facts["EntityPrefix"] = plan.ScopeValue!;
                using var response = await clients[(plan.Client, plan.Tenant)].PostAsync("api/internal/codes/generate",
                    new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"), ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                string? code = null;
                if (response.IsSuccessStatusCode)
                    code = JsonDocument.Parse(body).RootElement.GetProperty("code").GetString();
                issued.Add(new Issued(plan, (int)response.StatusCode, code));
            });

            var results = issued.ToList();
            var codes = results.Where(r => r.Code is not null).Select(r => r.Code!).ToList();
            recorder.CaptureHttp("generate-batch", "POST", "/api/internal/codes/generate",
                results.All(r => r.Status == 200) ? 200 : results.First(r => r.Status != 200).Status,
                new
                {
                    requested = work.Count,
                    succeeded = codes.Count,
                    distinctPerTenant = results.Where(r => r.Code is not null).Select(r => $"{r.Plan.Tenant:D}|{r.Plan.Kind}|{r.Code}").Distinct(StringComparer.Ordinal).Count(),
                    statusCounts = results.GroupBy(r => r.Status).ToDictionary(g => g.Key.ToString(CultureInfo.InvariantCulture), g => g.Count())
                });

            Assert.All(results, r => Assert.Equal(200, r.Status));
            Assert.Equal(120, codes.Count);
            // D-TEN-3: a number carries no agency segment and is unique within its tenant, so two tenants may hold the same text.
            var issuedKeys = results.Select(r => $"{r.Plan.Tenant:D}|{r.Plan.Kind}|{r.Code}").ToList();
            Assert.Equal(120, issuedKeys.Distinct(StringComparer.Ordinal).Count());
            Assert.All(results, r => Assert.Matches(r.Plan.Format, r.Code!));
            Assert.DoesNotContain(codes, c => c.StartsWith('-'));

            // The database: one issuance per number, in the tenant that asked for it (matched on tenant, kind and code).
            var values = string.Join(",", results.Select(r => $"('{r.Plan.Tenant:D}','{r.Plan.Kind}','{r.Code}')"));
            var rows = (await Sql($"""
                SELECT i."TenantId" || '|' || i."EntityKind" || '|' || i."Code" FROM "CodeIssuances" i
                JOIN (VALUES {values}) AS ours(tenant, kind, code)
                  ON i."TenantId"::text = ours.tenant AND i."EntityKind" = ours.kind AND i."Code" = ours.code
                ORDER BY 1;
                """)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(120, rows.Length);
            Assert.Equal(issuedKeys.OrderBy(k => k, StringComparer.Ordinal), rows.OrderBy(k => k, StringComparer.Ordinal));

            // The counters: this class's own scopes moved by exactly their numbers; the asset tag counter (shared with the other
            // classes of the stack) moved by at least its numbers, and every number this class got lies above where it started.
            var after = new Dictionary<Plan, long>();
            var counters = new List<object>();
            foreach (var plan in plans)
            {
                after[plan] = await CounterAsync(plan);
                var numbers = results.Where(r => r.Plan == plan)
                    .Select(r => long.Parse(r.Code![(r.Code!.LastIndexOf('-') + 1)..], CultureInfo.InvariantCulture)).OrderBy(n => n).ToList();
                if (plan.ScopeValue is not null)
                {
                    Assert.Equal(before[plan] + plan.Count, after[plan]);
                    Assert.Equal(Enumerable.Range(1, plan.Count).Select(i => before[plan] + i), numbers);
                }
                else
                {
                    Assert.True(after[plan] >= before[plan] + plan.Count, $"{plan.TenantLabel} {plan.Kind}: {before[plan]} -> {after[plan]}");
                    Assert.All(numbers, n => Assert.True(n > before[plan] && n <= after[plan]));
                }

                counters.Add(new { tenant = plan.TenantLabel, kind = plan.Kind.ToString(), scope = plan.ScopeValue, before = before[plan], after = after[plan], issued = plan.Count, first = numbers.First(), last = numbers.Last() });
            }

            recorder.WriteDbChecks(new
            {
                requested = 120,
                issuancesFound = rows.Length,
                distinctPerTenant = issuedKeys.Distinct(StringComparer.Ordinal).Count(),
                prefixless = codes.Count(c => c.StartsWith('-')),
                counters,
                sample = plans.Select(p => results.First(r => r.Plan == p).Code).ToArray()
            });
            recorder.WriteAfter(new { counters });
            recorder.WriteResult("Pass");
        }
        finally
        {
            foreach (var http in clients.Values)
                http.Dispose();
        }
    }
}
