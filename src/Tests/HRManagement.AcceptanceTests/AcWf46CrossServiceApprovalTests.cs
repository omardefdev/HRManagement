using System.Net;
using System.Text.RegularExpressions;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.AcceptanceTests;

/// <summary>PLAN-46: the card and discipline journeys through the pages a person uses, posting what the pages render.</summary>
internal static class CrossServiceJourney
{
    public const string Proof = "AC-WF-46";

    // IDCard.Domain.Enums.CardRequestStatus and CardStatus.
    public const int CardDraft = 0;
    public const int CardProcessed = 1;
    public const int CardRejected = 2;
    public const int CardPendingApproval = 3;
    public const int CardApproved = 4;
    public const int CardActive = 0;

    // Compliance.Domain.Enums.DisciplinaryApprovalStatus.
    public const int ActionDraft = 0;
    public const int ActionPendingApproval = 1;
    public const int ActionApproved = 2;
    public const int ActionRejected = 3;

    public const string EngineUnavailable = "تعذّر الوصول إلى محرك الاعتماد";

    public static string CardPage(Guid request) => $"ui/idcard/CardRequests/Details/{request:D}";

    public static string ActionPage(Guid action) => $"ui/compliance/DisciplinaryActions/Details/{action:D}";

    public static Task<long> CardStatusAsync(Guid request) =>
        CustodyJourney.CountAsync("idcard_db", $"SELECT \"Status\" FROM \"CardRequests\" WHERE \"Id\"='{request:D}';");

    public static Task<long> ActionStatusAsync(Guid action) =>
        CustodyJourney.CountAsync("compliance_db", $"SELECT \"ApprovalStatus\" FROM \"DisciplinaryActions\" WHERE \"Id\"='{action:D}';");

    public static async Task<Guid> CardInstanceAsync(Guid request) =>
        Guid.TryParse(await CustodyJourney.ScalarAsync("idcard_db", $"SELECT coalesce(\"WorkflowInstanceId\"::text,'') FROM \"CardRequests\" WHERE \"Id\"='{request:D}';"), out var id) ? id : Guid.Empty;

    public static async Task<Guid> ActionInstanceAsync(Guid action) =>
        Guid.TryParse(await CustodyJourney.ScalarAsync("compliance_db", $"SELECT coalesce(\"WorkflowInstanceId\"::text,'') FROM \"DisciplinaryActions\" WHERE \"Id\"='{action:D}';"), out var id) ? id : Guid.Empty;

    public static Task<long> PayrollRecordsAsync(Guid action) =>
        CustodyJourney.CountAsync("payroll_db", $"SELECT count(*) FROM \"PayrollAdjustments\" WHERE \"AdjustmentReason\" LIKE '%DisciplinaryAction-{action:D}%';");

