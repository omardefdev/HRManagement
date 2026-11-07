using System.Net;
using System.Text.RegularExpressions;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-45: the custody journey through the pages a person uses. Every POST takes the URL, the fields and the token from the page that
/// renders the form, because a hand-built URL hid a gateway gap once (PLAN-44). AC-11, AC-33, AC-WF-43 and AC-WF-45 share it.
/// </summary>
internal static class CustodyJourney
{
    public const string Proof = "AC-WF-45-EVIDENCE";
    public const string ApproveAction = "ApproveWorkflow";
    public const string RejectAction = "RejectWorkflow";
    public const string RequesterRefusal = "لا يجوز لطالب الإجراء";
    public const string StartingLabel = "بانتظار بدء الاعتماد";

    public const int Pending = 0;
    public const int Active = 1;
    public const int Starting = 4;
    public const int Withdrawn = 5;
    public const int LossConfirmed = 1;
    public const int AssetInUse = 0;
    public const int AssetInStorage = 1;
    public const int AssetLost = 4;

    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(30);

    public static string ApprovalPage(Guid instance) => $"ui/hr-core/WorkflowApprovals/Details/{instance:D}";

    public static string AssetPage(Guid asset) => $"ui/admin-logistics/Assets/Details/{asset:D}";

    public static Task<string> Sql(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    public static async Task<string> ScalarAsync(string database, string sql) => (await Sql(database, sql)).Trim();

    public static Task<long> CountAsync(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(database, sql);

    public static async Task<(HttpStatusCode Status, string Html)> OpenAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>The form on <paramref name="page"/> that posts to <paramref name="action"/>, as the page renders it.</summary>
    private static (string Url, Dictionary<string, string> Fields)? FindForm(string rawHtml, string action)
    {
        foreach (Match form in Regex.Matches(rawHtml, @"<form\b[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var url = Regex.Match(form.Value, @"action=""/([^""]*/" + action + @"[^""]*)""");
            if (!url.Success)
                continue;

            var fields = new Dictionary<string, string>();
            foreach (Match input in Regex.Matches(form.Value, @"<input\b[^>]*type=""hidden""[^>]*>", RegexOptions.IgnoreCase))
            {
                var name = Regex.Match(input.Value, @"name=""([^""]+)""");
                var value = Regex.Match(input.Value, @"value=""([^""]*)""");
                if (name.Success)
                    fields[name.Groups[1].Value] = WebUtility.HtmlDecode(value.Groups[1].Value);
            }

            return (WebUtility.HtmlDecode(url.Groups[1].Value), fields);
        }

        return null;
    }

    public static async Task<bool> HasFormAsync(HttpClient http, string page, string action)
    {
        using var response = await http.GetAsync(page);
        return FindForm(await response.Content.ReadAsStringAsync(), action) is not null;
    }

    /// <summary>Posts the rendered form, and answers the page it comes back to.</summary>
    public static async Task<string> PostFormAsync(
        HttpClient http, string page, string action, IReadOnlyDictionary<string, string>? extra = null)
    {
        using var opened = await http.GetAsync(page);
        var raw = await opened.Content.ReadAsStringAsync();
        Assert.True(opened.StatusCode == HttpStatusCode.OK, $"GET {page}: {(int)opened.StatusCode}");
        var form = FindForm(raw, action);
        Assert.True(form is not null, $"{page} has no {action} form: {AcS2s42TrustBoundaryTests.Alerts(WebUtility.HtmlDecode(raw))}");

        var (url, fields) = form!.Value;
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            fields[key] = value;

        using var posted = await http.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.True((int)posted.StatusCode < 500, $"POST {url}: {(int)posted.StatusCode}");
        return (await OpenAsync(http, page)).Html;
    }

    public static async Task<(HttpStatusCode Status, string Html)> PostWithoutFormAsync(HttpClient http, string page, string url)
    {
        var (_, html) = await OpenAsync(http, page);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AcMsg40AtomicPublishingTests.RequireAntiforgeryToken(html),
            ["Comments"] = "PLAN-45",
            ["TargetStepLevel"] = "1"
        };
        using var posted = await http.PostAsync(url, new FormUrlEncodedContent(fields));
        return (posted.StatusCode, (await OpenAsync(http, page)).Html);
    }

