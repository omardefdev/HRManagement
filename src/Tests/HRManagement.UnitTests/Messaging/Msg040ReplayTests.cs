using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Infrastructure.Messaging;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.UnitTests.Messaging;

/// <summary>
/// PLAN-40 (§6, D-40-4): the replay lists what has no ledger row, changes nothing in a dry run, publishes once and audits on
/// apply, and lists nothing afterwards.
/// </summary>
public sealed class Msg040ReplayTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static (Guid Lost, Guid Published) SeedPromotions(MovementsDbContext db)
    {
        PromotionRequest Executed() => new()
        {
            EmployeeId = Guid.NewGuid(),
            FromGrade = 5,
            ToGrade = 4,
            FromStep = 3,
            ToStep = 1,
            TransitionDecisionRef = "DEC",
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PromotionRequestStatus.Executed
        };

        var lost = Executed();
        var published = Executed();
        var approved = Executed();
        approved.Status = PromotionRequestStatus.Approved;
        db.PromotionRequests.AddRange(lost, published, approved);
        db.MovementExecutionRecords.AddRange(
            new MovementExecutionRecord { PromotionRequestId = lost.Id, EmployeeId = lost.EmployeeId, ExecutedAt = DateTime.UtcNow },
            new MovementExecutionRecord { PromotionRequestId = published.Id, EmployeeId = published.EmployeeId, ExecutedAt = DateTime.UtcNow });
        db.AuditLogs.Add(PublishedEventLedger.Entry<IPromotionExecuted>(published.Id, null));
        return (lost.Id, published.Id);
    }

    [Fact]
    public async Task Promotion_dry_run_lists_only_what_has_no_ledger_row_and_changes_nothing()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new MovementsDbContext(probe.InMemoryOptions<MovementsDbContext>());
        var (lost, _) = SeedPromotions(db);
        await db.SaveChangesAsync();
        probe.Clear();
        var audits = await db.AuditLogs.CountAsync();

        var result = await new MovementsMessagingRepair(db, probe.Publish).ReplayPromotionsAsync(dryRun: true, Actor);

        Assert.True(result.DryRun);
        Assert.Equal([lost], result.Ids);
        Assert.Empty(probe.Log);
        Assert.Equal(audits, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Promotion_apply_publishes_once_writes_the_ledger_and_audit_and_the_next_run_lists_zero()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new MovementsDbContext(probe.InMemoryOptions<MovementsDbContext>());
        var (lost, _) = SeedPromotions(db);
        await db.SaveChangesAsync();
        probe.Clear();
        var repair = new MovementsMessagingRepair(db, probe.Publish);

        var applied = await repair.ReplayPromotionsAsync(dryRun: false, Actor);

        Assert.False(applied.DryRun);
        Assert.Equal(1, applied.Count);
        Assert.Equal(["publish:IPromotionExecuted", "save"], probe.Log);
        var ledger = await db.AuditLogs.SingleAsync(a => a.EntityId == lost);
        Assert.Equal(PublishedEventLedger.Replayed, ledger.Action);
        Assert.Equal(Actor, ledger.UserId);
        var run = await db.AuditLogs.SingleAsync(a => a.EntityType == PublishedEventLedger.ReplayRunEntityType);
        Assert.Equal(Actor, run.UserId);
        Assert.Contains("\"count\":1", run.ChangedFields, StringComparison.Ordinal);

        probe.Clear();
        var second = await repair.ReplayPromotionsAsync(dryRun: true, Actor);
        Assert.Equal(0, second.Count);
        Assert.Equal(0, (await repair.ReplayPromotionsAsync(dryRun: false, Actor)).Count);
        Assert.Empty(probe.Log);
    }

    [Fact]
    public async Task Delegation_replay_lists_an_execution_without_ledger_then_nothing()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new MovementsDbContext(probe.InMemoryOptions<MovementsDbContext>());
        var delegation = new DelegationRequest
        {
            EmployeeId = Guid.NewGuid(),
            HostOrg = "Host",
            Status = DelegationRequestStatus.Active,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
        };
        db.DelegationRequests.Add(delegation);
        db.MovementExecutionRecords.Add(new MovementExecutionRecord { DelegationRequestId = delegation.Id, EmployeeId = delegation.EmployeeId, ExecutedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        probe.Clear();
        var repair = new MovementsMessagingRepair(db, probe.Publish);

        Assert.Equal([delegation.Id], (await repair.ReplayDelegationsAsync(dryRun: true, Actor)).Ids);
        Assert.Empty(probe.Log);
        Assert.Equal(1, (await repair.ReplayDelegationsAsync(dryRun: false, Actor)).Count);
        Assert.Equal(["publish:IDelegationExecuted", "save"], probe.Log);
        Assert.Equal(0, (await repair.ReplayDelegationsAsync(dryRun: true, Actor)).Count);
    }

    private static WorkflowInstance Finished(string entityType, WorkflowStatus status) => new()
    {
        DefinitionId = Guid.NewGuid(),
        EntityType = entityType,
        EntityId = Guid.NewGuid(),
        Status = status,
        CompletedAt = DateTime.UtcNow,
        ContextJson = $$"""{"assetId":"{{Guid.NewGuid()}}","employeeId":"{{Guid.NewGuid()}}"}""",
        StepHistoryJson = JsonSerializer.Serialize(new[] { new WorkflowStepHistoryEntry { Level = 1, Comments = "لا" } })
    };

    [Fact]
    public async Task Asset_replay_covers_finished_custody_workflows_only_and_runs_once()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new HRCoreDbContext(probe.InMemoryOptions<HRCoreDbContext>());
        var approved = Finished("AssetAssignment", WorkflowStatus.Completed);
        var rejected = Finished("AssetLossReport", WorkflowStatus.Rejected);
        var running = Finished("AssetAssignment", WorkflowStatus.InProgress);
        var other = Finished("OfferLetter", WorkflowStatus.Completed);
        var alreadyPublished = Finished("AssetAssignment", WorkflowStatus.Completed);
        db.WorkflowInstances.AddRange(approved, rejected, running, other, alreadyPublished);
        db.AuditLogs.Add(PublishedEventLedger.Entry<IAssetAssignmentApproved>(alreadyPublished.Id, null));
        await db.SaveChangesAsync();
        probe.Clear();
        var repair = new HrCoreMessagingRepair(db, probe.Publish, new OrgUnitIntegrationService(db, probe.Publish));

        var dry = await repair.ReplayAssetOutcomesAsync(dryRun: true, Actor);
        Assert.Equal(new[] { approved.Id, rejected.Id }.Order(), dry.Ids.Order());
        Assert.Empty(probe.Log);

        var applied = await repair.ReplayAssetOutcomesAsync(dryRun: false, Actor);
        Assert.Equal(2, applied.Count);
        Assert.Contains("publish:IAssetAssignmentApproved", probe.Log);
        Assert.Contains("publish:IAssetLossRejected", probe.Log);
        Assert.Equal("save", probe.Log[^1]);
        Assert.Equal(0, (await repair.ReplayAssetOutcomesAsync(dryRun: true, Actor)).Count);
    }

    [Fact]
    public async Task Org_unit_resync_counts_in_a_dry_run_and_publishes_every_unit_on_apply()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new HRCoreDbContext(probe.InMemoryOptions<HRCoreDbContext>());
        db.OrgUnits.AddRange(
            new OrgUnit { Code = "U1", NameAr = "وحدة 1", NameEn = "U1" },
            new OrgUnit { Code = "U2", NameAr = "وحدة 2", NameEn = "U2" });
        await db.SaveChangesAsync();
        probe.Clear();
        var repair = new HrCoreMessagingRepair(db, probe.Publish, new OrgUnitIntegrationService(db, probe.Publish));

        Assert.Equal(2, (await repair.ResyncOrgUnitsAsync(dryRun: true, Actor)).Count);
        Assert.Empty(probe.Log);

        await repair.ResyncOrgUnitsAsync(dryRun: false, Actor);
        Assert.Equal(2, probe.Log.Count(e => e == "publish:IOrgUnitChanged"));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityType == PublishedEventLedger.ReplayRunEntityType));
    }

    [Fact]
    public void The_result_carries_ids_and_counts_only()
    {
        var properties = typeof(MessagingRepairResult).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").Order();
        Assert.Equal(["Count", "DryRun", "Ids", "Kind"], properties);
    }

    [Theory]
    [InlineData(HrManagementRoles.Admin, true)]
    [InlineData(HrManagementRoles.PlatformAdmin, true)]
    [InlineData(HrManagementRoles.HrMovements, false)]
    [InlineData(HrManagementRoles.HrCore, false)]
    public async Task Only_administrators_may_replay(string role, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddHrManagementAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, HrManagementPolicies.MessagingRepair);

        Assert.Equal(allowed, result.Succeeded);
    }
}
