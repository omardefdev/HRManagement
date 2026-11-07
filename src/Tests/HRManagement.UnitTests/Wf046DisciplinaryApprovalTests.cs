using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Domain.Services;
using Compliance.Infrastructure.Consumers;
using Compliance.Infrastructure.Data;
using Compliance.Infrastructure.Services;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-46 (section 4): a disciplinary action reaches payroll only after compliance, then the authority, approve it.</summary>
[Collection("TenantFlow")]
public sealed class Wf046DisciplinaryApprovalTests
{
    private static readonly Guid Tenant = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    private static readonly Guid Creator = Guid.Parse("46000000-0000-4000-8000-0000000000c1");
    private static readonly Guid Checker = Guid.Parse("46000000-0000-4000-8000-0000000000c2");
    private static readonly Guid Authority = Guid.Parse("46000000-0000-4000-8000-0000000000c3");

    private static readonly WorkflowStepDecisionDto[] TwoSteps =
    [
        new(1, "compliance", Checker, "Approved", DateTime.UtcNow),
        new(2, "hr-management-approver", Authority, "Approved", DateTime.UtcNow)
    ];

    private static ComplianceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ComplianceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (DisciplinaryApprovalService Service, List<object> Published) Service(ComplianceDbContext db, IWorkflowApprovalClient? engine = null)
    {
        var published = new List<object>();
        var publish = new Mock<IPublishEndpoint>();
        publish.Setup(p => p.Publish<IDisciplinaryActionTaken>(
                It.IsAny<object>(), It.IsAny<IPipe<PublishContext<IDisciplinaryActionTaken>>>(), It.IsAny<CancellationToken>()))
            .Callback<object, IPipe<PublishContext<IDisciplinaryActionTaken>>, CancellationToken>((values, _, _) => published.Add(values))
            .Returns(Task.CompletedTask);
        return (new DisciplinaryApprovalService(db, engine ?? new FakeWorkflowApprovalClient(), publish.Object,
            NullLogger<DisciplinaryApprovalService>.Instance), published);
    }

    private static async Task<DisciplinaryAction> ActionAsync(
        ComplianceDbContext db, DisciplinaryApprovalStatus status = DisciplinaryApprovalStatus.Draft, bool beforeEngine = false)
    {
        var action = new DisciplinaryAction
        {
            TenantId = Tenant,
            EmployeeId = Guid.NewGuid(),
            ActionType = ActionType.Warning,
            IssuedAt = DateTime.UtcNow,
            ApprovalStatus = status,
            ApprovedBeforeEngine = beforeEngine,
            CreatedBy = Creator
        };
        if (status == DisciplinaryApprovalStatus.PendingApproval)
        {
            action.WorkflowInstanceId = Guid.NewGuid();
            action.SubmissionNo = 1;
            action.SubmittedBy = Creator;
            action.SubmittedAt = DateTime.UtcNow;
        }

        db.DisciplinaryActions.Add(action);
        await db.SaveChangesAsync();
        return action;
    }

    [Theory]
    [InlineData(DisciplinaryApprovalStatus.Draft, true, true, false, false)]
    [InlineData(DisciplinaryApprovalStatus.Rejected, true, true, false, false)]
    [InlineData(DisciplinaryApprovalStatus.PendingApproval, false, false, true, false)]
    [InlineData(DisciplinaryApprovalStatus.Approved, false, false, false, true)]
    public void Each_state_allows_what_the_plan_says(DisciplinaryApprovalStatus status, bool edit, bool submit, bool withdraw, bool appeal)
    {
        Assert.Equal(edit, DisciplinaryWorkflowRules.CanEdit(status));
        Assert.Equal(submit, DisciplinaryWorkflowRules.CanSubmit(status));
        Assert.Equal(withdraw, DisciplinaryWorkflowRules.CanWithdraw(status));
        Assert.Equal(appeal, DisciplinaryWorkflowRules.CanAppeal(AppealStatus.None, status));
        Assert.Equal(appeal, DisciplinaryWorkflowRules.CanDecide(AppealStatus.Pending, status));
    }

