using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Signatures;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-50 (§6, P-6): the delegation in the engine. On the custody definition (step 2: <c>hr-management-approver</c>, distinct approvers):
/// a delegate approves the delegated role's step; every identity rule runs as the delegate and as the principal; the principal's stored
/// branch scope bounds the delegate; an expired, revoked or wrong-type delegation, and a failing lookup, give the role's refusal; the direct
/// role wins; the history and the signature name the principal, and a direct action signs exactly what it signed before.
/// </summary>
public class Iam050EvaluatorDelegationTests
{
    private const string Approver = HrManagementRoles.HrManagementApprover;
    private const string EntityType = "AssetAssignment";
    private static readonly Guid Requester = Guid.Parse("50e00000-0000-4000-8000-000000000001");
    private static readonly Guid Principal = Guid.Parse("50e00000-0000-4000-8000-000000000002");
    private static readonly Guid PrincipalEmployee = Guid.Parse("50e00000-0000-4000-8000-0000000000e2");
    private static readonly Guid Delegate = Guid.Parse("50e00000-0000-4000-8000-000000000003");
    private static readonly Guid DelegateEmployee = Guid.Parse("50e00000-0000-4000-8000-0000000000e3");
    private static readonly Guid FirstApprover = Guid.Parse("50e00000-0000-4000-8000-000000000004");

    private static HRCoreDbContext Db() =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static WorkflowStepHistoryEntry Approved(int level, Guid actor, Guid? onBehalfOf = null) =>
        new() { Level = level, StepName = "s", Status = WorkflowStepStatus.Approved, ActorId = actor, OnBehalfOfId = onBehalfOf, ActionAt = DateTime.UtcNow };

