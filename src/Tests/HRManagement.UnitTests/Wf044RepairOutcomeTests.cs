using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm025ApprovalGatesTests;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-44 (G-WF-12): the repair's second category, a finished instance whose linked entity is still pending. It applies only an outcome that
/// slipped: it never creates or changes an instance, and it audits the operator as the actor.
/// </summary>
public sealed class Wf044RepairOutcomeTests
{
    private static readonly Guid Operator = Guid.Parse("44000000-0000-4000-8000-0000000000c1");

    private static async Task<(Guid RequisitionId, WorkflowInstance Instance)> SlippedAsync(Fx fx, WorkflowStatus status, string history = "[]")
    {
        var (requisitionId, instance) = await fx.RequisitionAsync();
        var tracked = await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id);
        tracked.Status = status;
        tracked.CompletedAt = DateTime.UtcNow;
        tracked.StepHistoryJson = history;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();
        return (requisitionId, tracked);
    }

    private static ApprovalRepairService Repair(HRCoreDbContext db) => Hcm025ApprovalRepairTests.CreateRepair(db);

    private static async Task<string> SnapshotAsync(HRCoreDbContext db)
    {
        var rows = await db.WorkflowInstances.AsNoTracking().OrderBy(i => i.Id)
            .Select(i => new { i.Id, i.Status, i.CurrentStepLevel, i.StepHistoryJson, i.CompletedAt })
            .ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(rows);
    }

    [Fact]
    public async Task A_dry_run_lists_a_slipped_outcome_and_changes_nothing()
    {
        await using var fx = await Fx.CreateAsync();
        var (requisitionId, instance) = await SlippedAsync(fx, WorkflowStatus.Completed);

        var report = await Repair(fx.Db).RunAsync(dryRun: true, Operator, "operator");

        var item = Assert.Single(report.Items, i => i.Action == ApprovalRepairService.ApplyOutcomeAction);
        Assert.Equal(ApprovalRepairService.Listed, item.Result);
        Assert.Equal(instance.Id, item.WorkflowInstanceId);
        Assert.Equal(RequisitionStatus.PendingApproval,
            (await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == requisitionId)).Status);
    }

    [Fact]
    public async Task Applying_approves_the_entity_audits_the_operator_and_leaves_the_instance_alone()
    {
        await using var fx = await Fx.CreateAsync();
        var (requisitionId, _) = await SlippedAsync(fx, WorkflowStatus.Completed);
        var before = await SnapshotAsync(fx.Db);

        var report = await Repair(fx.Db).RunAsync(dryRun: false, Operator, "operator");

        Assert.Equal(ApprovalRepairService.Repaired, Assert.Single(report.Items, i => i.Action == ApprovalRepairService.ApplyOutcomeAction).Result);
        fx.Db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Approved, (await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId)).Status);
        var audit = await fx.Db.AuditLogs.SingleAsync(a => a.Action == ApprovalRepairService.OutcomeReappliedAuditAction);
        Assert.Equal(Operator, audit.UserId);
        Assert.Equal(requisitionId, audit.EntityId);
        Assert.Equal(before, await SnapshotAsync(fx.Db));
    }

    [Fact]
    public async Task A_rejected_instance_returns_the_entity_to_draft()
    {
        await using var fx = await Fx.CreateAsync();
        var (requisitionId, _) = await SlippedAsync(fx, WorkflowStatus.Rejected);

        await Repair(fx.Db).RunAsync(dryRun: false, Operator, "operator");

        fx.Db.ChangeTracker.Clear();
        var requisition = await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId);
        Assert.Equal(RequisitionStatus.Draft, requisition.Status);
        Assert.Null(requisition.WorkflowInstanceId);
    }

    [Fact]
    public async Task A_second_run_finds_nothing()
    {
        await using var fx = await Fx.CreateAsync();
        await SlippedAsync(fx, WorkflowStatus.Completed);

        await Repair(fx.Db).RunAsync(dryRun: false, Operator, "operator");
        fx.Db.ChangeTracker.Clear();
        var again = await Repair(fx.Db).RunAsync(dryRun: false, Operator, "operator");

        Assert.DoesNotContain(again.Items, i => i.Action == ApprovalRepairService.ApplyOutcomeAction);
        Assert.Equal(1, await fx.Db.AuditLogs.CountAsync(a => a.Action == ApprovalRepairService.OutcomeReappliedAuditAction));
    }

    [Fact]
    public async Task A_completed_instance_the_entity_does_not_link_is_named_and_never_applied()
    {
        await using var fx = await Fx.CreateAsync();
        var (requisitionId, linked) = await fx.RequisitionAsync();
        var stray = new WorkflowInstance
        {
            DefinitionId = linked.DefinitionId,
            DefinitionVersion = linked.DefinitionVersion,
            EntityType = "RecruitmentRequisition",
            EntityId = requisitionId,
            Status = WorkflowStatus.Completed
        };
        fx.Db.WorkflowInstances.Add(stray);
        await fx.Db.SaveChangesAsync();

        var report = await Repair(fx.Db).RunAsync(dryRun: false, Operator, "operator");

        var item = Assert.Single(report.Items, i => i.Action == ApprovalRepairService.ApplyOutcomeAction);
        Assert.Equal(ApprovalRepairService.NotLinked, item.Result);
        Assert.Equal(ApprovalRepairService.NotLinkedMessage, item.Error);
        Assert.Equal(stray.Id, item.WorkflowInstanceId);
        fx.Db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.PendingApproval, (await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId)).Status);
    }

    [Fact]
    public async Task A_rejected_instance_behind_a_resubmission_is_ordinary_history_and_is_not_listed()
    {
        await using var fx = await Fx.CreateAsync();
        var (requisitionId, linked) = await fx.RequisitionAsync();
        fx.Db.WorkflowInstances.Add(new WorkflowInstance
        {
            DefinitionId = linked.DefinitionId,
            DefinitionVersion = linked.DefinitionVersion,
            EntityType = "RecruitmentRequisition",
            EntityId = requisitionId,
            Status = WorkflowStatus.Rejected
        });
        await fx.Db.SaveChangesAsync();

        var report = await Repair(fx.Db).RunAsync(dryRun: true, Operator, "operator");

        Assert.DoesNotContain(report.Items, i => i.Action == ApprovalRepairService.ApplyOutcomeAction);
    }

    private static async Task<Guid> SlippedAppealAsync(Fx fx, RecruitmentAppealOutcome? outcome)
    {
        var (instance, appealId) = await fx.AppealAsync(Guid.NewGuid());
        var appeal = await fx.Db.RecruitmentAppeals.SingleAsync(a => a.Id == appealId);
        appeal.WorkflowInstanceId = instance.Id;
        appeal.Outcome = outcome;
        var tracked = await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id);
        tracked.Status = WorkflowStatus.Completed;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();
        return appealId;
    }

    [Fact]
    public async Task An_upheld_objection_is_left_to_the_operator_because_the_repair_cannot_recompute_the_results()
    {
        await using var fx = await Fx.CreateAsync();
        var appealId = await SlippedAppealAsync(fx, RecruitmentAppealOutcome.ObjectionUpheld);

        var report = await Repair(fx.Db).RunAsync(dryRun: false, Operator, "operator");

        var item = Assert.Single(report.Items, i => i.EntityId == appealId && i.Action == ApprovalRepairService.ApplyOutcomeAction);
        Assert.Equal(ApprovalRepairService.Failed, item.Result);
        Assert.Equal(ApprovalRepairService.ObjectionNeedsOperatorMessage, item.Error);
        Assert.Equal(RecruitmentAppealStatus.Pending, (await fx.Db.RecruitmentAppeals.AsNoTracking().SingleAsync(a => a.Id == appealId)).Status);
        Assert.Equal(0, await fx.Db.AuditLogs.CountAsync(a => a.Action == ApprovalRepairService.OutcomeReappliedAuditAction));
    }

    [Fact]
    public async Task A_handler_that_changes_nothing_leaves_no_audit_row_and_reports_a_failure()
    {
        await using var fx = await Fx.CreateAsync();
        var appealId = await SlippedAppealAsync(fx, outcome: null);

        var report = await Repair(fx.Db).RunAsync(dryRun: false, Operator, "operator");

        var item = Assert.Single(report.Items, i => i.EntityId == appealId && i.Action == ApprovalRepairService.ApplyOutcomeAction);
        Assert.Equal(ApprovalRepairService.Failed, item.Result);
        Assert.Equal(ApprovalRepairService.OutcomeUnchangedMessage, item.Error);
        Assert.Equal(0, await fx.Db.AuditLogs.CountAsync(a => a.Action == ApprovalRepairService.OutcomeReappliedAuditAction));
    }

    [Fact]
    public void The_metric_sums_the_tenants_and_goes_back_to_zero()
    {
        var type = $"Wf044Metric-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        WorkflowIntegrityMetrics.SetUnapplied(tenantA, new Dictionary<string, int> { [type] = 2 });
        WorkflowIntegrityMetrics.SetUnapplied(tenantB, new Dictionary<string, int> { [type] = 1 });
        Assert.Equal(3, WorkflowIntegrityMetrics.UnappliedCount(type));

        WorkflowIntegrityMetrics.SetUnapplied(tenantA, new Dictionary<string, int>());
        WorkflowIntegrityMetrics.SetUnapplied(tenantB, new Dictionary<string, int>());
        Assert.Equal(0, WorkflowIntegrityMetrics.UnappliedCount(type));
    }

    [Fact]
    public async Task The_outcomes_scope_leaves_a_pending_entity_with_no_instance_to_the_operator()
    {
        await using var fx = await Fx.CreateAsync();
        var (requisitionId, _) = await fx.RequisitionAsync();
        var requisition = await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId);
        requisition.WorkflowInstanceId = null;
        await fx.Db.SaveChangesAsync();
        var instances = await fx.Db.WorkflowInstances.CountAsync();

        var report = await Repair(fx.Db).RunAsync(dryRun: false, null, "job", scope: ApprovalRepairScope.Outcomes);

        Assert.Empty(report.Items);
        Assert.Equal(instances, await fx.Db.WorkflowInstances.CountAsync());
    }
}
