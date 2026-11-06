using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using static HRManagement.UnitTests.Hcm025ApprovalGatesTests;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-44 (G-WF-12): how a decision that lost a race is reported. The race itself needs <c>xmin</c>, so
/// <c>Wf044InstanceIntegrityPostgresTests</c> proves it on PostgreSQL; here the exception is forced.
/// </summary>
public sealed class Wf044ConflictResultTests
{
    private static readonly Guid Reviewer = Guid.Parse("44000000-0000-4000-8000-0000000000a1");

    private static WorkflowActionActor HrCore => new(Reviewer, "hr", HrManagementRoles.HrCore);

    private static WorkflowInstanceActionService Service(HRCoreDbContext db, IWorkflowSignatureService signatures) =>
        new(
            db,
            new WorkflowEngine(),
            signatures,
            Mock.Of<MassTransit.IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeWorkflowHandler(db, null!));

    private static IWorkflowSignatureService LosingSignature()
    {
        var signatures = new Mock<IWorkflowSignatureService>();
        signatures
            .Setup(s => s.RecordStepActionAsync(
                It.IsAny<WorkflowInstance>(), It.IsAny<WorkflowDefinition>(), It.IsAny<WorkflowStepHistoryEntry>(),
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("xmin moved"));
        return signatures.Object;
    }

    [Fact]
    public async Task An_approval_that_lost_the_race_gets_the_message_and_leaves_nothing_tracked()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, instance) = await fx.RequisitionAsync();

        var result = await Service(fx.Db, LosingSignature()).ApproveAsync(instance.Id, HrCore, "ok", null);

        Assert.False(result.Success);
        Assert.True(result.IsConflict);
        Assert.Equal(WorkflowConcurrency.ConflictMessage, result.Error);
        Assert.Empty(fx.Db.ChangeTracker.Entries());
        var stored = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
        Assert.Equal(1, stored.CurrentStepLevel);
        Assert.Equal("[]", stored.StepHistoryJson);
    }

    [Fact]
    public async Task A_rejection_that_lost_the_race_gets_the_message_and_changes_nothing()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, instance) = await fx.RequisitionAsync();

        var result = await Service(fx.Db, LosingSignature()).RejectAsync(instance.Id, HrCore, "no");

        Assert.True(result.IsConflict);
        var stored = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
        Assert.Equal(WorkflowStatus.InProgress, stored.Status);
        Assert.Null(stored.CompletedAt);
    }

    [Fact]
    public async Task A_page_behind_the_instance_is_a_conflict_and_reaches_no_signature()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, instance) = await fx.RequisitionAsync();
        await fx.ApprovedStepAsync(instance, Guid.NewGuid());
        var signatures = new Mock<IWorkflowSignatureService>(MockBehavior.Strict);
        var actor = new WorkflowActionActor(Reviewer, "m", HrManagementRoles.HrManagementApprover);

        var approve = await Service(fx.Db, signatures.Object).ApproveAsync(instance.Id, actor, "ok", targetStepLevel: 1);
        var reject = await Service(fx.Db, signatures.Object).RejectAsync(instance.Id, actor, "no", targetStepLevel: 1);

        Assert.True(approve.IsConflict);
        Assert.True(reject.IsConflict);
    }

    [Fact]
    public async Task A_page_on_a_finished_instance_is_a_conflict()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, instance) = await fx.RequisitionAsync();
        var tracked = await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id);
        tracked.Status = WorkflowStatus.Completed;
        await fx.Db.SaveChangesAsync();

        var result = await Service(fx.Db, Mock.Of<IWorkflowSignatureService>()).ApproveAsync(instance.Id, HrCore, "ok", targetStepLevel: 1);

        Assert.True(result.IsConflict);
    }

    [Fact]
    public async Task A_target_ahead_of_the_instance_stays_the_stage_guards_refusal()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, instance) = await fx.RequisitionAsync();

        var result = await Service(fx.Db, Mock.Of<IWorkflowSignatureService>()).ApproveAsync(instance.Id, HrCore, "ok", targetStepLevel: 2);

        Assert.False(result.IsConflict);
        Assert.Equal(WorkflowStageGuard.SkipStageMessage, result.Error);
    }

    [Fact]
    public async Task A_withdrawal_that_lost_the_race_is_a_conflict_with_the_message()
    {
        var interceptor = new LosingSave();
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options);
        await WorkflowPackSeeder.SeedAsync(db);
        var service = new ExternalApprovalService(db, new WorkflowEngine());
        var owner = new ExternalApprovalCaller(true, ServiceClientIds.AdminLogistics, Reviewer.ToString(), "الطالب");
        var started = await service.StartAsync(new WorkflowApprovalStartRequest(
            "AssetAssignment", Guid.NewGuid(), "ASSET-ASSIGN-APPROVAL", "AssetAssignment:44", [], Guid.NewGuid(),
            "تسليم عهدة", "/ui/admin-logistics/Assets/Details/1", null, null), owner);
        interceptor.Lose = true;

        var result = await service.CancelAsync(started.Instance!.Id, owner, "ألغى الطالب الإجراء");

        Assert.Equal(ExternalApprovalOutcome.Conflict, result.Outcome);
        Assert.Equal(WorkflowConcurrency.ConflictMessage, result.Error);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private sealed class LosingSave : SaveChangesInterceptor
    {
        public bool Lose { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Lose
                ? throw new DbUpdateConcurrencyException("xmin moved")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
