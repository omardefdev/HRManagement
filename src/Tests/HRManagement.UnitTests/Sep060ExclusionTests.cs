using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-60 (P-7, AC-SEP-60c's other half): the exclusion itself. No acceptance persona holds <c>hr-contracts</c> with <c>hr-core</c>, so
/// on the stack the maker meets the step's role first (the engine checks the role before the separation-of-duties rules). Here HR.Core's
/// own evaluator meets a separation instance whose excluded list holds its creator and its submitter, each of whom does hold the step's
/// role: each is refused, at approve and at reject, on both chains — and an approver who is neither passes.
/// </summary>
public sealed class Sep060ExclusionTests
{
    private static readonly Guid Creator = Guid.Parse("60e00000-0000-4000-8000-0000000000c1");
    private static readonly Guid Submitter = Guid.Parse("60e00000-0000-4000-8000-0000000000c2");
    private static readonly Guid Reviewer = Guid.Parse("60e00000-0000-4000-8000-0000000000c3");

    private static HRCoreDbContext Db() =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<(WorkflowInstance Instance, WorkflowDefinition Definition)> AtStepOneAsync(HRCoreDbContext db, string code)
    {
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);
        var definition = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == code && d.IsPublished)
            .OrderByDescending(d => d.Version)
            .FirstAsync();
        var instance = new WorkflowInstance
        {
            TenantId = TenantDefaults.DefaultTenantId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "SeparationRecord",
            EntityId = Guid.NewGuid(),
            Status = WorkflowStatus.InProgress,
            CurrentStepLevel = 1,
            InitiatedBy = Submitter,
            RequestedByUserId = Submitter,
            SubjectEmployeeId = Guid.NewGuid(),
            ExcludedApproverUserIdsJson = JsonSerializer.Serialize(new[] { Creator, Submitter }),
            StepHistoryJson = "[]"
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return (instance, definition);
    }

    private static WorkflowActionActor HrCore(Guid user) =>
        new(user, "موظف موارد بشرية", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HrManagementRoles.HrCore }, null);

    public static TheoryData<string, Guid> Barred() => new()
    {
        { "SEPARATION-APPROVAL", Creator },
        { "SEPARATION-APPROVAL", Submitter },
        { "SEPARATION-APPROVAL-SENSITIVE", Creator },
        { "SEPARATION-APPROVAL-SENSITIVE", Submitter }
    };

    [Theory]
    [MemberData(nameof(Barred))]
    public async Task The_creator_and_the_submitter_are_refused_at_approve_and_at_reject_while_holding_the_steps_role(string code, Guid barred)
    {
        await using var db = Db();
        var (instance, definition) = await AtStepOneAsync(db, code);
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);

        var approve = await evaluator.GetDecisionAsync(instance, definition, HrCore(barred), WorkflowActionKind.Approve, 1);
        var reject = await evaluator.GetDecisionAsync(instance, definition, HrCore(barred), WorkflowActionKind.Reject);

        Assert.False(approve.Allowed);
        Assert.Equal(WorkflowSodRules.ExcludedMessage, approve.BlockReason);
        Assert.False(reject.Allowed);
        Assert.Equal(WorkflowSodRules.ExcludedMessage, reject.BlockReason);
    }

    [Theory]
    [InlineData("SEPARATION-APPROVAL")]
    [InlineData("SEPARATION-APPROVAL-SENSITIVE")]
    public async Task Another_holder_of_the_role_takes_the_step(string code)
    {
        await using var db = Db();
        var (instance, definition) = await AtStepOneAsync(db, code);
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);

        var approve = await evaluator.GetDecisionAsync(instance, definition, HrCore(Reviewer), WorkflowActionKind.Approve, 1);
        Assert.True(approve.Allowed, approve.BlockReason);
    }

    [Theory]
    [InlineData("SEPARATION-APPROVAL", new[] { "hr-core", "hr-management-approver" })]
    [InlineData("SEPARATION-APPROVAL-SENSITIVE", new[] { "hr-core", "compliance", "hr-management-approver" })]
    public async Task Both_chains_are_seeded_with_D_SEP_4s_roles_and_distinct_approvers(string code, string[] roles)
    {
        await using var db = Db();
        var (_, definition) = await AtStepOneAsync(db, code);
        Assert.Equal("SeparationRecord", definition.DomainEntityType);
        Assert.True(definition.RequireDistinctApprovers);
        Assert.Equal(WorkflowDefinitionStatus.Published, definition.Status);
        var steps = JsonSerializer.Deserialize<WorkflowStepSnapshot[]>(definition.StepsJson)!;
        Assert.Equal(roles, steps.OrderBy(s => s.Level).Select(s => s.RoleRequired));
        Assert.Empty(WorkflowDefinitionCatalog.Validate(definition));
    }
}