    /// <summary>The approve URL the page renders for someone who holds the step.</summary>
    public static async Task<string> ApproveUrlAsync(HttpClient holder, Guid instance)
    {
        using var opened = await holder.GetAsync(ApprovalPage(instance));
        var form = FindForm(await opened.Content.ReadAsStringAsync(), ApproveAction);
        Assert.True(form is not null, "The holder of the step sees no approve form.");
        return form!.Value.Url;
    }

    /// <summary>The link the inbox gives, opened and approved from there.</summary>
    public static async Task<string> ApproveFromInboxAsync(string persona, Guid instance, EvidenceRecorder recorder)
    {
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync(persona);
        var (status, inbox) = await OpenAsync(http, "ui/hr-core/WorkInbox");
        recorder.CaptureHttp($"inbox-{persona}", "GET", "/ui/hr-core/WorkInbox", (int)status);
        Assert.True(status == HttpStatusCode.OK, $"{persona} inbox: {(int)status}");
        var link = $"/{ApprovalPage(instance)}";
        Assert.True(inbox.Contains(link, StringComparison.OrdinalIgnoreCase), $"{persona} has no {link} in the inbox.");

        var after = await PostFormAsync(http, ApprovalPage(instance), ApproveAction, new Dictionary<string, string> { ["Comments"] = "PLAN-45" });
        recorder.CaptureHttp($"approve-{persona}", "POST", $"/ui/hr-core/WorkflowApprovals/ApproveWorkflow/{instance:D}", 200);
        return after;
    }

    public static async Task<(Guid Assignment, Guid Instance)> AssignmentOfAsync(Guid asset)
    {
        var row = await ScalarAsync("admin_db", $"SELECT \"Id\" || ',' || coalesce(\"WorkflowInstanceId\"::text,'') FROM \"AssetAssignments\" WHERE \"AssetId\"='{asset:D}' AND \"ReturnedAt\" IS NULL;");
        var parts = row.Split(',');
        Assert.True(parts.Length == 2 && Guid.TryParse(parts[0], out _), $"No open assignment: '{row}'");
        return (Guid.Parse(parts[0]), Guid.TryParse(parts[1], out var instance) ? instance : Guid.Empty);
    }

