using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Workflow;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Consumers;
using IDCard.Infrastructure.Data;
using IDCard.Infrastructure.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-46 (section 3): a card request through the contract, around a fake engine.</summary>
[Collection("TenantFlow")]
public sealed class Wf046CardApprovalTests
{
    private static readonly Guid Tenant = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    private static readonly Guid Requester = Guid.Parse("46000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Submitter = Guid.Parse("46000000-0000-4000-8000-0000000000a2");
    private static readonly Guid Reviewer = Guid.Parse("46000000-0000-4000-8000-0000000000a3");

    private static IDCardDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IDCardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CardRequestApprovalService Service(IDCardDbContext db, IWorkflowApprovalClient engine) =>
        new(db, engine, NullLogger<CardRequestApprovalService>.Instance);

    private static async Task<CardRequest> DraftAsync(IDCardDbContext db, CardRequestStatus status = CardRequestStatus.Pending)
    {
        var request = new CardRequest
        {
            TenantId = Tenant,
            EmployeeId = Guid.NewGuid(),
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Purpose = CardRequestPurpose.New,
            Status = status,
            RequestedBy = Requester,
            CreatedBy = Requester
        };
        db.CardRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    [Theory]
    [InlineData(CardRequestStatus.Pending, false, true, true, false, true)]
    [InlineData(CardRequestStatus.PendingApproval, false, false, false, true, true)]
    [InlineData(CardRequestStatus.Approved, true, false, false, false, true)]
    [InlineData(CardRequestStatus.Processed, false, false, false, false, false)]
    [InlineData(CardRequestStatus.Rejected, false, false, false, false, false)]
    public void Each_state_allows_what_the_plan_says(
        CardRequestStatus status, bool process, bool submit, bool edit, bool withdraw, bool cancel)
    {
        Assert.Equal(process, CardIssuanceWorkflow.CanProcess(status));
        Assert.Equal(submit, CardIssuanceWorkflow.CanSubmit(status));
        Assert.Equal(edit, CardIssuanceWorkflow.CanEdit(status));
        Assert.Equal(withdraw, CardIssuanceWorkflow.CanWithdraw(status));
        Assert.Equal(cancel, CardIssuanceWorkflow.CanCancel(status));
    }

    [Fact]
    public void The_new_states_go_at_the_end_so_stored_numbers_keep_their_meaning()
    {
        Assert.Equal(0, (int)CardRequestStatus.Pending);
        Assert.Equal(1, (int)CardRequestStatus.Processed);
        Assert.Equal(2, (int)CardRequestStatus.Rejected);
        Assert.Equal(3, (int)CardRequestStatus.PendingApproval);
        Assert.Equal(4, (int)CardRequestStatus.Approved);
    }

    [Fact]
    public async Task A_submission_links_the_instance_in_the_same_save_and_excludes_requester_and_submitter()
    {
        await using var db = NewDb();
        var request = await DraftAsync(db);
        var engine = new FakeWorkflowApprovalClient();
        Guid? flowAtStart = null;

        var result = await Service(db, new TenantRecordingClient(engine, t => flowAtStart = t)).SubmitAsync(request, Submitter);

        Assert.True(result.Success, result.Error);
        var start = Assert.Single(engine.Starts);
        Assert.Equal("CardRequest", start.EntityType);
        Assert.Equal("ID-CARD-ISSUANCE", start.WorkflowCode);
        Assert.Equal($"CardRequest:{request.Id:D}:1", start.IdempotencyKey);
        Assert.Equal([Requester, Submitter], start.ExcludedApproverUserIds.Order());
        Assert.Equal(request.EmployeeId, start.SubjectEmployeeId);
        Assert.StartsWith("/ui/idcard/", start.DetailsUrl, StringComparison.Ordinal);
        Assert.Equal(Tenant, flowAtStart);
        Assert.Null(TenantIdFlow.Get());

        db.ChangeTracker.Clear();
        var saved = await db.CardRequests.SingleAsync();
        Assert.Equal(CardRequestStatus.PendingApproval, saved.Status);
        Assert.Equal(result.InstanceId, saved.WorkflowInstanceId);
        Assert.Equal(1, saved.SubmissionNo);
        Assert.Equal(Submitter, saved.SubmittedBy);
    }

    [Theory]
    [InlineData(WorkflowApprovalErrorCodes.EngineUnavailable, WorkflowApprovalErrorCodes.EngineUnavailableMessage)]
    [InlineData(WorkflowApprovalErrorCodes.DefinitionMissing, WorkflowApprovalErrorCodes.DefinitionMissingMessage)]
    public async Task A_failed_start_leaves_the_draft_as_it_was(string code, string message)
    {
        await using var db = NewDb();
        var request = await DraftAsync(db);
        var engine = new FakeWorkflowApprovalClient { StartFailure = new WorkflowApprovalStartResult(false, null, false, code, null) };

        var result = await Service(db, engine).SubmitAsync(request, Submitter);

        Assert.False(result.Success);
        Assert.Equal(message, result.Error);
        db.ChangeTracker.Clear();
        var saved = await db.CardRequests.SingleAsync();
        Assert.Equal(CardRequestStatus.Pending, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
        Assert.Equal(0, saved.SubmissionNo);
    }

    [Fact]
    public async Task A_retry_after_a_failure_uses_the_same_key_and_finds_the_same_instance()
    {
        await using var db = NewDb();
        var request = await DraftAsync(db);
        var engine = new FakeWorkflowApprovalClient { StartFailure = new WorkflowApprovalStartResult(false, null, false, WorkflowApprovalErrorCodes.EngineUnavailable, null) };
        await Service(db, engine).SubmitAsync(request, Submitter);

        engine.StartFailure = null;
        var first = await Service(db, engine).SubmitAsync(request, Submitter);

        Assert.True(first.Success);
        Assert.Equal(engine.Starts[0].IdempotencyKey, engine.Starts[1].IdempotencyKey);
    }

    [Fact]
    public async Task A_save_that_fails_after_the_start_withdraws_the_instance()
    {
        await using var db = new FailingSaveDb();
        var request = await DraftAsync(db);
        var engine = new FakeWorkflowApprovalClient();
        db.FailNextSave = true;

        var result = await Service(db, engine).SubmitAsync(request, Submitter);

        Assert.False(result.Success);
        Assert.Equal(WorkflowApprovalSubmission.SaveFailedMessage, result.Error);
        var (instance, reason) = Assert.Single(engine.Cancels);
        Assert.Equal(WorkflowApprovalSubmission.LocalSaveFailedReason, reason);
        Assert.NotEqual(Guid.Empty, instance);
    }

    [Fact]
    public async Task A_key_whose_instance_was_withdrawn_moves_to_the_next_number()
    {
        await using var db = NewDb();
        var request = await DraftAsync(db);
        var engine = new FakeWorkflowApprovalClient { FinishedKey = $"CardRequest:{request.Id:D}:1" };
        var first = (await engine.StartAsync(new WorkflowApprovalStartRequest("CardRequest", request.Id, "", engine.FinishedKey, [], null, "", "", null), default)).InstanceId!.Value;
        engine.Starts.Clear();
        engine.SetStatus(first, WorkflowApprovalOutcomes.Cancelled, "CardRequest", request.Id);

        var result = await Service(db, engine).SubmitAsync(request, Submitter);

        Assert.True(result.Success, result.Error);
        Assert.Equal([$"CardRequest:{request.Id:D}:1", $"CardRequest:{request.Id:D}:2"], engine.Starts.Select(s => s.IdempotencyKey));
        db.ChangeTracker.Clear();
        Assert.Equal(2, (await db.CardRequests.SingleAsync()).SubmissionNo);
    }

    [Fact]
    public async Task A_key_whose_instance_was_approved_applies_that_outcome()
    {
        await using var db = NewDb();
        var request = await DraftAsync(db);
        var key = $"CardRequest:{request.Id:D}:1";
        var engine = new FakeWorkflowApprovalClient { FinishedKey = key };
        var instance = (await engine.StartAsync(new WorkflowApprovalStartRequest("CardRequest", request.Id, "", key, [], null, "", "", null), default)).InstanceId!.Value;
        engine.SetStatus(instance, WorkflowApprovalOutcomes.Approved, "CardRequest", request.Id,
            decisions: [new WorkflowStepDecisionDto(1, "idcard", Reviewer, "Approved", DateTime.UtcNow)]);

        var result = await Service(db, engine).SubmitAsync(request, Submitter);

        Assert.True(result.Finished);
        db.ChangeTracker.Clear();
        Assert.Equal(CardRequestStatus.Approved, (await db.CardRequests.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(WorkflowApprovalOutcomes.Approved, CardRequestStatus.Approved)]
    [InlineData(WorkflowApprovalOutcomes.Rejected, CardRequestStatus.Rejected)]
    [InlineData(WorkflowApprovalOutcomes.Cancelled, CardRequestStatus.Pending)]
    public async Task The_consumer_applies_each_outcome_to_the_linked_pending_request(string outcome, CardRequestStatus expected)
    {
        await using var db = NewDb();
        var request = await PendingAsync(db);

        await Consume(db, FakeWorkflowApprovalClient.Outcome("CardRequest", request.Id, request.WorkflowInstanceId!.Value, outcome,
            Tenant, Reviewer, outcome == WorkflowApprovalOutcomes.Rejected ? "الصورة غير واضحة" : null,
            [new WorkflowStepDecisionDto(1, "idcard", Reviewer, outcome == WorkflowApprovalOutcomes.Cancelled ? "Withdrawn" : outcome, DateTime.UtcNow)]));

        db.ChangeTracker.Clear();
        var saved = await db.CardRequests.SingleAsync();
        Assert.Equal(expected, saved.Status);
        Assert.Equal(Reviewer, saved.DecidedBy);
        Assert.Equal(outcome == WorkflowApprovalOutcomes.Rejected ? "الصورة غير واضحة" : null, saved.RejectionReason);
    }

    [Fact]
    public async Task An_unlinked_instance_a_closed_request_and_another_type_change_nothing()
    {
        await using var db = NewDb();
        var request = await PendingAsync(db);
        var approved = new[] { new WorkflowStepDecisionDto(1, "idcard", Reviewer, "Approved", DateTime.UtcNow) };

        await Consume(db, FakeWorkflowApprovalClient.Outcome("CardRequest", request.Id, Guid.NewGuid(), WorkflowApprovalOutcomes.Approved, Tenant, Reviewer, decisions: approved));
        await Consume(db, FakeWorkflowApprovalClient.Outcome("DisciplinaryAction", request.Id, request.WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved, Tenant, Reviewer, decisions: approved));
        db.ChangeTracker.Clear();
        Assert.Equal(CardRequestStatus.PendingApproval, (await db.CardRequests.SingleAsync()).Status);

        var cancelled = await db.CardRequests.SingleAsync();
        CardRequestRules.SoftCancel(cancelled, "ملغى");
        await db.SaveChangesAsync();
        await Consume(db, FakeWorkflowApprovalClient.Outcome("CardRequest", request.Id, request.WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved, Tenant, Reviewer, decisions: approved));
        db.ChangeTracker.Clear();
        Assert.NotEqual(CardRequestStatus.Approved, (await db.CardRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task An_approval_by_the_requester_is_refused()
    {
        await using var db = NewDb();
        var request = await PendingAsync(db);

        await Consume(db, FakeWorkflowApprovalClient.Outcome("CardRequest", request.Id, request.WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved,
            Tenant, Requester, decisions: [new WorkflowStepDecisionDto(1, "idcard", Requester, "Approved", DateTime.UtcNow)]));

        db.ChangeTracker.Clear();
        var refused = await db.CardRequests.SingleAsync();
        Assert.Equal(CardRequestStatus.Rejected, refused.Status); // closed, never left pending for the sweep to read again
        Assert.Equal(CardRequestApprovalService.RefusedApprovalReason, refused.RejectionReason);
    }

    [Fact]
    public async Task A_withdrawal_returns_the_request_to_draft()
    {
        await using var db = NewDb();
        var request = await PendingAsync(db);
        var engine = new FakeWorkflowApprovalClient();

        var result = await Service(db, engine).WithdrawAsync(request, Requester, "لم يعد مطلوباً");

        Assert.True(result.Success);
        Assert.Equal(request.WorkflowInstanceId, Assert.Single(engine.Cancels).InstanceId);
        db.ChangeTracker.Clear();
        Assert.Equal(CardRequestStatus.Pending, (await db.CardRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_withdrawal_that_finds_the_approval_ended_keeps_its_outcome()
    {
        await using var db = NewDb();
        var request = await PendingAsync(db);
        var engine = new FakeWorkflowApprovalClient
        {
            CancelResult = new WorkflowApprovalCancelResult(false, true, WorkflowApprovalOutcomes.Approved, null, "منتهٍ")
        };
        engine.SetStatus(request.WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved, "CardRequest", request.Id,
            decisions: [new WorkflowStepDecisionDto(1, "idcard", Reviewer, "Approved", DateTime.UtcNow)]);

        var result = await Service(db, engine).WithdrawAsync(request, Requester, "سحب");

        Assert.False(result.Success);
        Assert.Equal(WorkflowApprovalOutcomes.Approved, result.FinalOutcome);
        db.ChangeTracker.Clear();
        Assert.Equal(CardRequestStatus.Approved, (await db.CardRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_withdrawal_the_engine_cannot_explain_changes_nothing_here()
    {
        await using var db = NewDb();
        var request = await PendingAsync(db);
        var engine = new FakeWorkflowApprovalClient
        {
            CancelResult = new WorkflowApprovalCancelResult(false, true, WorkflowApprovalOutcomes.Rejected, null, null)
        };

        var result = await Service(db, engine).WithdrawAsync(request, Requester, "سحب");

        Assert.False(result.Success);
        Assert.Equal(WorkflowApprovalSubmission.FinishedPendingMessage, result.Error);
        db.ChangeTracker.Clear();
        Assert.Equal(CardRequestStatus.PendingApproval, (await db.CardRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Only_a_draft_is_submitted()
    {
        await using var db = NewDb();
        var engine = new FakeWorkflowApprovalClient();

        var pending = await Service(db, engine).SubmitAsync(await PendingAsync(db), Submitter);
        var approved = await Service(db, engine).SubmitAsync(await DraftAsync(db, CardRequestStatus.Approved), Submitter);

        Assert.Equal(CardRequestApprovalService.NotDraftMessage, pending.Error);
        Assert.Equal(CardRequestApprovalService.NotDraftMessage, approved.Error);
        Assert.Empty(engine.Starts);
    }

    [Fact]
    public async Task The_sweep_store_lists_waiting_requests_and_their_tenants()
    {
        await using var db = NewDb();
        var stale = await PendingAsync(db, submittedAt: DateTime.UtcNow.AddMinutes(-30));
        await PendingAsync(db, submittedAt: DateTime.UtcNow);
        db.AuditLogs.Add(new HRManagement.SharedKernel.Entities.EntityAuditLog { TenantId = Tenant, EntityType = nameof(CardRequest), EntityId = stale.Id, Action = "Created" });
        db.AuditLogs.Add(new HRManagement.SharedKernel.Entities.EntityAuditLog { TenantId = Guid.Empty, EntityType = nameof(CardRequest), EntityId = stale.Id, Action = "Created" });
        await db.SaveChangesAsync();
        var store = new CardRequestApprovalStore(db, Service(db, new FakeWorkflowApprovalClient()));

        Assert.Equal([Tenant], await store.ListTenantsAsync(default));
        var waiting = Assert.Single(await store.ListPendingAsync(DateTime.UtcNow.AddMinutes(-15), null, 10, default));
        Assert.Equal(stale.Id, waiting.EntityId);
        Assert.Equal(AdvisoryLockKeys.CardRequestApprovalSweep, store.LeaderLockKey);
    }

    private static async Task<CardRequest> PendingAsync(IDCardDbContext db, DateTime? submittedAt = null)
    {
        var request = await DraftAsync(db, CardRequestStatus.PendingApproval);
        request.WorkflowInstanceId = Guid.NewGuid();
        request.SubmissionNo = 1;
        request.SubmittedBy = Submitter;
        request.SubmittedAt = submittedAt ?? DateTime.UtcNow;
        await db.SaveChangesAsync();
        return request;
    }

    private static Task Consume(IDCardDbContext db, IWorkflowOutcomeDecided message)
    {
        var context = new Mock<ConsumeContext<IWorkflowOutcomeDecided>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return new CardRequestApprovalOutcomeConsumer(Service(db, new FakeWorkflowApprovalClient())).Consume(context.Object);
    }

    private sealed class FailingSaveDb() : IDCardDbContext(new DbContextOptionsBuilder<IDCardDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
    {
        public bool FailNextSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!FailNextSave)
                return base.SaveChangesAsync(cancellationToken);

            FailNextSave = false;
            throw new DbUpdateConcurrencyException("xmin moved");
        }
    }

    private sealed class TenantRecordingClient(IWorkflowApprovalClient inner, Action<Guid?> record) : IWorkflowApprovalClient
    {
        public Task<WorkflowApprovalStartResult> StartAsync(WorkflowApprovalStartRequest request, CancellationToken ct)
        {
            record(TenantIdFlow.Get());
            return inner.StartAsync(request, ct);
        }

        public Task<WorkflowApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct) => inner.CancelAsync(instanceId, reason, ct);

        public Task<WorkflowApprovalStatusDto?> GetStatusAsync(Guid instanceId, CancellationToken ct) => inner.GetStatusAsync(instanceId, ct);
    }
}

/// <summary>PLAN-46 (section 3, rule 1): no path in IDCard makes a card except from an approved request.</summary>
public sealed class Wf046CardIssuanceArchitectureTests
{
    private static readonly string ServiceRoot = Path.Combine(RepoPaths.Root, "src", "Services", "IDCard");

    /// <summary>The issuing sites, each checking an approved request in the same action. The demo seeder writes seed data only.</summary>
    private static readonly (string File, string Method)[] IssuingSites =
    [
        ("IDCard.Web/Controllers/CardRequestsController.cs", "public async Task<IActionResult> ProcessRequest("),
        ("IDCard.Web/Controllers/IssuedCardsController.cs", "public async Task<IActionResult> Create(IssuedCardFormModel model)")
    ];

    private const string SeederFile = "IDCard.Infrastructure/Data/DemoIDCardSeeder.cs";

    [Fact]
    public void Every_card_comes_from_an_approved_request()
    {
        var problems = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = global::System.IO.File.ReadAllText(file);
            var relative = Path.GetRelativePath(ServiceRoot, file).Replace('\\', '/');
            foreach (var index in Occurrences(text, "IssuedCards.Add("))
            {
                if (relative == SeederFile)
                    continue;

                var site = IssuingSites.FirstOrDefault(s => s.File == relative && MethodAt(text, index).Contains(s.Method, StringComparison.Ordinal));
                if (site.File is null)
                    problems.Add($"{relative}: a card is added outside the approved issuing sites");
                else if (!MethodBody(text, site.Method).Contains("CardIssuanceWorkflow.CanProcess(", StringComparison.Ordinal))
                    problems.Add($"{relative}: {site.Method} adds a card without checking an approved request");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Only_the_issue_from_an_approved_request_publishes_a_card()
    {
        var sites = SourceFiles()
            .SelectMany(file =>
            {
                var text = global::System.IO.File.ReadAllText(file);
                return Occurrences(text, "Publish<ICardIssued>").Select(i => (File: Path.GetRelativePath(ServiceRoot, file).Replace('\\', '/'), Method: MethodAt(text, i)));
            })
            .ToList();

        var site = Assert.Single(sites);
        Assert.Equal("IDCard.Web/Controllers/IssuedCardsController.cs", site.File);
        Assert.Contains("Create(IssuedCardFormModel model)", site.Method, StringComparison.Ordinal);
    }

    [Fact]
    public void Renew_issues_nothing()
    {
        var text = global::System.IO.File.ReadAllText(Path.Combine(ServiceRoot, "IDCard.Web", "Controllers", "IssuedCardsController.cs"));
        var renew = MethodBody(text, "public async Task<IActionResult> Renew(");

        Assert.DoesNotContain("IssuedCards.Add(", renew, StringComparison.Ordinal);
        Assert.DoesNotContain("Publish<", renew, StringComparison.Ordinal);
        Assert.Contains("CardRequestPurpose.Replacement", renew, StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(ServiceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static IEnumerable<int> Occurrences(string text, string token)
    {
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal))
            yield return i;
    }

    /// <summary>The signature line of the method that contains <paramref name="index"/>.</summary>
    private static string MethodAt(string text, int index)
    {
        var start = Math.Max(text.LastIndexOf("    public ", index, StringComparison.Ordinal), text.LastIndexOf("    internal ", index, StringComparison.Ordinal));
        start = Math.Max(start, text.LastIndexOf("    private ", index, StringComparison.Ordinal));
        if (start < 0)
            return string.Empty;
        var end = text.IndexOf('\n', start);
        return text[start..end];
    }

    private static string MethodBody(string text, string signature)
    {
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var next = text.IndexOf("\n    public ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }
}
