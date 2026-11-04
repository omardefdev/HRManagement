using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-40 (AC-MSG-40a..h) on the prod-like acceptance stack (<c>hrm-acceptance</c> with
/// docker-compose.acceptance-prodlike.yml): every publish writes its <c>OutboxMessage</c> row in the transaction that
/// commits the state, consumers publish through the consumer outbox, and the replay tool repairs what was lost before.
/// Setup rows are inserted with SQL where the UI path to them is not the subject (an approved promotion, an injected
/// "before the fix" row); the action under test always goes through the gateway. Outbox rows are deleted once delivered,
/// so "a row was written" is read from the <c>OutboxMessage</c> sequence (it never goes back).
/// </summary>
[RequiredAcceptance]
[Collection(AcMsg40Collection.Name)]
public class AcMsg40AtomicPublishingTests
{
    private const string Ac = "AC-MSG-40";
    private static readonly Guid TenantA = AcceptanceSeedIds.AgencyAId;
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(60);

    // ------------------------------------------------------------------------------------------------ helpers

    private static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    private static Task<string> SqlTextAsync(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    private static Task<long> SqlScalarAsync(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(database, sql);

    /// <summary>The last <c>OutboxMessage</c> sequence value of a database (0 before the first row).</summary>
    internal static Task<long> OutboxSequenceAsync(string database) =>
        SqlScalarAsync(database,
            "SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM \"OutboxMessage_SequenceNumber_seq\";");

    internal static async Task<long> PollAsync(Func<Task<long>> read, Func<long, bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long value;
        do
        {
            value = await read();
            if (done(value))
                return value;
            await Task.Delay(TimeSpan.FromSeconds(2));
        } while (DateTime.UtcNow < deadline);

        return value;
    }

    internal static string RequireAntiforgeryToken(string html)
    {
        var match = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "No antiforgery token on the page.");
        return WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
    }

    private static async Task<HttpResponseMessage> PostUiFormAsync(
        HttpClient http, string pagePath, string actionPath, IDictionary<string, string>? fields = null)
    {
        var page = await http.GetAsync(pagePath);
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {pagePath} -> {(int)page.StatusCode}");
        var form = new Dictionary<string, string>(fields ?? new Dictionary<string, string>())
        {
            ["__RequestVerificationToken"] = RequireAntiforgeryToken(html)
        };
        return await http.PostAsync(actionPath, new FormUrlEncodedContent(form));
    }

    private static async Task<Guid> OpenEmployeeAsync()
    {
        var text = await SqlTextAsync("hr_core_db",
            $"SELECT \"EmployeeId\" FROM \"EmploymentRecords\" WHERE \"EndDate\" IS NULL AND \"TenantId\" = '{TenantA:D}' " +
            "ORDER BY \"EmployeeId\" LIMIT 1;");
        Assert.True(Guid.TryParse(text, out var id), $"No employee with an open employment record in agency A: {text}");
        return id;
    }

    /// <summary>How often <paramref name="marker"/> occurs in the employee's open employment record notes.</summary>
    private static Task<long> NoteOccurrencesAsync(Guid employeeId, string marker) =>
        SqlScalarAsync("hr_core_db",
            "SELECT COALESCE(SUM((length(coalesce(\"Notes\",'')) - length(replace(coalesce(\"Notes\",''), " +
            $"'{marker}', ''))) / length('{marker}')), 0) FROM \"EmploymentRecords\" WHERE \"EmployeeId\" = '{employeeId:D}';");

    private static Task<long> ReceiptsAsync(string database, Guid messageId) =>
        SqlScalarAsync(database,
            $"SELECT COUNT(*) FROM \"IntegrationMessageReceipts\" WHERE \"MessageId\" = '{messageId:D}';");

    private static Task<long> ReceiptsForConsumerAsync(string database, string consumerSuffix) =>
        SqlScalarAsync(database,
            $"SELECT COUNT(*) FROM \"IntegrationMessageReceipts\" WHERE \"ConsumerType\" LIKE '%{consumerSuffix}';");

    /// <summary>Publishes <paramref name="values"/> again with <paramref name="messageId"/> (the AC-16 duplicate tool).</summary>
    private static async Task RedeliverAsync<T>(object values, Guid messageId) where T : class
    {
        var (bus, provider) = await AcceptanceMessagingHelper.ConnectBusAsync();
        try
        {
            var publish = provider.GetRequiredService<IPublishEndpoint>();
            await publish.Publish<T>(values, ctx =>
            {
                ctx.MessageId = messageId;
                ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantA.ToString("D"));
            });
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------------------------------------ 40a

    [SkippableFact]
    public async Task Ac40a_promotion_execution_writes_its_outbox_row_and_reaches_hr_core_once()
    {
        var r = Recorder("40a", "Promotion executed: IPromotionExecuted in the same save, delivered once to HR.Core", "a-movements");
        AcceptanceGate.RequireEnabled(r);
        var persona = AcceptanceHarness.RequirePersona("a-movements");

        var employeeId = await OpenEmployeeAsync();
        var promotionId = Guid.NewGuid();
        var decisionRef = $"MSG40A-{promotionId:N}"[..20];
        await SqlTextAsync("hr_movements_db", $"""
            INSERT INTO "PromotionRequests" ("Id","EmployeeId","SourceLegalRegimeId","FromGrade","ToGrade","FromStep","ToStep",
              "TransitionDecisionRef","EffectiveDate","Status","IsCancelled","TenantId","CreatedAt","UpdatedAt")
            VALUES ('{promotionId:D}','{employeeId:D}',NULL,5,4,3,1,'{decisionRef}',CURRENT_DATE - 1,1,false,'{TenantA:D}',now(),now());
            INSERT INTO "MovementChecklists" ("Id","MovementKind","CorrelationId","Step","IsCompleted","IsRequired","CompletedAt",
              "EvidenceRef","SlaHours","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),3,'{promotionId:D}',0,true,true,now(),'msg40a:recorded',24,'{TenantA:D}',now(),now()),
                   (gen_random_uuid(),3,'{promotionId:D}',1,true,true,now(),'msg40a:approved',24,'{TenantA:D}',now(),now());
            SELECT 1;
            """);

        var messageId = DeterministicMessageId.For<IPromotionExecuted>(promotionId);
        var sequenceBefore = await OutboxSequenceAsync("hr_movements_db");
        var consumerReceiptsBefore = await ReceiptsForConsumerAsync("hr_core_db", "promotion-executed");
        r.WriteBefore(new { promotionId, employeeId, decisionRef, messageId, sequenceBefore, consumerReceiptsBefore });

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, Guid.NewGuid().ToString("D"), allowAutoRedirect: false);
        var execute = await PostUiFormAsync(http,
            $"ui/hr-movements/PromotionRequests/Details/{promotionId:D}",
            $"ui/hr-movements/PromotionRequests/Execute/{promotionId:D}");
        r.CaptureHttp("execute", "POST", $"/ui/hr-movements/PromotionRequests/Execute/{promotionId:D}", (int)execute.StatusCode,
            new { location = execute.Headers.Location?.ToString() });

        var status = await SqlScalarAsync("hr_movements_db", $"SELECT \"Status\" FROM \"PromotionRequests\" WHERE \"Id\" = '{promotionId:D}';");
        var sequenceAfter = await OutboxSequenceAsync("hr_movements_db");
        var receipts = await PollAsync(() => ReceiptsAsync("hr_core_db", messageId), n => n >= 1, DeliveryWindow);
        var consumerReceiptsAfter = await ReceiptsForConsumerAsync("hr_core_db", "promotion-executed");
        var notes = await NoteOccurrencesAsync(employeeId, decisionRef);
        var checks = new
        {
            promotionId, messageId, executeStatus = (int)execute.StatusCode, promotionStatus = status,
            outboxRowsWritten = sequenceAfter - sequenceBefore, receipts, consumerReceiptsBefore, consumerReceiptsAfter,
            employmentNotes = notes,
        };
        r.WriteScenarioDbChecks(checks);
        Assert.Equal(HttpStatusCode.Redirect, execute.StatusCode);
        Assert.Equal(2, status);
        Assert.True(sequenceAfter > sequenceBefore, $"No OutboxMessage row was written in hr_movements_db ({sequenceBefore} -> {sequenceAfter}).");
        Assert.Equal(1, receipts);
        Assert.Equal(1, notes);

        // Negative: the same message again (deterministic MessageId) is skipped by HR.Core's inbox.
        await RedeliverAsync<IPromotionExecuted>(new
        {
            PromotionRequestId = promotionId,
            EmployeeId = employeeId,
            SourceLegalRegimeId = (string?)null,
            FromGrade = 5, ToGrade = 4, FromStep = 3, ToStep = 1,
            TransitionDecisionRef = decisionRef,
            EffectiveDate = DateTime.UtcNow.Date.AddDays(-1),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Movements"
        }, messageId);
        await Task.Delay(TimeSpan.FromSeconds(8));
        var receiptsAfterDuplicate = await ReceiptsAsync("hr_core_db", messageId);
        var notesAfterDuplicate = await NoteOccurrencesAsync(employeeId, decisionRef);
        r.WriteAfter(new { receiptsAfterDuplicate, notesAfterDuplicate });
        Assert.Equal(1, receiptsAfterDuplicate);
        Assert.Equal(1, notesAfterDuplicate);

        r.WriteScenarioSteps(
            "1. SQL: an approved promotion (effective yesterday) with its RequestRecorded and Approved checklist steps, for an employee with an open employment record.\n" +
            "2. Gateway UI (a-movements): POST PromotionRequests/Execute -> 302; the request is Executed.\n" +
            "3. hr_movements_db: the OutboxMessage sequence advanced (a row was written with the state).\n" +
            "4. hr_core_db: one inbox receipt for the deterministic MessageId UUIDv5(IPromotionExecuted:<id>:v1) and one note in the employment record within 60 s.\n" +
            "5. Negative: the same message published again with the same MessageId -> still one receipt and one note.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 40b

    private const string AssignmentDefinition = "f0050001-0001-4000-8000-000000000001";

    /// <summary>An asset in storage and a pending assignment in admin_db, and its workflow instance in hr_core_db.</summary>
    private static async Task<(Guid Assignment, Guid Asset, Guid Instance)> InjectAssignmentAsync(Guid employeeId, string workflowStatus, int stepLevel)
    {
        var asset = Guid.NewGuid();
        var assignment = Guid.NewGuid();
        var instance = Guid.NewGuid();
        await SqlTextAsync("admin_db", $"""
            INSERT INTO "Assets" ("Id","TagNumber","Name","Status","TenantId","CreatedAt","UpdatedAt")
            VALUES ('{asset:D}','MSG40-{asset:N}','MSG40 asset',1,'{TenantA:D}',now(),now());
            INSERT INTO "AssetAssignments" ("Id","AssetId","EmployeeId","AssignedAt","Status","TenantId","CreatedAt","UpdatedAt")
            VALUES ('{assignment:D}','{asset:D}','{employeeId:D}',now(),0,'{TenantA:D}',now(),now());
            SELECT 1;
            """);
        var completedAt = workflowStatus == "InProgress" ? "NULL" : "now()";
        await SqlTextAsync("hr_core_db", $$"""
            INSERT INTO "WorkflowInstances" ("Id","TenantId","DefinitionId","DefinitionVersion","EntityType","EntityId","CurrentStepLevel",
              "Status","StepHistoryJson","ContextJson","InitiatedBy","StartedAt","CompletedAt","CreatedAt","UpdatedAt")
            VALUES ('{{instance:D}}','{{TenantA:D}}','{{AssignmentDefinition}}',1,'AssetAssignment','{{assignment:D}}',{{stepLevel}},
              '{{workflowStatus}}','[]','{"assetId":"{{asset:D}}","employeeId":"{{employeeId:D}}"}','{{Guid.NewGuid():D}}',now(),{{completedAt}},now(),now());
            SELECT 1;
            """);
        return (assignment, asset, instance);
    }

    private static Task<long> AssignmentStatusAsync(Guid assignment) =>
        SqlScalarAsync("admin_db", $"SELECT \"Status\" FROM \"AssetAssignments\" WHERE \"Id\" = '{assignment:D}';");

    private static Task<long> AssetStatusAsync(Guid asset) =>
        SqlScalarAsync("admin_db", $"SELECT \"Status\" FROM \"Assets\" WHERE \"Id\" = '{asset:D}';");

    private static async Task<HttpResponseMessage> WorkflowActionAsync(string personaId, Guid instance, string action, int level)
    {
        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona(personaId), Guid.NewGuid().ToString("D"));
        return await http.PostAsJsonAsync(
            $"api/hr-core/api/workflow-instances/{instance:D}/{action}",
            new { Comments = "AC-MSG-40b", TargetStepLevel = level });
    }

    [SkippableFact]
    public async Task Ac40b_custody_approval_and_rejection_reach_admin_logistics()
    {
        var r = Recorder("40b", "Asset assignment approved or rejected: the outcome is committed with the instance and reaches Admin.Logistics", "central-a, a-management-approver");
        AcceptanceGate.RequireEnabled(r);
        var employeeId = await OpenEmployeeAsync();
        const int active = 1, inUse = 0;
        const int rejectedStatus = 3;

        var (assignment, asset, instance) = await InjectAssignmentAsync(employeeId, "InProgress", 1);
        var first = await WorkflowActionAsync("central-a", instance, "approve", 1);
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.IsSuccessStatusCode, $"step 1: {(int)first.StatusCode} {firstBody}");
        var last = await WorkflowActionAsync("a-management-approver", instance, "approve", 2);
        var lastBody = await last.Content.ReadAsStringAsync();
        Assert.True(last.IsSuccessStatusCode, $"step 2: {(int)last.StatusCode} {lastBody}");
        var assignmentStatus = await PollAsync(() => AssignmentStatusAsync(assignment), s => s == active, DeliveryWindow);
        var assetStatus = await AssetStatusAsync(asset);
        Assert.Equal(active, assignmentStatus);
        Assert.Equal(inUse, assetStatus);

        var (rejected, rejectedAsset, rejectedInstance) = await InjectAssignmentAsync(employeeId, "InProgress", 1);
        var reject = await WorkflowActionAsync("central-a", rejectedInstance, "reject", 1);
        Assert.True(reject.IsSuccessStatusCode, $"reject: {(int)reject.StatusCode} {await reject.Content.ReadAsStringAsync()}");
        var rejectedAssignment = await PollAsync(() => AssignmentStatusAsync(rejected), s => s == rejectedStatus, DeliveryWindow);
        var rejectedAssetStatus = await AssetStatusAsync(rejectedAsset);
        Assert.Equal(rejectedStatus, rejectedAssignment);
        Assert.NotEqual(inUse, rejectedAssetStatus);

        r.WriteScenarioDbChecks(new { assignment, assignmentStatus, assetStatus, rejected, rejectedAssignment, rejectedAssetStatus });
        r.WriteScenarioSteps(
            "1. SQL: an asset in storage, a PendingApproval assignment and its ASSET-ASSIGN-APPROVAL instance.\n" +
            "2. Gateway API: central-a approves step 1, a-management-approver step 2 -> within 60 s the assignment is Active and the asset InUse.\n" +
            "3. Negative: a second assignment rejected at step 1 -> Rejected, the asset is not InUse.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 40c

    private static async Task<(Guid Template, Guid Version)> InjectDraftPolicyAsync(string domain, Guid? createdBy)
    {
        var template = Guid.NewGuid();
        var version = Guid.NewGuid();
        var maker = createdBy is null ? "NULL" : $"'{createdBy:D}'";
        await SqlTextAsync("hr_core_db", $"""
            INSERT INTO "PolicyTemplates" ("Id","Domain","Name","Scope","SubjectType","TenantId","CreatedAt","UpdatedAt")
            VALUES ('{template:D}','{domain}','MSG40 policy','TenantOverride','Employee','{TenantA:D}',now(),now());
            INSERT INTO "PolicyTemplateVersions" ("Id","PolicyTemplateId","VersionNumber","Status","TenantId","CreatedAt","UpdatedAt","CreatedBy")
            VALUES ('{version:D}','{template:D}',1,'Draft','{TenantA:D}',now(),now(),{maker});
            SELECT 1;
            """);
        return (template, version);
    }

    [SkippableFact]
    public async Task Ac40c_policy_published_from_the_api_and_the_ui_reaches_idcard()
    {
        var r = Recorder("40c", "IPolicyPublished from the API and the UI, committed with the publication; four-eyes refusal publishes nothing", "central-a");
        AcceptanceGate.RequireEnabled(r);
        var persona = AcceptanceHarness.RequirePersona("central-a");
        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, Guid.NewGuid().ToString("D"), allowAutoRedirect: false);
        var domain = $"msg40-{Guid.NewGuid():N}"[..20];

        var (_, apiVersion) = await InjectDraftPolicyAsync(domain, Guid.NewGuid());
        var api = await http.PostAsJsonAsync($"api/hr-core/api/policies/templates/{apiVersion:D}/publish", new { EvidenceRef = "AC-MSG-40c" });
        Assert.True(api.IsSuccessStatusCode, $"API publish: {(int)api.StatusCode} {await api.Content.ReadAsStringAsync()}");

        var (uiTemplate, uiVersion) = await InjectDraftPolicyAsync(domain + "u", Guid.NewGuid());
        var ui = await PostUiFormAsync(http,
            $"ui/hr-core/PolicyTemplates/Details/{uiTemplate:D}",
            "ui/hr-core/PolicyTemplates/PublishFromUi",
            new Dictionary<string, string> { ["versionId"] = uiVersion.ToString("D"), ["evidenceRef"] = "AC-MSG-40c" });
        Assert.Equal(HttpStatusCode.Redirect, ui.StatusCode);

        var apiReceipts = await PollAsync(() => ReceiptsAsync("idcard_db", DeterministicMessageId.For<IPolicyPublished>(apiVersion)), n => n >= 1, DeliveryWindow);
        var uiReceipts = await PollAsync(() => ReceiptsAsync("idcard_db", DeterministicMessageId.For<IPolicyPublished>(uiVersion)), n => n >= 1, DeliveryWindow);
        Assert.Equal(1, apiReceipts);
        Assert.Equal(1, uiReceipts);

        // Negative: no maker on record means the checker is the maker; four-eyes refuses and nothing is published.
        var (_, refusedVersion) = await InjectDraftPolicyAsync(domain + "n", null);
        var sequenceBefore = await OutboxSequenceAsync("hr_core_db");
        var refused = await http.PostAsJsonAsync($"api/hr-core/api/policies/templates/{refusedVersion:D}/publish", new { EvidenceRef = "AC-MSG-40c" });
        var sequenceAfter = await OutboxSequenceAsync("hr_core_db");
        await Task.Delay(TimeSpan.FromSeconds(5));
        var refusedReceipts = await ReceiptsAsync("idcard_db", DeterministicMessageId.For<IPolicyPublished>(refusedVersion));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, refusedReceipts);

        r.WriteScenarioDbChecks(new { apiVersion, apiReceipts, uiVersion, uiReceipts, refusedVersion, refusedStatus = (int)refused.StatusCode, refusedReceipts, sequenceBefore, sequenceAfter });
        r.WriteScenarioSteps(
            "1. SQL: draft policy versions with a recorded maker.\n" +
            "2. POST policies/templates/{id}/publish (API) and PolicyTemplates/PublishFromUi (UI, which published nothing before PLAN-40).\n" +
            "3. idcard_db: one receipt for each deterministic MessageId within 60 s.\n" +
            "4. Negative: a version with no maker is refused by four-eyes (400): no receipt.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 40d

    [SkippableFact]
    public async Task Ac40d_leave_approved_while_rabbitmq_is_down_is_delivered_once_after_it_returns()
    {
        var r = Recorder("40d", "Leave approved with the broker stopped: state and outbox row commit, one delivery after restart", "a-contracts-maker, a-contracts-checker");
        AcceptanceGate.RequireEnabled(r);
        var used = await SqlTextAsync("hr_contracts_db", """SELECT COALESCE(string_agg(DISTINCT "EmployeeId"::text, ','), '') FROM "LeaveRequests";""");
        var exclude = string.Join(",", used.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => $"'{id}'").DefaultIfEmpty("'00000000-0000-0000-0000-000000000000'"));
        var employee = await SqlTextAsync("hr_contracts_db",
            $"SELECT \"EmployeeId\" FROM \"LeaveBalances\" WHERE \"TenantId\"='{TenantA:D}' AND \"LeaveType\"='Annual' AND \"Year\"={DateTime.UtcNow.Year} " +
            $"AND \"TotalEntitlement\" - \"Used\" >= 5 AND \"EmployeeId\" NOT IN ({exclude}) ORDER BY \"EmployeeId\" LIMIT 1;");
        AcceptanceGate.RequireData(r, Guid.TryParse(employee, out var employeeId), "لا موظف برصيد بلا طلبات", "No agency-A employee with a free balance left (scripts/acceptance-reset.ps1).");

        var day = DateTime.UtcNow.Date.AddDays(20);
        if (day.Year != DateTime.UtcNow.Year) day = new DateTime(DateTime.UtcNow.Year, 12, 20);
        using var maker = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("a-contracts-maker"), Guid.NewGuid().ToString("D"), allowAutoRedirect: false);
        var created = await PostUiFormAsync(maker, "ui/hr-contracts/LeaveRequests/Create", "ui/hr-contracts/LeaveRequests/Create", new Dictionary<string, string>
        {
            ["EmployeeId"] = employeeId.ToString("D"), ["LeaveType"] = "Annual", ["StartDate"] = day.ToString("yyyy-MM-dd"),
            ["EndDate"] = day.AddDays(1).ToString("yyyy-MM-dd"), ["DurationDays"] = "2", ["Notes"] = "AC-MSG-40d"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var leave = await SqlTextAsync("hr_contracts_db", $"SELECT \"Id\" FROM \"LeaveRequests\" WHERE \"EmployeeId\"='{employeeId:D}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(leave, out var leaveId), "leave request not created");

        using var checker = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("a-contracts-checker"), Guid.NewGuid().ToString("D"), allowAutoRedirect: false);
        var page = await (await checker.GetAsync("ui/hr-contracts/LeaveRequests")).Content.ReadAsStringAsync();
        var token = RequireAntiforgeryToken(page);

        var receiptsBefore = await ReceiptsForConsumerAsync("archive_db", "leave-approved");
        var rabbit = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("rabbitmq");
        long outboxDuring;
        string statusDuring;
        var (stopExit, stopOutput) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "stop", rabbit);
        Assert.True(stopExit == 0, stopOutput);
        try
        {
            var approve = await checker.PostAsync($"ui/hr-contracts/LeaveRequests/Approve/{leaveId:D}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
            r.CaptureHttp("approve-while-rabbit-down", "POST", $"/ui/hr-contracts/LeaveRequests/Approve/{leaveId:D}", (int)approve.StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
            statusDuring = await SqlTextAsync("hr_contracts_db", $"SELECT \"Status\" FROM \"LeaveRequests\" WHERE \"Id\"='{leaveId:D}';");
            outboxDuring = await SqlScalarAsync("hr_contracts_db", "SELECT COUNT(*) FROM \"OutboxMessage\";");
        }
        finally
        {
            var (startExit, startOutput) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "start", rabbit);
            Assert.True(startExit == 0, startOutput);
        }

        Assert.True(outboxDuring > 0, "The outbox row must be committed while the broker is down.");
        var receiptsAfter = await PollAsync(() => ReceiptsForConsumerAsync("archive_db", "leave-approved"), n => n > receiptsBefore, TimeSpan.FromSeconds(120));
        var outboxAfter = await PollAsync(() => SqlScalarAsync("hr_contracts_db", "SELECT COUNT(*) FROM \"OutboxMessage\";"), n => n == 0, TimeSpan.FromSeconds(120));
        await Task.Delay(TimeSpan.FromSeconds(5));
        var receiptsFinal = await ReceiptsForConsumerAsync("archive_db", "leave-approved");

        Assert.Equal(receiptsBefore + 1, receiptsFinal);
        Assert.Equal(0, outboxAfter);
        r.WriteScenarioDbChecks(new { leaveId, statusDuring, outboxDuring, receiptsBefore, receiptsAfter, receiptsFinal, outboxAfter });
        r.WriteScenarioSteps(
            "1. a-contracts-maker creates a leave through the UI; a-contracts-checker opens it.\n" +
            "2. RabbitMQ stopped; the approval commits the state and the OutboxMessage row.\n" +
            "3. RabbitMQ started: the outbox drains and Records.Archive records exactly one more receipt (no duplicate, no state without its event).\n" +
            "AC-16, AC-17 and AC-18 are rerun live next to this scenario.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 40e

    [SkippableFact]
    public async Task Ac40e_party_acknowledgements_of_admin_logistics_and_auth_directory_go_through_their_outbox()
    {
        var r = Recorder("40e", "Saga party acknowledgements travel through OutboxMessage in the party services", "system (saga command)");
        AcceptanceGate.RequireEnabled(r);
        var employeeId = await OpenEmployeeAsync();
        var record = Guid.NewGuid();
        var adminBefore = await OutboxSequenceAsync("admin_db");
        var authBefore = await OutboxSequenceAsync("auth_directory_db");

        var (bus, provider) = await AcceptanceMessagingHelper.ConnectBusAsync();
        try
        {
            await provider.GetRequiredService<IPublishEndpoint>().Publish<HRManagement.MessageContracts.Commands.IStartSeparationSaga>(new
            {
                SeparationRecordId = record,
                EmployeeId = employeeId,
                SeparationType = "Resignation",
                EffectiveDate = DateTime.UtcNow.Date.AddDays(30),
                ContractId = (Guid?)null,
                CorrelationId = Guid.NewGuid().ToString("D"),
                TenantId = (Guid?)TenantA,
                RemainingLeaveDays = 0
            }, ctx => ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantA.ToString("D")));
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }

        var adminAfter = await PollAsync(() => OutboxSequenceAsync("admin_db"), n => n > adminBefore, DeliveryWindow);
        var authAfter = await PollAsync(() => OutboxSequenceAsync("auth_directory_db"), n => n > authBefore, DeliveryWindow);
        Assert.True(adminAfter > adminBefore, "Admin.Logistics published its acknowledgement without its OutboxMessage table.");
        Assert.True(authAfter > authBefore, "Auth.Directory published its acknowledgement without its OutboxMessage table.");

        r.WriteScenarioDbChecks(new { record, adminBefore, adminAfter, authBefore, authAfter });
        r.WriteScenarioSteps(
            "1. IStartSeparationSaga for an employee: the saga dispatches the parties.\n" +
            "2. admin_db and auth_directory_db: the OutboxMessage sequence advanced, so the acknowledgement was written in the consumer's transaction.\n" +
            "The prod-like hire journey (Category=JourneyProdLike) and AcHcm29 run next to this scenario; a failed commit leaving no acknowledgement is proved in Msg040OutboxOrderingIntegrationTests.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 40f

    private static readonly (string Service, int Port)[] OutboxServices =
    [
        ("hr-core-web", 6210), ("hr-contracts-web", 6220), ("hr-health-web", 6230), ("hr-movements-web", 6240), ("hr-training-web", 6250),
        ("payroll-web", 6260), ("idcard-web", 6270), ("records-archive-web", 6280), ("admin-logistics-web", 6290), ("it-infra-web", 6300),
        ("dev-analytics-web", 6310), ("compliance-web", 6320), ("auth-directory-web", 6330),
    ];

    [SkippableFact]
    public async Task Ac40f_no_unsaved_outbox_message_in_any_of_the_thirteen_services()
    {
        var r = Recorder("40f", "outbox_unsaved_messages_total stays 0 in the 13 services after the acceptance run", "system (metrics)");
        AcceptanceGate.RequireEnabled(r);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var samples = new List<object>();
        foreach (var (service, port) in OutboxServices)
        {
            var text = await http.GetStringAsync($"http://localhost:{port}/metrics");
            var lines = text.Split('\n').Where(l => l.StartsWith("outbox_unsaved_messages_total", StringComparison.Ordinal)).ToList();
            Assert.True(lines.Count > 0, $"{service}: the guard's counter is not exported.");
            var total = lines.Sum(l => double.Parse(l[(l.LastIndexOf('}') + 1)..].Trim().Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture));
            samples.Add(new { service, series = lines.Count, total });
            Assert.True(total == 0, $"{service}: {string.Join(" | ", lines)}");
        }

        r.WriteScenarioDbChecks(new { samples });
        r.WriteScenarioSteps("GET /metrics of the 13 outbox services after the scenarios: the counter exists (zero sample route=none) and sums to 0. " +
                             "The negative case (a publish without a save fails the request) is Msg040UnsavedOutboxGuardIntegrationTests.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 40g

    private static async Task<(int Exit, string Output, JsonDocument? Report)> RunReplayAsync(string token, string reportName, bool apply)
    {
        var path = Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence", Ac, reportName);
        var args = new List<string>
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AcceptanceHarness.RepoRoot, "scripts", "msg040-replay-lost-events.ps1"),
            "-GatewayUrl", "http://localhost:6200", "-AccessToken", token, "-ReportPath", path, apply ? "-Apply" : "-DryRun"
        };
        var (exit, output) = await AcceptanceMessagingHelper.RunProcessAsync("powershell", [.. args]);
        return (exit, output, File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null);
    }

    private static IReadOnlyList<string> ReportIds(JsonDocument report, string kind) =>
        report.RootElement.GetProperty("results").EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == kind)
            .GetProperty("ids").EnumerateArray().Select(i => i.GetString()!).ToList();

    [SkippableFact]
    public async Task Ac40g_events_lost_before_the_fix_are_listed_replayed_once_and_then_gone()
    {
        var r = Recorder("40g", "Replay: dry run lists injected rows, apply produces the effect, second run lists zero", "a-directory-checker (hr-admin)");
        AcceptanceGate.RequireEnabled(r);
        var employeeId = await OpenEmployeeAsync();
        var promotionId = Guid.NewGuid();
        var decisionRef = $"MSG40G-{promotionId:N}"[..20];
        await SqlTextAsync("hr_movements_db", $"""
            INSERT INTO "PromotionRequests" ("Id","EmployeeId","FromGrade","ToGrade","FromStep","ToStep","TransitionDecisionRef","EffectiveDate","Status",
              "IsCancelled","TenantId","CreatedAt","UpdatedAt")
            VALUES ('{promotionId:D}','{employeeId:D}',5,4,3,1,'{decisionRef}',CURRENT_DATE - 1,2,false,'{TenantA:D}',now(),now());
            INSERT INTO "MovementExecutionRecords" ("Id","PromotionRequestId","EmployeeId","ExecutedAt","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{promotionId:D}','{employeeId:D}',now(),'{TenantA:D}',now(),now());
            SELECT 1;
            """);
        var (assignment, asset, instance) = await InjectAssignmentAsync(employeeId, "Completed", 2);

        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(AcceptanceHarness.RequirePersona("a-directory-checker"));
        var (dryExit, dryOutput, dry) = await RunReplayAsync(token, "replay-1-dry-run.json", apply: false);
        Assert.True(dryExit == 0, dryOutput);
        Assert.NotNull(dry);
        Assert.Contains(promotionId.ToString("D"), ReportIds(dry, "promotions"));
        Assert.Contains(instance.ToString("D"), ReportIds(dry, "asset-outcomes"));
        Assert.Equal(0, await NoteOccurrencesAsync(employeeId, decisionRef));
        Assert.Equal(0, await AssignmentStatusAsync(assignment));

        var (applyExit, applyOutput, applied) = await RunReplayAsync(token, "replay-2-apply.json", apply: true);
        Assert.True(applyExit == 0, applyOutput);
        Assert.NotNull(applied);
        var notes = await PollAsync(() => NoteOccurrencesAsync(employeeId, decisionRef), n => n >= 1, DeliveryWindow);
        var assignmentStatus = await PollAsync(() => AssignmentStatusAsync(assignment), s => s == 1, DeliveryWindow);
        Assert.Equal(1, notes);
        Assert.Equal(1, assignmentStatus);
        Assert.Equal(0, await AssetStatusAsync(asset));

        var (secondExit, secondOutput, second) = await RunReplayAsync(token, "replay-3-second-run.json", apply: false);
        Assert.True(secondExit == 0, secondOutput);
        Assert.NotNull(second);
        Assert.Empty(ReportIds(second, "promotions"));
        Assert.Empty(ReportIds(second, "delegations"));
        Assert.Empty(ReportIds(second, "asset-outcomes"));
        var audited = await SqlScalarAsync("hr_movements_db", "SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityType\" = 'MessagingReplayRun';");
        Assert.True(audited >= 1, "The apply run has no audit row.");

        r.WriteScenarioDbChecks(new
        {
            promotionId, instance, assignment, notes, assignmentStatus, audited,
            dryRun = dry.RootElement.GetProperty("lostEvents").GetInt32(),
            applied = applied.RootElement.GetProperty("lostEvents").GetInt32(),
            secondRun = second.RootElement.GetProperty("lostEvents").GetInt32()
        });
        r.WriteScenarioSteps(
            "1. SQL: an executed promotion with its execution record and a completed custody workflow with a pending assignment, none with a ledger row (the state a lost event left).\n" +
            "2. scripts/msg040-replay-lost-events.ps1 -DryRun lists both; nothing changes.\n" +
            "3. -Apply on this acceptance database: the note reaches HR.Core and the assignment becomes Active; the run is audited.\n" +
            "4. Negative: the second dry run lists zero replayable events.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 40h

    private static async Task RestartHrCoreAsync()
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("hr-core-web");
        var (exit, output) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "restart", container);
        Assert.True(exit == 0, output);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if ((await http.GetAsync("http://localhost:6210/health/ready")).IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Still starting.
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.Fail("HR.Core did not become ready after the restart.");
    }

    [SkippableFact]
    public async Task Ac40h_org_unit_added_by_sql_reaches_the_snapshots_after_a_restart_without_duplicates()
    {
        var r = Recorder("40h", "HR.Core start-up re-sync saves its IOrgUnitChanged messages; a second restart adds no snapshot", "system (restart)");
        AcceptanceGate.RequireEnabled(r);
        var unit = Guid.NewGuid();
        await SqlTextAsync("hr_core_db", $"""
            INSERT INTO "OrgUnits" ("Id","Code","NameAr","TenantId","CreatedAt","UpdatedAt","MaterializedPath")
            VALUES ('{unit:D}','MSG40-{unit:N}','وحدة اختبار','{TenantA:D}',now(),now(),'/{unit:D}/');
            SELECT 1;
            """);
        var sequenceBefore = await OutboxSequenceAsync("hr_core_db");
        var snapshot = $"SELECT COUNT(*) FROM \"OrgUnitSnapshots\" WHERE \"Id\" = '{unit:D}';";

        await RestartHrCoreAsync();
        var payroll = await PollAsync(() => SqlScalarAsync("payroll_db", snapshot), n => n >= 1, TimeSpan.FromSeconds(120));
        var contracts = await PollAsync(() => SqlScalarAsync("hr_contracts_db", snapshot), n => n >= 1, TimeSpan.FromSeconds(120));
        var sequenceAfter = await OutboxSequenceAsync("hr_core_db");
        var pending = await PollAsync(() => SqlScalarAsync("hr_core_db", "SELECT COUNT(*) FROM \"OutboxMessage\";"), n => n == 0, TimeSpan.FromSeconds(120));
        Assert.Equal(1, payroll);
        Assert.Equal(1, contracts);
        Assert.True(sequenceAfter > sequenceBefore, "The start-up re-sync wrote no OutboxMessage row.");
        Assert.Equal(0, pending);

        await RestartHrCoreAsync();
        await Task.Delay(TimeSpan.FromSeconds(15));
        var payrollAgain = await SqlScalarAsync("payroll_db", snapshot);
        var contractsAgain = await SqlScalarAsync("hr_contracts_db", snapshot);
        Assert.Equal(1, payrollAgain);
        Assert.Equal(1, contractsAgain);

        await SqlTextAsync("hr_core_db", $"DELETE FROM \"OrgUnits\" WHERE \"Id\" = '{unit:D}'; SELECT 1;");
        r.WriteScenarioDbChecks(new { unit, sequenceBefore, sequenceAfter, payroll, contracts, pending, payrollAgain, contractsAgain });
        r.WriteScenarioSteps(
            "1. SQL: an agency-A unit in hr_core_db, so no event is published for it.\n" +
            "2. Restart HR.Core: the start-up re-sync publishes every unit under its tenant; the snapshot appears in Payroll and HR.Contracts within 120 s and the outbox drains.\n" +
            "3. Negative: a second restart leaves one snapshot per service (the consumers upsert by id).");
        r.WriteResult("Pass");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcMsg40Collection
{
    public const string Name = "AC-MSG-40 prod-like stack";
}