    /// <summary>A custody instance at step 2, its step 1 approved by <see cref="FirstApprover"/>.</summary>
    private static async Task<(WorkflowInstance Instance, WorkflowDefinition Definition)> AtStepTwoAsync(
        HRCoreDbContext db, Action<WorkflowInstance>? arrange = null)
    {
        await WorkflowPackSeeder.SeedAsync(db);
        var definition = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Code == "ASSET-ASSIGN-APPROVAL");
        var instance = new WorkflowInstance
        {
            TenantId = TenantDefaults.DefaultTenantId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = EntityType,
            EntityId = Guid.NewGuid(),
            Status = WorkflowStatus.InProgress,
            CurrentStepLevel = 2,
            InitiatedBy = Requester,
            RequestedByUserId = Requester,
            StepHistoryJson = JsonSerializer.Serialize(new[] { Approved(1, FirstApprover) })
        };
        arrange?.Invoke(instance);
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return (instance, definition);
    }

    private static ActiveDelegation Delegation(
        string? entityType = EntityType, DateTime? until = null, Guid? principalScope = null, string role = Approver) =>
        new(Guid.NewGuid(), Principal, PrincipalEmployee, "الأصيل", role, entityType, until ?? DateTime.UtcNow.AddDays(2), principalScope);

    private static WorkflowActionActor DelegateActor(params ActiveDelegation[] delegations) =>
        new(Delegate, "المفوَّض", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HrManagementRoles.HrCore }, DelegateEmployee)
        {
            Delegations = delegations
        };

    private static string RoleMessage => WorkflowStageGuard.StepRoleMessage(Approver);

    [Fact]
    public async Task A_delegate_approves_the_delegated_roles_step_on_behalf_of_the_principal()
    {
        await using var db = Db();
        var (instance, definition) = await AtStepTwoAsync(db);
        var delegation = Delegation();
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);

        var approve = await evaluator.GetDecisionAsync(instance, definition, DelegateActor(delegation), WorkflowActionKind.Approve, 2);
        var reject = await evaluator.GetDecisionAsync(instance, definition, DelegateActor(delegation), WorkflowActionKind.Reject);

        Assert.True(approve.Allowed, approve.BlockReason);
        Assert.Equal(delegation, approve.OnBehalfOf);
        Assert.True(reject.Allowed, reject.BlockReason);
        Assert.Equal(delegation, reject.OnBehalfOf);
    }

    [Fact]
    public async Task Without_a_matching_delegation_the_role_refuses()
    {
        await using var db = Db();
        var (instance, definition) = await AtStepTwoAsync(db);
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);

        foreach (var actor in new[]
                 {
                     DelegateActor(),                                                  // none, or revoked (the lookup returns none)
                     DelegateActor(Delegation(until: DateTime.UtcNow.AddSeconds(-1))), // expired
                     DelegateActor(Delegation(entityType: "RecruitmentRequisition")),  // another type
                     DelegateActor(Delegation(role: HrManagementRoles.PayrollApprover)) // another role
                 })
        {
            var decision = await evaluator.GetDecisionAsync(instance, definition, actor, WorkflowActionKind.Approve, 2);
            Assert.Equal(RoleMessage, decision.BlockReason);
            Assert.Null(decision.OnBehalfOf);
        }
    }

    [Fact]
    public async Task The_delegate_never_approves_what_he_or_his_principal_requested()
    {
        await using var db = Db();
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);
        var (byPrincipal, definition) = await AtStepTwoAsync(db, i => i.RequestedByUserId = Principal);
        Assert.Equal(WorkflowSodRules.ExcludedMessage,
            (await evaluator.GetDecisionAsync(byPrincipal, definition, DelegateActor(Delegation()), WorkflowActionKind.Approve, 2)).BlockReason);
        Assert.Equal(WorkflowSodRules.ExcludedMessage,
            (await evaluator.GetDecisionAsync(byPrincipal, definition, DelegateActor(Delegation()), WorkflowActionKind.Reject)).BlockReason);

        var (byDelegate, _) = await AtStepTwoAsync(db, i => i.RequestedByUserId = Delegate);
        Assert.Equal(WorkflowSodRules.ExcludedMessage,
            (await evaluator.GetDecisionAsync(byDelegate, definition, DelegateActor(Delegation()), WorkflowActionKind.Approve, 2)).BlockReason);
    }

    [Fact]
    public async Task The_principal_is_the_subject_or_the_initiator_and_the_delegate_is_refused()
    {
        await using var db = Db();
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);
        var (aboutPrincipal, definition) = await AtStepTwoAsync(db, i => i.SubjectEmployeeId = PrincipalEmployee);
        Assert.Equal(WorkflowSodRules.SubjectMessage,
            (await evaluator.GetDecisionAsync(aboutPrincipal, definition, DelegateActor(Delegation()), WorkflowActionKind.Approve, 2)).BlockReason);

        var (initiatedByPrincipal, _) = await AtStepTwoAsync(db, i => i.InitiatedBy = Principal);
        Assert.Equal(WorkflowStageGuard.InitiatorLaterStageMessage,
            (await evaluator.GetDecisionAsync(initiatedByPrincipal, definition, DelegateActor(Delegation()), WorkflowActionKind.Approve, 2)).BlockReason);
    }

    [Fact]
    public async Task An_approval_on_behalf_counts_for_both_people_in_the_distinct_approver_rule()
    {
        await using var db = Db();
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);
        // Step 1 approved by the principal himself: his delegate may not take step 2 for him.
        var (principalTookOne, definition) = await AtStepTwoAsync(db, i =>
            i.StepHistoryJson = JsonSerializer.Serialize(new[] { Approved(1, Principal) }));
        Assert.Equal(WorkflowSodRules.DistinctApproverMessage,
            (await evaluator.GetDecisionAsync(principalTookOne, definition, DelegateActor(Delegation()), WorkflowActionKind.Approve, 2)).BlockReason);

        // Step 1 approved by someone on behalf of the principal: the principal may not take step 2 himself.
        var (onBehalfTookOne, _) = await AtStepTwoAsync(db, i =>
            i.StepHistoryJson = JsonSerializer.Serialize(new[] { Approved(1, FirstApprover, onBehalfOf: Principal) }));
        var principalHimself = new WorkflowActionActor(Principal, "الأصيل", Approver);
        Assert.Equal(WorkflowSodRules.DistinctApproverMessage,
            (await evaluator.GetDecisionAsync(onBehalfTookOne, definition, principalHimself, WorkflowActionKind.Approve, 2)).BlockReason);
    }

    [Fact]
    public async Task The_direct_role_wins_and_acts_in_its_own_name()
    {
        await using var db = Db();
        var (instance, definition) = await AtStepTwoAsync(db);
        var both = new WorkflowActionActor(Delegate, "x", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Approver }, DelegateEmployee)
        {
            Delegations = [Delegation()]
        };
        var decision = await WorkflowApprovalEvaluator.CreateDefault(db).GetDecisionAsync(instance, definition, both, WorkflowActionKind.Approve, 2);
        Assert.True(decision.Allowed);
        Assert.Null(decision.OnBehalfOf);
    }

    [Fact]
    public async Task A_service_account_never_acts_as_a_delegate()
    {
        await using var db = Db();
        var (instance, definition) = await AtStepTwoAsync(db);
        var service = DelegateActor(Delegation()) with { IsServiceAccount = true };
        Assert.Equal(WorkflowStageGuard.ServiceAccountMessage,
            (await WorkflowApprovalEvaluator.CreateDefault(db).GetDecisionAsync(instance, definition, service, WorkflowActionKind.Approve, 2)).BlockReason);
    }

    private sealed class Lookup(Func<IReadOnlyList<ActiveDelegation>> answer) : IApprovalDelegationLookup
    {
        public Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
            Task.FromResult(answer());
    }

    [Fact]
    public async Task The_evaluator_asks_its_lookup_when_the_actor_brings_none_and_a_failure_means_none()
    {
        await using var db = Db();
        var (instance, definition) = await AtStepTwoAsync(db);
        var plain = new WorkflowActionActor(Delegate, "المفوَّض", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HrManagementRoles.HrCore }, DelegateEmployee);
        var delegation = Delegation();

        var asked = await WorkflowApprovalEvaluator.CreateDefault(db, delegationLookup: new Lookup(() => [delegation]))
            .GetDecisionAsync(instance, definition, plain, WorkflowActionKind.Approve, 2);
        Assert.Equal(delegation, asked.OnBehalfOf);

        var failed = await WorkflowApprovalEvaluator.CreateDefault(db, delegationLookup: new Lookup(() => throw new InvalidOperationException("down")))
            .GetDecisionAsync(instance, definition, plain, WorkflowActionKind.Approve, 2);
        Assert.Equal(RoleMessage, failed.BlockReason);

        Assert.Equal(RoleMessage, (await WorkflowApprovalEvaluator.CreateDefault(db)
            .GetDecisionAsync(instance, definition, plain, WorkflowActionKind.Approve, 2)).BlockReason);
    }

    [Fact]
    public async Task The_delegate_of_a_branch_scoped_principal_cannot_approve_outside_that_branch()
    {
        await using var db = Db();
        var hq = Unit("HQ", null, null);
        var branch1 = Unit("B1", hq.Id, hq.MaterializedPath);
        var branch2 = Unit("B2", hq.Id, hq.MaterializedPath);
        db.OrgUnits.AddRange(hq, branch1, branch2);
        await db.SaveChangesAsync();
        var (outside, definition) = await AtStepTwoAsync(db, i => i.ScopeOrgUnitId = branch2.Id);
        var (inside, _) = await AtStepTwoAsync(db, i => i.ScopeOrgUnitId = branch1.Id);
        var scoped = Delegation(principalScope: branch1.Id);
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(db);

        // The delegate (central hr-core) is unrestricted himself; the principal's stored branch bounds him.
        Assert.Equal(WorkflowGateOrgScope.OutOfBranchMessage,
            (await evaluator.GetDecisionAsync(outside, definition, DelegateActor(scoped), WorkflowActionKind.Approve, 2)).BlockReason);
        Assert.True((await evaluator.GetDecisionAsync(inside, definition, DelegateActor(scoped), WorkflowActionKind.Approve, 2)).Allowed);
    }

    private static OrgUnit Unit(string code, Guid? parent, string? parentPath)
    {
        var unit = new OrgUnit
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Code = $"{code}-I50-{Guid.NewGuid():N}"[..14],
            NameAr = code,
            NameEn = code,
            ParentId = parent
        };
        unit.MaterializedPath = $"{parentPath ?? "/"}{unit.Id:D}/";
        return unit;
    }

    // ---- the action service, the history and the signature ----

    private sealed class CapturingSignatures : IWorkflowSignatureService
    {
        public List<(WorkflowStepHistoryEntry Entry, string? Role, Guid? Actor)> Calls { get; } = [];

        public Task<SignatureRecord> RecordStepActionAsync(
            WorkflowInstance instance, WorkflowDefinition definition, WorkflowStepHistoryEntry historyEntry, string intent,
            Guid? actorId, string? actorRole, CancellationToken cancellationToken = default)
        {
            Calls.Add((historyEntry, actorRole, actorId));
            return Task.FromResult(new SignatureRecord());
        }
    }

    private static WorkflowInstanceActionService Actions(HRCoreDbContext db, IWorkflowSignatureService signatures) =>
        new(db,
            new WorkflowEngine(),
            signatures,
            Mock.Of<MassTransit.IPublishEndpoint>(),
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentRequisitionWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.OfferLetterWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeWorkflowHandler(db, null!)); // custody never reaches it

    [Fact]
    public async Task A_rejection_on_behalf_names_the_principal_in_the_history_and_signs_with_the_delegated_role()
    {
        await using var db = Db();
        var (instance, _) = await AtStepTwoAsync(db);
        var signatures = new CapturingSignatures();
        var delegation = Delegation();

        var result = await Actions(db, signatures).RejectAsync(instance.Id, DelegateActor(delegation), "لا", targetStepLevel: 2);

        Assert.True(result.Success, result.Error);
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(result.Instance!.StepHistoryJson)!;
        var entry = history[^1];
        Assert.Equal(Delegate, entry.ActorId);
        Assert.Equal(Principal, entry.OnBehalfOfId);
        Assert.Equal("الأصيل", entry.OnBehalfOfName);
        Assert.Equal(delegation.Id, entry.DelegationId);
        var signed = Assert.Single(signatures.Calls);
        Assert.Equal(Approver, signed.Role);
        Assert.Equal(Delegate, signed.Actor);
        Assert.Equal(Principal, signed.Entry.OnBehalfOfId);
    }

    [Fact]
    public async Task A_direct_rejection_writes_none_of_the_three_fields_and_signs_as_before()
    {
        await using var db = Db();
        var (instance, _) = await AtStepTwoAsync(db);
        var signatures = new CapturingSignatures();
        var direct = new WorkflowActionActor(Delegate, "x", Approver);

        var result = await Actions(db, signatures).RejectAsync(instance.Id, direct, "لا", targetStepLevel: 2);

        Assert.True(result.Success, result.Error);
        Assert.DoesNotContain("OnBehalfOf", result.Instance!.StepHistoryJson, StringComparison.Ordinal);
        Assert.DoesNotContain("DelegationId", result.Instance.StepHistoryJson, StringComparison.Ordinal);
        Assert.Equal(Approver, Assert.Single(signatures.Calls).Role);
    }

    [Fact]
    public void Old_history_json_still_reads()
    {
        const string old = """[{"Level":1,"StepName":"s","Status":1,"ActorId":"50e00000-0000-4000-8000-000000000004","ActorName":"a","Comments":null,"ActionAt":"2026-11-01T10:00:00Z"}]""";
        var entry = Assert.Single(JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(old)!);
        Assert.Equal(FirstApprover, entry.ActorId);
        Assert.Null(entry.OnBehalfOfId);
        Assert.Null(entry.DelegationId);
        Assert.True(entry.WasActedBy(FirstApprover));
    }

    [Fact]
    public void A_direct_action_signs_exactly_the_twelve_fields_it_signed_before_and_one_on_behalf_adds_two()
    {
        var definition = new WorkflowDefinition { Code = "C", Version = 3 };
        var instance = new WorkflowInstance { EntityType = EntityType, EntityId = Guid.NewGuid() };
        var entry = Approved(2, Delegate) with { Comments = "ok" };
        var before = new
        {
            workflowInstanceId = instance.Id,
            definitionId = definition.Id,
            definitionVersion = definition.Version,
            definitionCode = definition.Code,
            entityType = instance.EntityType,
            entityId = instance.EntityId,
            stepLevel = entry.Level,
            stepName = entry.StepName,
            intent = "approve",
            actorId = (Guid?)Delegate,
            comments = entry.Comments,
            actionAt = entry.ActionAt
        };

        Assert.Equal(
            CanonicalPayloadBuilder.ComputeHash(before),
            CanonicalPayloadBuilder.ComputeHash(WorkflowSignatureService.Payload(instance, definition, entry, "approve", Delegate)));

        var onBehalf = entry with { OnBehalfOfId = Principal, DelegationId = Guid.NewGuid() };
        var json = CanonicalPayloadBuilder.Canonicalize(WorkflowSignatureService.Payload(instance, definition, onBehalf, "approve", Delegate));
        Assert.Contains("onBehalfOfUserId", json, StringComparison.Ordinal);
        Assert.Contains("delegationId", json, StringComparison.Ordinal);
        Assert.NotEqual(
            CanonicalPayloadBuilder.ComputeHash(before),
            CanonicalPayloadBuilder.ComputeHash(WorkflowSignatureService.Payload(instance, definition, onBehalf, "approve", Delegate)));
    }
}