    [Fact]
    public async Task A_submission_excludes_the_creator_and_links_the_instance()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db);
        var engine = new FakeWorkflowApprovalClient();
        var (service, published) = Service(db, engine);

        var result = await service.SubmitAsync(action, Creator, "سامي الأحمد");

        Assert.True(result.Success, result.Error);
        var start = Assert.Single(engine.Starts);
        Assert.Equal("DISCIPLINARY-APPROVAL", start.WorkflowCode);
        Assert.Equal($"DisciplinaryAction:{action.Id:D}:1", start.IdempotencyKey);
        Assert.Equal([Creator], start.ExcludedApproverUserIds);
        Assert.Equal("إجراء تأديبي — سامي الأحمد — إنذار", start.DisplayTitle);
        Assert.Equal(action.EmployeeId, start.SubjectEmployeeId);
        Assert.Empty(published);
        db.ChangeTracker.Clear();
        var saved = await db.DisciplinaryActions.SingleAsync();
        Assert.Equal(DisciplinaryApprovalStatus.PendingApproval, saved.ApprovalStatus);
        Assert.Equal(result.InstanceId, saved.WorkflowInstanceId);
    }

    [Fact]
    public async Task A_title_never_carries_a_number()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db);
        var engine = new FakeWorkflowApprovalClient();

        await Service(db, engine).Service.SubmitAsync(action, Creator, "موظف 0123456789");

        Assert.Equal("إجراء تأديبي — إنذار", Assert.Single(engine.Starts).DisplayTitle);
    }

    [Fact]
    public async Task A_failed_start_leaves_a_draft_and_publishes_nothing()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db);
        var engine = new FakeWorkflowApprovalClient
        {
            StartFailure = new WorkflowApprovalStartResult(false, null, false, WorkflowApprovalErrorCodes.EngineUnavailable, null)
        };
        var (service, published) = Service(db, engine);

        var result = await service.SubmitAsync(action, Creator, null);

        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailableMessage, result.Error);
        Assert.Empty(published);
        db.ChangeTracker.Clear();
        Assert.Equal(DisciplinaryApprovalStatus.Draft, (await db.DisciplinaryActions.SingleAsync()).ApprovalStatus);
    }

    [Fact]
    public async Task The_approval_publishes_once_with_the_same_payload_and_a_stable_message_id()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db, DisciplinaryApprovalStatus.PendingApproval);
        var (service, published) = Service(db);
        var outcome = FakeWorkflowApprovalClient.Outcome("DisciplinaryAction", action.Id, action.WorkflowInstanceId!.Value,
            WorkflowApprovalOutcomes.Approved, Tenant, Authority, decisions: TwoSteps);

        await Consume(service, outcome);
        await Consume(service, outcome);

        var message = Assert.Single(published);
        var json = System.Text.Json.JsonSerializer.Serialize(message);
        Assert.Contains($"\"ActionId\":\"{action.Id:D}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"PenaltyDays\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"ActionType\":\"Warning\"", json, StringComparison.Ordinal);
        db.ChangeTracker.Clear();
        var saved = await db.DisciplinaryActions.SingleAsync();
        Assert.Equal(DisciplinaryApprovalStatus.Approved, saved.ApprovalStatus);
        Assert.NotNull(saved.OutcomePublishedAt);
        Assert.Equal(Authority, saved.DecidedBy);
    }

    [Fact]
    public void The_message_id_comes_from_the_action()
    {
        var id = Guid.NewGuid();
        Assert.Equal(DeterministicMessageId.For<IDisciplinaryActionTaken>(id), DeterministicMessageId.For<IDisciplinaryActionTaken>(id));
    }

    [Fact]
    public async Task An_approval_that_names_the_creator_is_refused_and_publishes_nothing()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db, DisciplinaryApprovalStatus.PendingApproval);
        var (service, published) = Service(db);

        await Consume(service, FakeWorkflowApprovalClient.Outcome("DisciplinaryAction", action.Id, action.WorkflowInstanceId!.Value,
            WorkflowApprovalOutcomes.Approved, Tenant, Authority,
            decisions: [new(1, "compliance", Creator, "Approved", DateTime.UtcNow), TwoSteps[1]]));

        Assert.Empty(published);
        db.ChangeTracker.Clear();
        var refused = await db.DisciplinaryActions.SingleAsync();
        Assert.Equal(DisciplinaryApprovalStatus.Rejected, refused.ApprovalStatus); // back to its authors, never published
        Assert.Equal(DisciplinaryApprovalService.RefusedApprovalReason, refused.RejectionReason);
    }

    [Fact]
    public async Task A_rejection_keeps_the_reason_and_reaches_no_payroll()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db, DisciplinaryApprovalStatus.PendingApproval);
        var (service, published) = Service(db);

        await Consume(service, FakeWorkflowApprovalClient.Outcome("DisciplinaryAction", action.Id, action.WorkflowInstanceId!.Value,
            WorkflowApprovalOutcomes.Rejected, Tenant, Authority, "لا يكفي الدليل", [TwoSteps[0], new(2, "hr-management-approver", Authority, "Rejected", DateTime.UtcNow)]));

        Assert.Empty(published);
        db.ChangeTracker.Clear();
        var saved = await db.DisciplinaryActions.SingleAsync();
        Assert.Equal(DisciplinaryApprovalStatus.Rejected, saved.ApprovalStatus);
        Assert.Equal("لا يكفي الدليل", saved.RejectionReason);
        Assert.True(DisciplinaryWorkflowRules.CanEdit(saved.ApprovalStatus));
    }

    [Fact]
    public async Task An_action_published_before_this_plan_is_never_published_again()
    {
        await using var db = NewDb();
        var old = await ActionAsync(db, DisciplinaryApprovalStatus.Approved, beforeEngine: true);
        var (service, published) = Service(db);

        // Even a forged pending state with a link would not publish it twice.
        old.ApprovalStatus = DisciplinaryApprovalStatus.PendingApproval;
        old.WorkflowInstanceId = Guid.NewGuid();
        await db.SaveChangesAsync();
        await Consume(service, FakeWorkflowApprovalClient.Outcome("DisciplinaryAction", old.Id, old.WorkflowInstanceId!.Value,
            WorkflowApprovalOutcomes.Approved, Tenant, Authority, decisions: TwoSteps));

        Assert.Empty(published);
        Assert.False(DisciplinaryWorkflowRules.CanSubmit(DisciplinaryApprovalStatus.Approved));
    }

    [Fact]
    public async Task A_withdrawal_returns_the_action_to_draft_and_editing_opens_again()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db, DisciplinaryApprovalStatus.PendingApproval);
        var engine = new FakeWorkflowApprovalClient();

        var result = await Service(db, engine).Service.WithdrawAsync(action, Creator, "سحب");

        Assert.True(result.Success);
        db.ChangeTracker.Clear();
        var saved = await db.DisciplinaryActions.SingleAsync();
        Assert.Equal(DisciplinaryApprovalStatus.Draft, saved.ApprovalStatus);
        Assert.True(DisciplinaryWorkflowRules.CanEdit(saved.ApprovalStatus));
    }

    [Fact]
    public async Task A_withdrawal_that_finds_the_approval_ended_publishes_through_the_same_path_once()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db, DisciplinaryApprovalStatus.PendingApproval);
        var engine = new FakeWorkflowApprovalClient
        {
            CancelResult = new WorkflowApprovalCancelResult(false, true, WorkflowApprovalOutcomes.Approved, null, null)
        };
        engine.SetStatus(action.WorkflowInstanceId!.Value, WorkflowApprovalOutcomes.Approved, "DisciplinaryAction", action.Id, decisions: TwoSteps);
        var (service, published) = Service(db, engine);

        var result = await service.WithdrawAsync(action, Creator, "سحب");

        Assert.False(result.Success);
        Assert.Equal(WorkflowApprovalOutcomes.Approved, result.FinalOutcome);
        Assert.Single(published);
        db.ChangeTracker.Clear();
        Assert.Equal(DisciplinaryApprovalStatus.Approved, (await db.DisciplinaryActions.SingleAsync()).ApprovalStatus);
    }

    [Fact]
    public async Task A_rejected_action_is_submitted_again_under_the_next_number()
    {
        await using var db = NewDb();
        var action = await ActionAsync(db, DisciplinaryApprovalStatus.Rejected);
        action.SubmissionNo = 1;
        await db.SaveChangesAsync();
        var engine = new FakeWorkflowApprovalClient();

        await Service(db, engine).Service.SubmitAsync(action, Creator, null);

        Assert.Equal($"DisciplinaryAction:{action.Id:D}:2", Assert.Single(engine.Starts).IdempotencyKey);
    }

    [Fact]
    public async Task The_sweep_store_lists_waiting_actions_and_their_tenants()
    {
        await using var db = NewDb();
        var stale = await ActionAsync(db, DisciplinaryApprovalStatus.PendingApproval);
        stale.SubmittedAt = DateTime.UtcNow.AddMinutes(-30);
        await ActionAsync(db, DisciplinaryApprovalStatus.PendingApproval);
        db.AuditLogs.Add(new HRManagement.SharedKernel.Entities.EntityAuditLog { TenantId = Tenant, EntityType = nameof(DisciplinaryAction), EntityId = stale.Id, Action = "Created" });
        await db.SaveChangesAsync();
        var (service, _) = Service(db);
        var store = new DisciplinaryApprovalStore(db, service);

        Assert.Equal([Tenant], await store.ListTenantsAsync(default));
        Assert.Equal(stale.Id, Assert.Single(await store.ListPendingAsync(DateTime.UtcNow.AddMinutes(-15), null, 10, default)).EntityId);
        Assert.Equal(AdvisoryLockKeys.DisciplinaryApprovalSweep, store.LeaderLockKey);
    }

    private static Task Consume(DisciplinaryApprovalService service, IWorkflowOutcomeDecided message)
    {
        var context = new Mock<ConsumeContext<IWorkflowOutcomeDecided>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return new DisciplinaryApprovalOutcomeConsumer(service).Consume(context.Object);
    }
}