    public static async Task<(Guid Report, Guid Instance)> LossOfAsync(Guid asset)
    {
        var row = await ScalarAsync("admin_db", $"SELECT \"Id\" || ',' || coalesce(\"WorkflowInstanceId\"::text,'') FROM \"AssetLossReports\" WHERE \"AssetId\"='{asset:D}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        var parts = row.Split(',');
        Assert.True(parts.Length == 2 && Guid.TryParse(parts[0], out _), $"No loss report: '{row}'");
        return (Guid.Parse(parts[0]), Guid.TryParse(parts[1], out var instance) ? instance : Guid.Empty);
    }

    public static Task<long> AssignmentStatusAsync(Guid assignment) =>
        CountAsync("admin_db", $"SELECT \"Status\" FROM \"AssetAssignments\" WHERE \"Id\"='{assignment:D}';");

    public static Task<long> LossStatusAsync(Guid report) =>
        CountAsync("admin_db", $"SELECT \"Status\" FROM \"AssetLossReports\" WHERE \"Id\"='{report:D}';");

    public static Task<long> AssetStatusAsync(Guid asset) =>
        CountAsync("admin_db", $"SELECT \"Status\" FROM \"Assets\" WHERE \"Id\"='{asset:D}';");

    public static Task<string> InstanceStatusAsync(Guid instance) =>
        ScalarAsync("hr_core_db", $"SELECT \"Status\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';");

    public static async Task<long> PollAsync(Func<Task<long>> read, long expected, TimeSpan? window = null)
    {
        var deadline = DateTime.UtcNow + (window ?? DeliveryWindow);
        var value = await read();
        while (value != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            value = await read();
        }

        return value;
    }

    /// <summary>A privileged asset handed over by the maker through the page: pending, with its instance.</summary>
    public static async Task<(Guid Asset, Guid Assignment, Guid Instance)> PrivilegedHandoverAsync(string persona = "a-logistics-maker")
    {
        var asset = await AcS2s42TrustBoundaryTests.InsertAssetAsync("حواسيب", privileged: true);
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync(persona);
        var (_, page) = await AcS2s42TrustBoundaryTests.AssignAsync(http, asset, AcceptanceSeedIds.EmpBr1Id, Proof);
        var (assignment, instance) = await AssignmentOfAsync(asset);
        Assert.True(instance != Guid.Empty, $"The assignment has no instance: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        Assert.Equal(Pending, await AssignmentStatusAsync(assignment));
        return (asset, assignment, instance);
    }

    /// <summary>A loss reported by the maker through the page of a privileged asset: pending, with its instance.</summary>
    public static async Task<(Guid Asset, Guid Report, Guid Instance)> PrivilegedLossAsync(string persona = "a-logistics-maker")
    {
        var asset = await AcS2s42TrustBoundaryTests.InsertAssetAsync("حواسيب", privileged: true);
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync(persona);
        var page = await PostFormAsync(http, AssetPage(asset), "MarkLost", new Dictionary<string, string>
        {
            ["reason"] = "Theft",
            ["context"] = "InStorage",
            ["notes"] = "PLAN-45",
            ["evidenceRef"] = Proof
        });
        var (report, instance) = await LossOfAsync(asset);
        Assert.True(instance != Guid.Empty, $"The report has no instance: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        Assert.Equal(Pending, await LossStatusAsync(report));
        return (asset, report, instance);
    }

    /// <summary>The whole chain of a handover: returns what the checks read.</summary>
    public static async Task<(Guid Asset, Guid Assignment, Guid Instance)> AssignThroughBothStepsAsync(EvidenceRecorder recorder)
    {
        var (asset, assignment, instance) = await PrivilegedHandoverAsync();
        await ApproveFromInboxAsync("a-logistics-checker", instance, recorder);
        Assert.Equal("2", await ScalarAsync("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));
        await ApproveFromInboxAsync("a-management-approver", instance, recorder);
        Assert.Equal("Completed", await InstanceStatusAsync(instance));
        Assert.Equal(Active, await PollAsync(() => AssignmentStatusAsync(assignment), Active));
        Assert.Equal(AssetInUse, await AssetStatusAsync(asset));
        return (asset, assignment, instance);
    }

    /// <summary>The whole chain of a loss.</summary>
    public static async Task<(Guid Asset, Guid Report, Guid Instance)> LossThroughBothStepsAsync(EvidenceRecorder recorder)
    {
        var (asset, report, instance) = await PrivilegedLossAsync();
        await ApproveFromInboxAsync("a-logistics-checker", instance, recorder);
        await ApproveFromInboxAsync("a-management-approver", instance, recorder);
        Assert.Equal("Completed", await InstanceStatusAsync(instance));
        Assert.Equal(LossConfirmed, await PollAsync(() => LossStatusAsync(report), LossConfirmed));
        Assert.Equal(AssetLost, await AssetStatusAsync(asset));
        return (asset, report, instance);
    }

    /// <summary>What a test harness can do and a person cannot: an outcome on the bus, consumed for certain before this returns.</summary>
    public static async Task PublishApprovedAsync(Guid assignment, Guid asset, Guid employee, Guid instance)
    {
        var receipts = await CountAsync("admin_db", "SELECT count(*) FROM \"IntegrationMessageReceipts\";");
        var (bus, provider) = await AcceptanceMessagingHelper.ConnectBusAsync();
        try
        {
            var messageId = Guid.NewGuid();
            await provider.GetRequiredService<IPublishEndpoint>().Publish<IAssetAssignmentApproved>(new
            {
                AssignmentId = assignment,
                AssetId = asset,
                EmployeeId = employee,
                OccurredAt = DateTime.UtcNow,
                SourceSystem = "AC-WF-45",
                WorkflowInstanceId = instance,
                SchemaVersion = 2
            }, ctx =>
            {
                ctx.MessageId = messageId;
                ctx.Headers.Set(TenantIdMiddleware.HeaderName, AcceptanceSeedIds.AgencyAId.ToString("D"));
            });
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }

        Assert.Equal(receipts + 1, await PollAsync(
            () => CountAsync("admin_db", "SELECT count(*) FROM \"IntegrationMessageReceipts\";"), receipts + 1));
    }
}

/// <summary>
/// AC-WF-45 (PLAN-45) on the prod-like stack: a privileged handover and a loss through the D-WF-2 chain, the start that survives HR.Core
/// being down, the withdrawal, and who sees the approval. Isolated: AC-WF-45b stops <c>hr-core-web</c>.
/// </summary>
[RequiredAcceptance]
public class AcWf45AssetApprovalLoopTests
{
    private const string Ac = "AC-WF-45";
    private const string Reason = "لم يعد التسليم مطلوباً لهذا الموظف";

    private static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(120);

    private static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    private static Task<long> PendingWithoutInstanceAsync() => CustodyJourney.CountAsync(
        "admin_db",
        "SELECT (SELECT count(*) FROM \"AssetAssignments\" WHERE \"Status\" = 0 AND \"WorkflowInstanceId\" IS NULL) + (SELECT count(*) FROM \"AssetLossReports\" WHERE \"Status\" = 0 AND \"WorkflowInstanceId\" IS NULL);");

    [SkippableFact]
    public async Task Ac45a_A_privileged_handover_goes_through_both_steps_from_the_inbox_and_becomes_active()
    {
        var r = Recorder("45a", "The maker hands over, the checker and the manager approve from the inbox", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);

        var (asset, assignment, instance) = await CustodyJourney.AssignThroughBothStepsAsync(r);

        var ledger = await CustodyJourney.CountAsync(
            "hr_core_db", $"SELECT count(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{instance:D}' AND \"EntityType\" LIKE '%AssetAssignmentApproved%';");
        Assert.Equal(1, ledger);
        Assert.Equal(0, await CustodyJourney.CountAsync("hr_core_db", $"SELECT count(*) FROM \"OutboxMessage\" WHERE \"Body\" LIKE '%{instance:D}%';"));

        r.WriteScenarioDbChecks(new { asset, assignment, instance, ledger, status = "Active", assetStatus = "InUse" });
        r.WriteScenarioSteps(
            "1. a-logistics-maker hands over a privileged asset through its page: PendingApproval with an instance.\n" +
            "2. The request is in a-logistics-checker's inbox; the checker approves on the generic page.\n" +
            "3. It is in a-management-approver's inbox; the manager approves on the generic page.\n" +
            "4. The assignment is Active and the asset InUse within 30 s; the ledger row exists and no outbox row is left for the instance (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac45b_With_hr_core_down_the_request_waits_holding_the_asset_and_the_job_starts_it()
    {
        var r = Recorder("45b", "hr-core-web stopped: the request waits; after the restart the job links it", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);
        await AcS2s42TrustBoundaryTests.StackReadyAsync();

        var asset = await AcS2s42TrustBoundaryTests.InsertAssetAsync("حواسيب", privileged: true);
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync("a-logistics-maker");
        Guid assignment;
        string page;
        await AcS2s42TrustBoundaryTests.StopAsync("hr-core-web");
        try
        {
            (_, page) = await AcS2s42TrustBoundaryTests.AssignAsync(http, asset, AcceptanceSeedIds.EmpBr1Id, CustodyJourney.Proof);
            (assignment, var noInstance) = await CustodyJourney.AssignmentOfAsync(asset);
            Assert.Equal(Guid.Empty, noInstance);
            Assert.Equal(CustodyJourney.Starting, await CustodyJourney.AssignmentStatusAsync(assignment));
            Assert.Contains(CustodyJourney.StartingLabel, page, StringComparison.Ordinal);
            Assert.Equal(0, await PendingWithoutInstanceAsync());

            // The asset stays held: a second handover is refused and leaves one open row.
            await AcS2s42TrustBoundaryTests.AssignAsync(http, asset, AcceptanceSeedIds.EmpBr1Id, CustodyJourney.Proof);
            Assert.Equal(1, await CustodyJourney.CountAsync("admin_db", $"SELECT count(*) FROM \"AssetAssignments\" WHERE \"AssetId\"='{asset:D}';"));
        }
        finally
        {
            await AcS2s42TrustBoundaryTests.StartAsync("hr-core-web");
        }

        await AcS2s42TrustBoundaryTests.StackReadyAsync();
        var status = await CustodyJourney.PollAsync(() => CustodyJourney.AssignmentStatusAsync(assignment), CustodyJourney.Pending, RestartWindow);
        Assert.Equal(CustodyJourney.Pending, status);
        var (_, instance) = await CustodyJourney.AssignmentOfAsync(asset);
        Assert.NotEqual(Guid.Empty, instance);
        Assert.Equal("InProgress", await CustodyJourney.InstanceStatusAsync(instance));
        Assert.Equal(1, await CustodyJourney.CountAsync(
            "hr_core_db", $"SELECT count(*) FROM \"WorkflowInstances\" WHERE \"IdempotencyKey\"='AssetAssignment:{assignment:D}';"));
        Assert.Equal(0, await PendingWithoutInstanceAsync());

        r.WriteScenarioDbChecks(new { asset, assignment, instance, pendingWithoutInstance = 0 });
        r.WriteScenarioSteps(
            "1. hr-core-web is stopped. a-logistics-maker hands over a privileged asset: ApprovalStarting with no instance, and the page says so in Arabic.\n" +
            "2. Negative: a second handover of the asset leaves one row. No pending row has no instance (psql).\n" +
            "3. hr-core-web starts. Within 2 minutes the job links one instance by the same key: PendingApproval (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac45c_The_requester_withdraws_the_asset_is_free_and_a_late_outcome_changes_nothing()
    {
        var r = Recorder("45c", "A withdrawal cancels the instance and frees the asset; a late outcome has no effect", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);

        var (asset, assignment, instance) = await CustodyJourney.PrivilegedHandoverAsync();
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync("a-logistics-maker");

        var tooShort = await CustodyJourney.PostFormAsync(http, CustodyJourney.AssetPage(asset), "WithdrawApproval", new Dictionary<string, string> { ["reason"] = "قصير" });
        Assert.Equal(CustodyJourney.Pending, await CustodyJourney.AssignmentStatusAsync(assignment));
        Assert.Contains("سبب السحب مطلوب", tooShort, StringComparison.Ordinal);

        await CustodyJourney.PostFormAsync(http, CustodyJourney.AssetPage(asset), "WithdrawApproval", new Dictionary<string, string> { ["reason"] = Reason });

        Assert.Equal(CustodyJourney.Withdrawn, await CustodyJourney.AssignmentStatusAsync(assignment));
        Assert.Equal("Cancelled", await CustodyJourney.InstanceStatusAsync(instance));
        Assert.Equal(1, await CustodyJourney.CountAsync(
            "admin_db", $"SELECT count(*) FROM \"AssetAssignments\" WHERE \"Id\"='{assignment:D}' AND \"ReturnedAt\" IS NOT NULL AND \"WithdrawnAt\" IS NOT NULL AND \"WithdrawnBy\" IS NOT NULL AND \"WithdrawnReason\"='{Reason}';"));

        await CustodyJourney.PublishApprovedAsync(assignment, asset, AcceptanceSeedIds.EmpBr1Id, instance);
        Assert.Equal(CustodyJourney.Withdrawn, await CustodyJourney.AssignmentStatusAsync(assignment));
        Assert.Equal(CustodyJourney.AssetInStorage, await CustodyJourney.AssetStatusAsync(asset));

        var (_, second) = await AcS2s42TrustBoundaryTests.AssignAsync(http, asset, AcceptanceSeedIds.EmpBr2Id, CustodyJourney.Proof);
        var (nextAssignment, nextInstance) = await CustodyJourney.AssignmentOfAsync(asset);
        Assert.True(nextInstance != Guid.Empty, AcS2s42TrustBoundaryTests.Alerts(second));
        Assert.NotEqual(assignment, nextAssignment);

        r.WriteScenarioDbChecks(new { asset, assignment, instance, nextAssignment, nextInstance });
        r.WriteScenarioSteps(
            "1. a-logistics-maker hands over a privileged asset. Negative: a reason under 10 characters changes nothing.\n" +
            "2. The maker withdraws with a reason: Withdrawn, who, when and why are recorded, the instance is Cancelled (psql).\n" +
            "3. Negative: an approved outcome for that instance, published and consumed afterwards, changes nothing (psql).\n" +
            "4. The asset goes to another employee: a new assignment with its own instance.");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac45d_A_loss_goes_through_both_steps_and_hr_core_is_refused_on_the_first()
    {
        var r = Recorder("45d", "A privileged loss: the chain, and the role refused", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);

        var (asset, report, instance) = await CustodyJourney.PrivilegedLossAsync();

        using var central = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("central-a"), Guid.NewGuid().ToString("D"));
        using var refused = await central.PostAsync(
            $"api/hr-core/api/workflow-instances/{instance:D}/approve",
            new StringContent("""{"comments":"AC-WF-45d","targetStepLevel":1}""", System.Text.Encoding.UTF8, "application/json"));
        var refusedBody = await refused.Content.ReadAsStringAsync();
        r.CaptureHttp("hr-core-step1-negative", "POST", $"/api/hr-core/api/workflow-instances/{instance:D}/approve", (int)refused.StatusCode, refusedBody);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(HrManagementRoles.AdminLogistics, refusedBody, StringComparison.Ordinal);

        await CustodyJourney.ApproveFromInboxAsync("a-logistics-checker", instance, r);
        await CustodyJourney.ApproveFromInboxAsync("a-management-approver", instance, r);

        Assert.Equal(CustodyJourney.LossConfirmed, await CustodyJourney.PollAsync(() => CustodyJourney.LossStatusAsync(report), CustodyJourney.LossConfirmed));
        Assert.Equal(CustodyJourney.AssetLost, await CustodyJourney.AssetStatusAsync(asset));

        r.WriteScenarioDbChecks(new { asset, report, instance, assetStatus = "Lost" });
        r.WriteScenarioSteps(
            "1. a-logistics-maker reports a privileged asset lost: PendingApproval with an instance.\n" +
            "2. Negative: central-a (hr-core) approves step 1: refused by the role.\n" +
            "3. a-logistics-checker, then a-management-approver, approve on the generic page: the report is Confirmed and the asset Lost (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac45e_The_requester_is_refused_and_the_officer_cannot_take_the_second_step()
    {
        var r = Recorder("45e", "The requester and the wrong role, on the URL the page renders", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);

        var (_, _, instance) = await CustodyJourney.PrivilegedHandoverAsync();
        var page = CustodyJourney.ApprovalPage(instance);
        using var checker = await AcS2s42TrustBoundaryTests.UiClientAsync("a-logistics-checker");
        var url = await CustodyJourney.ApproveUrlAsync(checker, instance);

        using var requester = await AcS2s42TrustBoundaryTests.UiClientAsync("a-logistics-maker");
        Assert.False(await CustodyJourney.HasFormAsync(requester, page, CustodyJourney.ApproveAction));
        var (status, after) = await CustodyJourney.PostWithoutFormAsync(requester, page, url);
        r.CaptureHttp("requester-step1-negative", "POST", $"/{url}", (int)status);
        Assert.Contains(CustodyJourney.RequesterRefusal, after, StringComparison.Ordinal);
        Assert.Equal("1", await CustodyJourney.ScalarAsync("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));

        await CustodyJourney.PostFormAsync(checker, page, CustodyJourney.ApproveAction);
        Assert.Equal("2", await CustodyJourney.ScalarAsync("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));

        Assert.False(await CustodyJourney.HasFormAsync(checker, page, CustodyJourney.ApproveAction));
        var (_, secondStep) = await CustodyJourney.OpenAsync(checker, page);
        Assert.Contains(HrManagementRoles.HrManagementApprover, secondStep, StringComparison.Ordinal);
        Assert.Equal("InProgress", await CustodyJourney.InstanceStatusAsync(instance));

        r.WriteScenarioDbChecks(new { instance, stepAfterRequester = 1, stepAfterChecker = 2 });
        r.WriteScenarioSteps(
            "1. Negative: the requester sees no approve form, and a POST to the URL the checker's page renders is refused with the requester message.\n" +
            "2. a-logistics-checker approves step 1.\n" +
            "3. Negative: the checker sees no form on step 2, and the page names the role it needs.");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac45f_An_outcome_naming_an_unlinked_instance_changes_nothing_and_is_logged()
    {
        var r = Recorder("45f", "An approved v2 event with an instance the assignment is not linked to", "harness");
        AcceptanceGate.RequireEnabled(r);

        var (asset, assignment, _) = await CustodyJourney.PrivilegedHandoverAsync();
        var stranger = Guid.NewGuid();
        var since = DateTime.UtcNow.AddSeconds(-5).ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        await CustodyJourney.PublishApprovedAsync(assignment, asset, AcceptanceSeedIds.EmpBr1Id, stranger);

        Assert.Equal(CustodyJourney.Pending, await CustodyJourney.AssignmentStatusAsync(assignment));
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("admin-logistics-web");
        var (_, logs) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "logs", "--since", since, container);
        Assert.Contains(stranger.ToString("D"), logs, StringComparison.OrdinalIgnoreCase);

        r.WriteScenarioDbChecks(new { assignment, stranger, status = "PendingApproval" });
        r.WriteScenarioSteps("Negative: an approved event, v2, naming an instance the assignment is not linked to: the assignment stays pending and the service logs a warning with that id.");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac45g_The_link_on_the_asset_page_opens_the_approval_for_its_people_only()
    {
        var r = Recorder("45g", "The link of the asset page; another agency and a role outside the definition", "a-logistics-checker");
        AcceptanceGate.RequireEnabled(r);

        var (asset, _, instance) = await CustodyJourney.PrivilegedHandoverAsync();
        using var checker = await AcS2s42TrustBoundaryTests.UiClientAsync("a-logistics-checker");

        var (assetStatus, assetHtml) = await CustodyJourney.OpenAsync(checker, CustodyJourney.AssetPage(asset));
        Assert.Equal(HttpStatusCode.OK, assetStatus);
        var link = Regex.Match(assetHtml, @"href=""/(ui/hr-core/WorkflowApprovals/Details/[^""]+)""[^>]*>\s*عرض الاعتماد");
        Assert.True(link.Success, "The asset page has no link to the approval.");
        Assert.Equal(CustodyJourney.ApprovalPage(instance), link.Groups[1].Value, ignoreCase: true);

        var (opened, html) = await CustodyJourney.OpenAsync(checker, link.Groups[1].Value);
        r.CaptureHttp("open-approval", "GET", $"/{link.Groups[1].Value}", (int)opened);
        Assert.Equal(HttpStatusCode.OK, opened);
        Assert.Contains("تسليم عهدة", html, StringComparison.Ordinal);

        using var otherAgency = await AcS2s42TrustBoundaryTests.UiClientAsync("b-logistics");
        var (agencyStatus, _) = await CustodyJourney.OpenAsync(otherAgency, link.Groups[1].Value);
        r.CaptureHttp("other-agency-negative", "GET", $"/{link.Groups[1].Value}", (int)agencyStatus);
        Assert.True(agencyStatus is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"b-logistics: {(int)agencyStatus}");

        using var hrCore = await AcS2s42TrustBoundaryTests.UiClientAsync("central-a2");
        var (roleStatus, _) = await CustodyJourney.OpenAsync(hrCore, link.Groups[1].Value);
        r.CaptureHttp("no-step-role-negative", "GET", $"/{link.Groups[1].Value}", (int)roleStatus);
        Assert.Equal(HttpStatusCode.Forbidden, roleStatus);

        var (employees, _) = await CustodyJourney.OpenAsync(checker, "ui/hr-core/Employees");
        r.CaptureHttp("rest-of-hr-core-negative", "GET", "/ui/hr-core/Employees", (int)employees);
        Assert.Equal(HttpStatusCode.Forbidden, employees);

        r.WriteScenarioDbChecks(new { asset, instance, opened = (int)opened, otherAgency = (int)agencyStatus, noStepRole = (int)roleStatus, employees = (int)employees });
        r.WriteScenarioSteps(
            "1. a-logistics-checker follows the link on the asset page: the generic page, 200.\n" +
            "2. Negative: b-logistics of another agency: 404 or 403. central-a2, with no role in the definition: 403.\n" +
            "3. Negative: the checker on /ui/hr-core/Employees: 403.");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac45h_The_journeys_of_AC_11_and_AC_33_pass_with_the_production_settings()
    {
        var r = Recorder("45h", "The custody and loss journeys of AC-11 and AC-33, on the prod-like stack", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);

        var custody = await CustodyJourney.AssignThroughBothStepsAsync(r);
        var loss = await CustodyJourney.LossThroughBothStepsAsync(r);

        Assert.Equal(CustodyJourney.Active, await CustodyJourney.AssignmentStatusAsync(custody.Assignment));
        Assert.Equal(CustodyJourney.AssetLost, await CustodyJourney.AssetStatusAsync(loss.Asset));

        r.WriteScenarioDbChecks(new { custody.Assignment, custodyInstance = custody.Instance, loss.Report, lossInstance = loss.Instance });
        r.WriteScenarioSteps("AC-11's handover and AC-33's loss, the same journeys through the same pages, with the production settings: Active and Lost.");
        r.WriteResult("Pass");
    }
}