    /// <summary>A card draft made by <paramref name="persona"/> on the Create page, for a fresh employee id.</summary>
    public static async Task<Guid> CardDraftAsync(HttpClient http, Guid? employee = null)
    {
        var employeeId = employee ?? Guid.NewGuid();
        var page = await CustodyJourney.PostFormAsync(http, "ui/idcard/CardRequests/Create", "Create", new Dictionary<string, string>
        {
            ["EmployeeId"] = employeeId.ToString("D"),
            ["RequestDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["Purpose"] = "0"
        });
        var id = await CustodyJourney.ScalarAsync("idcard_db",
            $"SELECT \"Id\" FROM \"CardRequests\" WHERE \"EmployeeId\"='{employeeId:D}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out var request), $"No card request: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        Assert.Equal(CardDraft, await CardStatusAsync(request));
        return request;
    }

    /// <summary>A submitted card request: PendingApproval with its instance.</summary>
    public static async Task<(Guid Request, Guid Instance)> SubmittedCardAsync(string persona = "a-idcard")
    {
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync(persona);
        var request = await CardDraftAsync(http);
        var page = await CustodyJourney.PostFormAsync(http, CardPage(request), "Submit");
        var instance = await CardInstanceAsync(request);
        Assert.True(instance != Guid.Empty, $"The request has no instance: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        Assert.Equal(CardPendingApproval, await CardStatusAsync(request));
        return (request, instance);
    }

    /// <summary>A warning created by <paramref name="persona"/> on the Create page: saved as a draft, then submitted.</summary>
    public static async Task<(Guid Action, Guid Instance)> SubmittedWarningAsync(string persona = "a-compliance")
    {
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync(persona);
        var employee = Guid.NewGuid();
        var page = await CustodyJourney.PostFormAsync(http, "ui/compliance/DisciplinaryActions/Create", "Create", new Dictionary<string, string>
        {
            ["EmployeeId"] = employee.ToString("D"),
            ["ActionType"] = "0",
            ["IssuedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"),
            ["Notes"] = Proof
        });
        var id = await CustodyJourney.ScalarAsync("compliance_db",
            $"SELECT \"Id\" FROM \"DisciplinaryActions\" WHERE \"EmployeeId\"='{employee:D}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out var action), $"No disciplinary action: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        var instance = await ActionInstanceAsync(action);
        Assert.True(instance != Guid.Empty, $"The action has no instance: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        Assert.Equal(ActionPendingApproval, await ActionStatusAsync(action));
        return (action, instance);
    }

    /// <summary>What a test harness can do and a person cannot: an outcome on the bus, consumed for certain before this returns.</summary>
    public static async Task PublishOutcomeAsync(string database, string entityType, Guid entityId, Guid instance, string outcome)
    {
        var receipts = await CustodyJourney.CountAsync(database, "SELECT count(*) FROM \"IntegrationMessageReceipts\";");
        var (bus, provider) = await AcceptanceMessagingHelper.ConnectBusAsync();
        try
        {
            await provider.GetRequiredService<IPublishEndpoint>().Publish<IWorkflowOutcomeDecided>(new
            {
                TenantId = (Guid?)AcceptanceSeedIds.AgencyAId,
                EntityType = entityType,
                EntityId = entityId,
                InstanceId = instance,
                WorkflowCode = "AC-WF-46",
                Outcome = outcome,
                DecidedByUserId = (Guid?)Guid.NewGuid(),
                DecidedAt = DateTime.UtcNow,
                Reason = (string?)null,
                Decisions = new[] { new WorkflowStepDecisionDto(1, "idcard", Guid.NewGuid(), outcome, DateTime.UtcNow) },
                CorrelationId = Proof,
                SchemaVersion = 1
            }, ctx =>
            {
                ctx.MessageId = Guid.NewGuid();
                ctx.Headers.Set(TenantIdMiddleware.HeaderName, AcceptanceSeedIds.AgencyAId.ToString("D"));
            });
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }

        Assert.True(await CustodyJourney.PollAsync(
            () => CustodyJourney.CountAsync(database, "SELECT count(*) FROM \"IntegrationMessageReceipts\";"), receipts + 1) >= receipts + 1);
    }
}

/// <summary>
/// AC-WF-46 (PLAN-46) on the prod-like stack: the ID card and the disciplinary action through HR.Core's engine, positive and negative.
/// Isolated: AC-WF-46i stops <c>hr-core-web</c>. AC-WF-46j runs with a policy overlay (<see cref="AcWf46PolicyOrphanTests"/>).
/// </summary>
[RequiredAcceptance]
public class AcWf46CrossServiceApprovalTests
{
    private const string Ac = "AC-WF-46";

    private static readonly TimeSpan PayrollWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(120);

    internal static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    [SkippableFact]
    public async Task Ac46a_A_card_is_approved_by_another_card_officer_from_the_inbox_then_processed_into_an_active_card()
    {
        var r = Recorder("46a", "a-idcard submits, a-idcard-2 approves on the generic page, a-idcard processes", "a-idcard");
        AcceptanceGate.RequireEnabled(r);

        var (request, instance) = await CrossServiceJourney.SubmittedCardAsync();
        await CustodyJourney.ApproveFromInboxAsync("a-idcard-2", instance, r);
        Assert.Equal("Completed", await CustodyJourney.InstanceStatusAsync(instance));
        Assert.Equal(CrossServiceJourney.CardApproved, await CustodyJourney.PollAsync(() => CrossServiceJourney.CardStatusAsync(request), CrossServiceJourney.CardApproved));

        using var requester = await AcS2s42TrustBoundaryTests.UiClientAsync("a-idcard");
        var page = await CustodyJourney.PostFormAsync(requester, CrossServiceJourney.CardPage(request), "ProcessRequest");
        Assert.Equal(CrossServiceJourney.CardProcessed, await CrossServiceJourney.CardStatusAsync(request));
        var card = await CustodyJourney.ScalarAsync("idcard_db",
            $"SELECT c.\"Status\" FROM \"IssuedCards\" c JOIN \"CardRequests\" r ON r.\"IssuedCardId\" = c.\"Id\" WHERE r.\"Id\"='{request:D}';");
        Assert.True(card == CrossServiceJourney.CardActive.ToString(), $"No active card: {AcS2s42TrustBoundaryTests.Alerts(page)}");

        r.WriteScenarioDbChecks(new { request, instance, instanceStatus = "Completed", requestStatus = "Processed", card = "Active" });
        r.WriteScenarioSteps(
            "1. a-idcard creates a card request on the Create page (a draft) and submits it: PendingApproval with an instance (psql).\n" +
            "2. The request is in a-idcard-2's inbox; a-idcard-2 approves on the generic page: the instance is Completed.\n" +
            "3. Within 30 s the request is Approved (the outcome event); a-idcard processes it: Processed, with an Active card (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46b_The_requester_cannot_approve_and_a_pending_request_is_not_processed()
    {
        var r = Recorder("46b", "The requester on the URL the reviewer's page renders; ProcessRequest of a pending request", "a-idcard");
        AcceptanceGate.RequireEnabled(r);

        var (request, instance) = await CrossServiceJourney.SubmittedCardAsync();
        var approval = CustodyJourney.ApprovalPage(instance);
        using var reviewer = await AcS2s42TrustBoundaryTests.UiClientAsync("a-idcard-2");
        var url = await CustodyJourney.ApproveUrlAsync(reviewer, instance);

        using var requester = await AcS2s42TrustBoundaryTests.UiClientAsync("a-idcard");
        Assert.False(await CustodyJourney.HasFormAsync(requester, approval, CustodyJourney.ApproveAction));
        var (status, after) = await CustodyJourney.PostWithoutFormAsync(requester, approval, url);
        r.CaptureHttp("requester-approve-negative", "POST", $"/{url}", (int)status);
        Assert.Contains(CustodyJourney.RequesterRefusal, after, StringComparison.Ordinal);
        Assert.Equal("InProgress", await CustodyJourney.InstanceStatusAsync(instance));

        Assert.False(await CustodyJourney.HasFormAsync(requester, CrossServiceJourney.CardPage(request), "ProcessRequest"));
        var (processStatus, processed) = await CustodyJourney.PostWithoutFormAsync(
            requester, CrossServiceJourney.CardPage(request), $"ui/idcard/CardRequests/ProcessRequest/{request:D}");
        r.CaptureHttp("process-pending-negative", "POST", $"/ui/idcard/CardRequests/ProcessRequest/{request:D}", (int)processStatus);
        Assert.Contains("تُعالَج الطلبات المعتمدة فقط", processed, StringComparison.Ordinal);
        Assert.Equal(CrossServiceJourney.CardPendingApproval, await CrossServiceJourney.CardStatusAsync(request));
        Assert.Equal(0, await CustodyJourney.CountAsync("idcard_db", $"SELECT count(*) FROM \"CardRequests\" WHERE \"Id\"='{request:D}' AND \"IssuedCardId\" IS NOT NULL;"));

        r.WriteScenarioDbChecks(new { request, instance, instanceStatus = "InProgress", requestStatus = "PendingApproval" });
        r.WriteScenarioSteps(
            "1. Negative: a-idcard (the requester) sees no approve form; a POST to the URL a-idcard-2's page renders is refused with «لا يجوز لطالب الإجراء».\n" +
            "2. Negative: ProcessRequest of the pending request is refused and issues no card (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46c_Renew_opens_a_replacement_request_and_a_card_without_an_approved_request_is_refused()
    {
        var r = Recorder("46c", "Renew issues nothing; IssuedCards/Create with no approved request is refused", "a-idcard");
        AcceptanceGate.RequireEnabled(r);
        var card = await CustodyJourney.ScalarAsync("idcard_db",
            $"SELECT \"Id\" || ',' || \"EmployeeId\" FROM \"IssuedCards\" WHERE \"Status\" = 0 AND \"TenantId\"='{AcceptanceSeedIds.AgencyAId:D}' AND NOT EXISTS (SELECT 1 FROM \"CardRequests\" q WHERE q.\"ReplacesCardId\" = \"IssuedCards\".\"Id\") ORDER BY \"IssuedAt\" LIMIT 1;");
        AcceptanceGate.RequireData(r, card.Contains(","), "لا توجد بطاقة نشطة", "An active card in agency A with no replacement request.");
        var cardId = Guid.Parse(card.Split(',')[0]);
        var employee = Guid.Parse(card.Split(',')[1]);
        var cardsBefore = await CustodyJourney.CountAsync("idcard_db", "SELECT count(*) FROM \"IssuedCards\";");

        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync("a-idcard");
        var (renewStatus, _) = await CustodyJourney.PostWithoutFormAsync(http, $"ui/idcard/IssuedCards/Details/{cardId:D}", $"ui/idcard/IssuedCards/Renew/{cardId:D}");
        r.CaptureHttp("renew", "POST", $"/ui/idcard/IssuedCards/Renew/{cardId:D}", (int)renewStatus);
        Assert.Equal(cardsBefore, await CustodyJourney.CountAsync("idcard_db", "SELECT count(*) FROM \"IssuedCards\";"));
        Assert.Equal(CrossServiceJourney.CardActive.ToString(), await CustodyJourney.ScalarAsync("idcard_db", $"SELECT \"Status\" FROM \"IssuedCards\" WHERE \"Id\"='{cardId:D}';"));
        Assert.Equal(1, await CustodyJourney.CountAsync("idcard_db",
            $"SELECT count(*) FROM \"CardRequests\" WHERE \"ReplacesCardId\"='{cardId:D}' AND \"Purpose\" = 1 AND \"Status\" = 0 AND \"EmployeeId\"='{employee:D}';"));

        var design = await CustodyJourney.ScalarAsync("idcard_db", $"SELECT \"Id\" FROM \"CardDesigns\" WHERE \"TenantId\"='{AcceptanceSeedIds.AgencyAId:D}' LIMIT 1;");
        var refused = await CustodyJourney.PostFormAsync(http, "ui/idcard/IssuedCards/Create", "Create", new Dictionary<string, string>
        {
            ["EmployeeId"] = Guid.NewGuid().ToString("D"),
            ["CardDesignId"] = design,
            ["IssuedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"),
            ["Status"] = "0"
        });
        Assert.Equal(cardsBefore, await CustodyJourney.CountAsync("idcard_db", "SELECT count(*) FROM \"IssuedCards\";"));
        Assert.Contains("الإصدار يتطلب طلباً معتمداً", refused, StringComparison.Ordinal);

        r.WriteScenarioDbChecks(new { cardId, cardsBefore, replacementRequests = 1, cardStillActive = true });
        r.WriteScenarioSteps(
            "1. a-idcard renews an active card: no new card; a Replacement draft for the same employee points at the card, which stays Active (psql).\n" +
            "2. Negative: IssuedCards/Create with no approved request is refused with «الإصدار يتطلب طلباً معتمداً», and no card is issued, so no ICardIssued (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46d_A_warning_reaches_payroll_only_after_compliance_then_the_authority_approve()
    {
        var r = Recorder("46d", "a-compliance creates a warning; a-compliance-2 then a-management-approver approve", "a-compliance");
        AcceptanceGate.RequireEnabled(r);

        var (action, instance) = await CrossServiceJourney.SubmittedWarningAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await CrossServiceJourney.PayrollRecordsAsync(action));

        await CustodyJourney.ApproveFromInboxAsync("a-compliance-2", instance, r);
        Assert.Equal("2", await CustodyJourney.ScalarAsync("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));
        Assert.Equal(0, await CrossServiceJourney.PayrollRecordsAsync(action));
        await CustodyJourney.ApproveFromInboxAsync("a-management-approver", instance, r);

        Assert.Equal(CrossServiceJourney.ActionApproved, await CustodyJourney.PollAsync(() => CrossServiceJourney.ActionStatusAsync(action), CrossServiceJourney.ActionApproved));
        Assert.Equal(1, await CustodyJourney.PollAsync(() => CrossServiceJourney.PayrollRecordsAsync(action), 1, PayrollWindow));

        r.WriteScenarioDbChecks(new { action, instance, actionStatus = "Approved", payrollRecords = 1 });
        r.WriteScenarioSteps(
            "1. a-compliance creates a warning on the Create page: saved as a draft and submitted: PendingApproval with an instance, and no payroll record (psql).\n" +
            "2. a-compliance-2 approves step 1 from the inbox; still no payroll record.\n" +
            "3. a-management-approver approves step 2 from the inbox: the action is Approved within 30 s, and Payroll's DisciplinaryActionTakenConsumer writes its record within 60 s (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46e_The_creator_central_hr_and_the_first_checker_on_the_second_step_are_refused()
    {
        var r = Recorder("46e", "The creator, a role outside the step, and the wrong role on step 2", "a-compliance");
        AcceptanceGate.RequireEnabled(r);

        var (_, instance) = await CrossServiceJourney.SubmittedWarningAsync();
        var page = CustodyJourney.ApprovalPage(instance);
        using var checker = await AcS2s42TrustBoundaryTests.UiClientAsync("a-compliance-2");
        var url = await CustodyJourney.ApproveUrlAsync(checker, instance);

        using var creator = await AcS2s42TrustBoundaryTests.UiClientAsync("a-compliance");
        Assert.False(await CustodyJourney.HasFormAsync(creator, page, CustodyJourney.ApproveAction));
        var (creatorStatus, creatorPage) = await CustodyJourney.PostWithoutFormAsync(creator, page, url);
        r.CaptureHttp("creator-step1-negative", "POST", $"/{url}", (int)creatorStatus);
        Assert.Contains(CustodyJourney.RequesterRefusal, creatorPage, StringComparison.Ordinal);

        using var central = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("central-a"), Guid.NewGuid().ToString("D"));
        using var refused = await central.PostAsync(
            $"api/hr-core/api/workflow-instances/{instance:D}/approve",
            new StringContent("""{"comments":"AC-WF-46e","targetStepLevel":1}""", System.Text.Encoding.UTF8, "application/json"));
        var refusedBody = await refused.Content.ReadAsStringAsync();
        r.CaptureHttp("central-step1-negative", "POST", $"/api/hr-core/api/workflow-instances/{instance:D}/approve", (int)refused.StatusCode, refusedBody);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(HrManagementRoles.Compliance, refusedBody, StringComparison.Ordinal);
        Assert.Equal("1", await CustodyJourney.ScalarAsync("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));

        await CustodyJourney.PostFormAsync(checker, page, CustodyJourney.ApproveAction);
        Assert.Equal("2", await CustodyJourney.ScalarAsync("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));
        Assert.False(await CustodyJourney.HasFormAsync(checker, page, CustodyJourney.ApproveAction));
        using var authority = await AcS2s42TrustBoundaryTests.UiClientAsync("a-management-approver");
        var stepTwo = await CustodyJourney.ApproveUrlAsync(authority, instance);
        var (_, secondPage) = await CustodyJourney.PostWithoutFormAsync(checker, page, stepTwo);
        Assert.Contains(HrManagementRoles.HrManagementApprover, secondPage, StringComparison.Ordinal);
        Assert.Equal("2", await CustodyJourney.ScalarAsync("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));
        Assert.Equal("InProgress", await CustodyJourney.InstanceStatusAsync(instance));

        r.WriteScenarioDbChecks(new { instance, stepAfterNegatives = 2, status = "InProgress" });
        r.WriteScenarioSteps(
            "1. Negative: a-compliance (the creator) sees no approve form, and a POST to the URL a-compliance-2's page renders is refused.\n" +
            "2. Negative: central-a (hr-core) approves the compliance step: refused by the role (400).\n" +
            "3. a-compliance-2 approves step 1. Negative: a-compliance-2 on step 2, on the URL the authority's page renders: refused by the role.");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46f_A_rejection_at_the_second_step_keeps_the_reason_and_reaches_no_payroll()
    {
        var r = Recorder("46f", "a-management-approver rejects step 2 with a reason", "a-management-approver");
        AcceptanceGate.RequireEnabled(r);
        const string reason = "الوقائع غير ثابتة بما يكفي";

        var (action, instance) = await CrossServiceJourney.SubmittedWarningAsync();
        await CustodyJourney.ApproveFromInboxAsync("a-compliance-2", instance, r);
        using var authority = await AcS2s42TrustBoundaryTests.UiClientAsync("a-management-approver");
        await CustodyJourney.PostFormAsync(authority, CustodyJourney.ApprovalPage(instance), CustodyJourney.RejectAction,
            new Dictionary<string, string> { ["Comments"] = reason });

        Assert.Equal("Rejected", await CustodyJourney.InstanceStatusAsync(instance));
        Assert.Equal(CrossServiceJourney.ActionRejected, await CustodyJourney.PollAsync(() => CrossServiceJourney.ActionStatusAsync(action), CrossServiceJourney.ActionRejected));
        Assert.Equal(reason, await CustodyJourney.ScalarAsync("compliance_db", $"SELECT \"RejectionReason\" FROM \"DisciplinaryActions\" WHERE \"Id\"='{action:D}';"));
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(0, await CrossServiceJourney.PayrollRecordsAsync(action));

        r.WriteScenarioDbChecks(new { action, instance, actionStatus = "Rejected", reason, payrollRecords = 0 });
        r.WriteScenarioSteps("a-compliance-2 approves step 1; a-management-approver rejects step 2 with a reason: the action is Rejected with that reason, and Payroll has no record (psql).");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46g_A_withdrawal_returns_the_action_to_draft_and_a_pending_action_cannot_be_edited()
    {
        var r = Recorder("46g", "a-compliance withdraws a pending action; editing a pending action is refused", "a-compliance");
        AcceptanceGate.RequireEnabled(r);

        var (action, instance) = await CrossServiceJourney.SubmittedWarningAsync();
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync("a-compliance");
        var page = CrossServiceJourney.ActionPage(action);
        var employeeBefore = await CustodyJourney.ScalarAsync("compliance_db", $"SELECT \"EmployeeId\" FROM \"DisciplinaryActions\" WHERE \"Id\"='{action:D}';");

        var (editStatus, _) = await CustodyJourney.PostWithoutFormAsync(http, page, $"ui/compliance/DisciplinaryActions/Edit/{action:D}");
        r.CaptureHttp("edit-pending-negative", "POST", $"/ui/compliance/DisciplinaryActions/Edit/{action:D}", (int)editStatus);
        Assert.Equal(employeeBefore, await CustodyJourney.ScalarAsync("compliance_db", $"SELECT \"EmployeeId\" FROM \"DisciplinaryActions\" WHERE \"Id\"='{action:D}';"));
        Assert.Equal(CrossServiceJourney.ActionPendingApproval, await CrossServiceJourney.ActionStatusAsync(action));

        await CustodyJourney.PostFormAsync(http, page, "Withdraw");
        Assert.Equal("Cancelled", await CustodyJourney.InstanceStatusAsync(instance));
        Assert.Equal(CrossServiceJourney.ActionDraft, await CrossServiceJourney.ActionStatusAsync(action));
        var (editOpen, editPage) = await CustodyJourney.OpenAsync(http, $"ui/compliance/DisciplinaryActions/Edit/{action:D}");
        Assert.Equal(HttpStatusCode.OK, editOpen);
        Assert.Contains("name=\"EmployeeId\"", editPage, StringComparison.Ordinal);

        r.WriteScenarioDbChecks(new { action, instance, instanceStatus = "Cancelled", actionStatus = "Draft" });
        r.WriteScenarioSteps(
            "1. Negative: editing the pending action is refused; its employee is unchanged (psql).\n" +
            "2. a-compliance withdraws it: the instance is Cancelled and the action a Draft (psql); the Edit page opens again.");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46h_An_outcome_naming_an_unlinked_instance_changes_nothing_and_is_logged()
    {
        var r = Recorder("46h", "IWorkflowOutcomeDecided(Approved) for a card request with an instance it is not linked to", "harness");
        AcceptanceGate.RequireEnabled(r);

        var (request, _) = await CrossServiceJourney.SubmittedCardAsync();
        var stranger = Guid.NewGuid();
        var since = DateTime.UtcNow.AddSeconds(-5).ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        await CrossServiceJourney.PublishOutcomeAsync("idcard_db", "CardRequest", request, stranger, "Approved");

        Assert.Equal(CrossServiceJourney.CardPendingApproval, await CrossServiceJourney.CardStatusAsync(request));
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("idcard-web");
        var (_, logs) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "logs", "--since", since, container);
        Assert.Contains(stranger.ToString("D"), logs, StringComparison.OrdinalIgnoreCase);

        r.WriteScenarioDbChecks(new { request, stranger, status = "PendingApproval" });
        r.WriteScenarioSteps("Negative: an approved outcome naming an instance the request is not linked to: the request stays PendingApproval and idcard-web logs the ignored outcome with that id.");
        r.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac46i_With_hr_core_down_a_submission_stays_a_draft_and_the_retry_makes_one_instance()
    {
        var r = Recorder("46i", "hr-core-web stopped: the submission fails in Arabic and the request stays a draft; after the restart, one instance", "a-idcard");
        AcceptanceGate.RequireEnabled(r);
        await AcS2s42TrustBoundaryTests.StackReadyAsync();

        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync("a-idcard");
        var request = await CrossServiceJourney.CardDraftAsync(http);
        await AcS2s42TrustBoundaryTests.StopAsync("hr-core-web");
        string failed;
        try
        {
            failed = await CustodyJourney.PostFormAsync(http, CrossServiceJourney.CardPage(request), "Submit");
        }
        finally
        {
            await AcS2s42TrustBoundaryTests.StartAsync("hr-core-web");
        }

        Assert.Contains(CrossServiceJourney.EngineUnavailable, failed, StringComparison.Ordinal);
        Assert.Equal(CrossServiceJourney.CardDraft, await CrossServiceJourney.CardStatusAsync(request));
        Assert.Equal(Guid.Empty, await CrossServiceJourney.CardInstanceAsync(request));

        await AcS2s42TrustBoundaryTests.StackReadyAsync();
        var deadline = DateTime.UtcNow + RestartWindow;
        while (await CrossServiceJourney.CardStatusAsync(request) != CrossServiceJourney.CardPendingApproval && DateTime.UtcNow < deadline)
        {
            await CustodyJourney.PostFormAsync(http, CrossServiceJourney.CardPage(request), "Submit");
            if (await CrossServiceJourney.CardStatusAsync(request) != CrossServiceJourney.CardPendingApproval)
                await Task.Delay(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(CrossServiceJourney.CardPendingApproval, await CrossServiceJourney.CardStatusAsync(request));
        Assert.Equal(1, await CustodyJourney.CountAsync("hr_core_db",
            $"SELECT count(*) FROM \"WorkflowInstances\" WHERE \"EntityType\"='CardRequest' AND \"EntityId\"='{request:D}';"));
        var pendingWithout = await CustodyJourney.CountAsync("idcard_db", "SELECT count(*) FROM \"CardRequests\" WHERE \"Status\" = 3 AND \"WorkflowInstanceId\" IS NULL;")
            + await CustodyJourney.CountAsync("compliance_db", "SELECT count(*) FROM \"DisciplinaryActions\" WHERE \"ApprovalStatus\" = 1 AND \"WorkflowInstanceId\" IS NULL;");
        Assert.Equal(0, pendingWithout);

        r.WriteScenarioDbChecks(new { request, instances = 1, pendingWithoutInstance = pendingWithout });
        r.WriteScenarioSteps(
            "1. hr-core-web is stopped. a-idcard submits a draft: the page says «تعذّر الوصول إلى محرك الاعتماد» and the request stays a draft with no instance (psql).\n" +
            "2. hr-core-web starts; the submission again: PendingApproval, and HR.Core holds exactly one instance for the request (psql).\n" +
            "3. Negative: no card request and no disciplinary action is PendingApproval without an instance (psql).");
        r.WriteResult("Pass");
    }
}

/// <summary>
/// AC-WF-46j (PLAN-46) with the card policy in Enforce (docker-compose.acceptance-wf46.yml, applied with the s2s42 overlay): the four orphan
/// definitions are unpublished, and R-ID-PROCESS starts nothing.
/// </summary>
[RequiredAcceptance]
public class AcWf46PolicyOrphanTests
{
    private static readonly string[] Orphans = ["HIRE-WIZARD", "TRANSFER-APPROVAL", "IT-TICKET", "SEPARATION-WIZARD"];

    [SkippableFact]
    public async Task Ac46j_The_orphans_are_retired_and_the_card_policy_starts_no_instance()
    {
        var r = AcWf46CrossServiceApprovalTests.Recorder("46j", "The definitions list; ProcessRequest under PolicyEngine:IDCard.Lifecycle=Enforce", "p-workflow-admin-2");
        AcceptanceGate.RequireEnabled(r);
        // The overlay of this stage has just recreated hr-core-web and idcard-web.
        await AcS2s42TrustBoundaryTests.StackReadyAsync();

        // The national definitions, read by a national reader: an agency user sees only its own tenant's, which would prove nothing.
        using var reader = await AcS2s42TrustBoundaryTests.UiClientAsync("p-workflow-admin-2");
        using var listed = await reader.GetAsync("api/hr-core/api/workflow-definitions?publishedOnly=true");
        var published = await listed.Content.ReadAsStringAsync();
        r.CaptureHttp("published-definitions", "GET", "/api/hr-core/api/workflow-definitions?publishedOnly=true", (int)listed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        foreach (var code in Orphans)
            Assert.DoesNotContain($"\"{code}\"", published, StringComparison.Ordinal);
        Assert.Contains("\"ID-CARD-ISSUANCE\"", published, StringComparison.Ordinal);
        Assert.Equal(0, await CustodyJourney.CountAsync("hr_core_db",
            "SELECT count(*) FROM \"WorkflowDefinitions\" WHERE \"Code\" IN ('HIRE-WIZARD','TRANSFER-APPROVAL','IT-TICKET','SEPARATION-WIZARD') AND \"IsPublished\";"));

        var (request, instance) = await CrossServiceJourney.SubmittedCardAsync();
        await CustodyJourney.ApproveFromInboxAsync("a-idcard-2", instance, r);
        Assert.Equal(CrossServiceJourney.CardApproved, await CustodyJourney.PollAsync(() => CrossServiceJourney.CardStatusAsync(request), CrossServiceJourney.CardApproved));
        var instancesBefore = await CustodyJourney.CountAsync("hr_core_db", "SELECT count(*) FROM \"WorkflowInstances\";");

        using var requester = await AcS2s42TrustBoundaryTests.UiClientAsync("a-idcard");
        var page = await CustodyJourney.PostFormAsync(requester, CrossServiceJourney.CardPage(request), "ProcessRequest");

        Assert.Equal(instancesBefore, await CustodyJourney.CountAsync("hr_core_db", "SELECT count(*) FROM \"WorkflowInstances\";"));
        var outcome = await CrossServiceJourney.CardStatusAsync(request);
        r.WriteScenarioDbChecks(new { orphansPublished = 0, request, instancesBefore, instancesAfter = instancesBefore, requestStatusAfterProcess = outcome, alerts = AcS2s42TrustBoundaryTests.Alerts(page) });
        r.WriteScenarioSteps(
            "1. GET api/workflow-definitions?publishedOnly=true lists none of HIRE-WIZARD, TRANSFER-APPROVAL, IT-TICKET, SEPARATION-WIZARD (and none is published in psql).\n" +
            "2. Negative: with PolicyEngine:IDCard.Lifecycle=Enforce on idcard-web and hr-core-web, a-idcard processes an approved request: R-ID-PROCESS starts no instance in HR.Core (psql).");
        r.WriteResult("Pass");
    }
}