/// <summary>PLAN-46 (section 4): only the outcome consumer sends a disciplinary action to payroll.</summary>
public sealed class Wf046DisciplinaryPublishTests
{
    [Fact]
    public void Only_the_outcome_consumer_publishes_the_action()
    {
        var services = Path.Combine(RepoPaths.Root, "src", "Services");
        var sites = Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => global::System.IO.File.ReadAllText(f).Contains("Publish<IDisciplinaryActionTaken>", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(services, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["Compliance/Compliance.Infrastructure/Consumers/DisciplinaryApprovalOutcomeConsumer.cs"], sites);
    }

    [Fact]
    public void The_controller_publishes_nothing()
    {
        var controller = global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Services", "Compliance", "Compliance.Web", "Controllers", "ComplianceExtendedControllers.cs"));
        var start = controller.IndexOf("public class DisciplinaryActionsController", StringComparison.Ordinal);
        var end = controller.IndexOf("public class AuditFindingsController", start, StringComparison.Ordinal);
        var body = controller[start..end];

        Assert.DoesNotContain("IDisciplinaryActionTaken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IPublishEndpoint", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consumer_consumes_the_outcome_and_stages_the_message_for_its_caller_to_save()
    {
        Assert.True(typeof(IConsumer<IWorkflowOutcomeDecided>).IsAssignableFrom(typeof(DisciplinaryApprovalOutcomeConsumer)));
        Assert.NotNull(typeof(DisciplinaryApprovalOutcomeConsumer).GetMethod(nameof(DisciplinaryApprovalOutcomeConsumer.StageActionTakenAsync))!
            .GetCustomAttributes(typeof(StagesOutboxMessagesAttribute), false).SingleOrDefault());
    }
}
