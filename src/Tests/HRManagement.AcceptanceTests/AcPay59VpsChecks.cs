using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-59 (P9, P-15, P-16) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers, as the demo's
/// <c>e2e-payroll-processor</c> (the password read into memory only; evidence under <c>artifacts/plan59/vps</c>, never committed).
/// <para>
/// <b>These checks only read.</b> They add no user and calculate, post, fund, process or settle nothing: the release (its tag, every
/// container healthy, no payroll migration pending), the run read's new fields on a demo run, the orders read on a demo batch (its count,
/// no account number and no field named like one), and the demo's runs, lines, batches, orders and arrears as in the picture taken before
/// the deploy (<c>PAY059_VPS_RUNS</c>, <c>_LINES</c>, <c>_BATCHES</c>, <c>_ORDERS</c>, <c>_ARREARS</c>: «count|md5», never an amount).
/// </para>
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Pay59Processor = "e2e-payroll-processor";
    private const string Pay59Api = "/ui/payroll/api/payroll";

    private static readonly string Pay59Out = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan59", "vps");

    private static EvidenceRecorder StartPay59(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"pay59-{key}-{Guid.NewGuid():N}", Pay59Out) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "the demo's payroll processor (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static Dictionary<string, string> Pay59Expected() =>
        new[] { "RUNS", "LINES", "BATCHES", "ORDERS", "ARREARS" }
            .Select(k => (k, v: Environment.GetEnvironmentVariable($"PAY059_VPS_{k}") ?? ""))
            .Where(x => x.v.Length > 0)
            .ToDictionary(x => x.k, x => x.v, StringComparer.Ordinal);

    private static async Task<(int Status, JsonElement Body, string Text)> Pay59GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"https://{AppDomain}{Pay59Api}/{path}");
        var text = await response.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<JsonElement>(text) : default;
        return ((int)response.StatusCode, json, text);
    }

    private static IEnumerable<string> Pay59Names(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Pay59Names(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Pay59Names),
        _ => [],
    };

    [SkippableFact]
    public void AC_PAY_59_vps_the_release_runs_every_container_is_healthy_and_no_payroll_migration_is_pending()
    {
        var recorder = StartPay59("release", "Demo server: PLAN-59's tag runs, every container healthy, no payroll migration pending");
        var expectedTag = Environment.GetEnvironmentVariable("PAY059_VPS_TAG") ?? "";
        var tags = Sh("docker ps --format '{{.Image}}' | grep -o ':[0-9a-f]\\{12\\}$' | sort | uniq -c").Output.Trim();
        var unhealthy = Sh("docker ps --format '{{.Names}} {{.Status}}' | grep -v '(healthy)' | grep -ci 'starting\\|unhealthy' || true").Output.Trim();
        var latest = Directory.GetFiles(Path.Combine(AcceptanceHarness.RepoRoot, "src", "Services", "Payroll", "Payroll.Infrastructure", "Migrations"), "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !n.EndsWith(".Designer", StringComparison.Ordinal) && !n.EndsWith("ModelSnapshot", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Last();
        var applied = Pay58One("select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\" desc limit 1");
        recorder.WriteScenarioDbChecks(new { expectedTag, tags, unhealthy, latestMigrationInTheTree = latest, lastAppliedOnTheDemo = applied });

        Assert.False(string.IsNullOrWhiteSpace(expectedTag), "Set PAY059_VPS_TAG to the deployed tag.");
        Assert.Contains(":" + expectedTag, tags, StringComparison.Ordinal);
        Assert.DoesNotMatch(@":(?!" + expectedTag + @")[0-9a-f]{12}", tags);
        Assert.Equal("0", unhealthy);
        Assert.Equal(latest, applied);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_59_vps_a_demo_run_answers_its_revision_approvals_and_batch()
    {
        var recorder = StartPay59("run-read", "Demo server: GET api/payroll/runs/{id} answers revision, approvalsGiven, approvalsRequired and paymentBatchId");
        var password = DemoPassword();
        var (client, _) = await SignInAsync(Pay59Processor, password, "/ui/payroll/");
        using var _client = client;
        var posted = Pay58One("select \"Id\" from \"PayrollRuns\" where \"Status\" = 2 order by \"PeriodStart\" desc limit 1");
        var batch = Pay58One($"select coalesce((select \"Id\"::text from \"PaymentBatches\" where \"PayrollRunId\" = '{posted}' and \"BatchKind\" = 'Disbursement' order by (\"Source\" = 1) desc, \"CreatedAt\" limit 1), '')");
        var revision = Pay58One($"select \"Revision\" from \"PayrollRuns\" where \"Id\" = '{posted}'");
        var (status, run, text) = await Pay59GetAsync(client, $"runs/{posted}");
        recorder.WriteScenarioDbChecks(new
        {
            posted,
            status,
            revision = status == 200 ? run.GetProperty("revision").GetInt32() : -1,
            approvalsGiven = status == 200 ? run.GetProperty("approvalsGiven").GetInt32() : -1,
            approvalsRequired = status == 200 ? run.GetProperty("approvalsRequired").GetInt32() : -1,
            paymentBatchId = status == 200 ? run.GetProperty("paymentBatchId").ToString() : "",
            batchOnTheDemo = batch,
            revisionOnTheDemo = revision,
        });

        Assert.True(status == 200, $"GET runs/{posted}: {status} {(text.Length > 200 ? text[..200] : text)}");
        Assert.Equal(revision, run.GetProperty("revision").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(run.GetProperty("approvalsRequired").GetInt32() >= 1);
        Assert.True(run.GetProperty("approvalsGiven").GetInt32() >= 0);
        if (batch.Length > 0)
            Assert.Equal(batch, run.GetProperty("paymentBatchId").GetString());
        else
            Assert.Equal(JsonValueKind.Null, run.GetProperty("paymentBatchId").ValueKind);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_59_vps_a_demo_batch_answers_its_orders_without_an_account_number()
    {
        var recorder = StartPay59("orders-read", "Demo server: GET api/payroll/payment-batches/{id}/orders answers the batch's orders, no account number, no field named like one");
        var password = DemoPassword();
        var (client, _) = await SignInAsync(Pay59Processor, password, "/ui/payroll/");
        using var _client = client;
        var batch = Pay58One("select \"Id\" from \"PaymentBatches\" order by \"CreatedAt\" desc limit 1");
        var count = Pay58One($"select count(*) from \"PaymentOrders\" where \"PaymentBatchId\" = '{batch}'");
        var numbers = Sql("payroll_db", $"select coalesce(\"BeneficiaryIban\", '') from \"PaymentOrders\" where \"PaymentBatchId\" = '{batch}'")
            .Where(n => n.Length > 0).ToList();
        var (status, body, text) = await Pay59GetAsync(client, $"payment-batches/{batch}/orders");
        var names = status == 200 ? Pay59Names(body).Distinct().ToList() : [];
        recorder.WriteScenarioDbChecks(new { batch, status, total = status == 200 ? body.GetProperty("total").GetInt32() : -1, ordersOnTheDemo = count, names });

        Assert.True(status == 200, $"GET payment-batches/{batch}/orders: {status} {(text.Length > 200 ? text[..200] : text)}");
        Assert.Equal(count, body.GetProperty("total").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.All(numbers, number => Assert.DoesNotContain(number, text, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("account", StringComparison.OrdinalIgnoreCase) || n.Contains("iban", StringComparison.OrdinalIgnoreCase)
                                          || n.Contains("beneficiary", StringComparison.OrdinalIgnoreCase));
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public void AC_PAY_59_vps_nothing_moved_the_runs_lines_batches_orders_and_arrears_are_as_in_the_picture()
    {
        var recorder = StartPay59("picture", "Demo server: the runs, lines, batches, orders and arrears as in the picture taken before the deploy");
        var picture = Pay58Picture();
        var expected = Pay59Expected();
        recorder.WriteScenarioDbChecks(new { picture, expected, pictureFormat = "count|md5 (picture.sh's expressions; no amount)" });

        Assert.Equal(5, expected.Count);
        foreach (var (key, value) in expected)
            Assert.True(picture[key] == value, $"{key}: {picture[key]} on the demo, {value} in the picture before the deploy");
        recorder.WriteResult("Pass");
    }
}
