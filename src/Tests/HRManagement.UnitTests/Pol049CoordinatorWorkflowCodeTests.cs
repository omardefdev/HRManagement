using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (G-POL-09): a policy may refuse an approval, or start the workflow the service asked for; never another one. With
/// the real orchestrator over the seeded Core.Recruitment rules plus one weak rule (priority 1: every offer issue fires the
/// ordinary offer approval): an ordinary offer gets the policy's instance; an exception offer, which asks for the override
/// approval, is refused, its policy instance is cancelled and never reused, and the offer stays a draft. In Shadow the weak
/// rule changes nothing. The same check guards the appeal review, which starts outside the coordinator.
/// </summary>
public sealed class Pol049CoordinatorWorkflowCodeTests
{
    private const string WeakRuleKey = AcceptancePol49WeakOfferPatch.RuleKey;
    private const string OfferCode = RecruitmentOfferService.OfferApprovalWorkflowCode;
    private const string OverrideCode = OfferOverrideApprovalRules.WorkflowCode;

    /// <summary>
    /// The very rule the pol49 acceptance stage seeds (AC-POL-49f): valid by the catalog (an offer may trigger either offer
    /// workflow), because the validator cannot know which offer a rule will meet.
    /// </summary>
    private static readonly PolicySeedChangeSet WeakOfferRule = AcceptancePol49WeakOfferPatch.ChangeSet;

    private static readonly PolicySeedChangeSet RetireWeakOfferRule = new("POL-TEST-WEAK-OFFER-RETIRE",
    [
        new(PolicySeedOperationKind.RetireRule, PolicyDomains.CoreRecruitment, RuleKey: WeakRuleKey, ReasonAr: "انتهى الاختبار")
    ]);

    private static async Task<(HRCoreDbContext Db, OfferLetter Exception, OfferLetter Ordinary)> ArrangeAsync(bool weakRule = true)
    {
        var db = CreateContext();
        await SeedReferenceDataAsync(db);
        await PolicyEngineSeeder.SeedAsync(db);
        if (weakRule)
            await PolicySeedChangeSetApplier.Create(db, null).ApplyAsync([WeakOfferRule]);

        var drafts = CreateOfferService(db);
        var first = await SeedRequisitionAsync(db, "P49A");
        await SeedApplicationAsync(db, first, RecruitmentApplicationStatus.Ranked, 1, "أ-1");
        var second = await SeedApplicationAsync(db, first, RecruitmentApplicationStatus.Ranked, 2, "أ-2");
        var (exception, e1) = await drafts.CreateDraftAsync(
            second.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null, CreatorId, isMeritOverride: true, meritOverrideReason: ValidOverrideReason);
        Assert.True(exception is not null, e1);

        var other = await SeedRequisitionAsync(db, "P49B");
        var top = await SeedApplicationAsync(db, other, RecruitmentApplicationStatus.Ranked, 1, "ب-1");
        var (ordinary, e2) = await drafts.CreateDraftAsync(top.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null, CreatorId);
        Assert.True(ordinary is not null, e2);
        db.ChangeTracker.Clear();
        return (db, exception!, ordinary!);
    }

    private static RecruitmentOfferService Offers(HRCoreDbContext db, string mode) =>
        CreateOfferService(db, Config(policyMode: mode), CreateOrchestrator(db));

    private static async Task<List<(Guid Id, WorkflowStatus Status, string Code)>> InstancesOfAsync(HRCoreDbContext db, Guid entityId)
    {
        db.ChangeTracker.Clear();
        var instances = await db.WorkflowInstances.AsNoTracking().Where(i => i.EntityId == entityId).OrderBy(i => i.StartedAt).ToListAsync();
        var result = new List<(Guid, WorkflowStatus, string)>();
        foreach (var instance in instances)
            result.Add((instance.Id, instance.Status, (await GetWorkflowCodeAsync(db, instance.Id))!));
        return result;
    }

    [Fact]
    public async Task In_Enforce_an_ordinary_offer_takes_the_policys_instance_of_the_workflow_it_asked_for()
    {
        var (db, _, ordinary) = await ArrangeAsync();
        await using var _d = db;

        var (success, error) = await Offers(db, "Enforce").SubmitForApprovalAsync(ordinary.Id, CreatorId, "creator");

        Assert.True(success, error);
        var instance = Assert.Single(await InstancesOfAsync(db, ordinary.Id));
        Assert.Equal(WorkflowStatus.InProgress, instance.Status);
        Assert.Equal(OfferCode, instance.Code);
        var saved = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == ordinary.Id);
        Assert.Equal(OfferStatus.PendingApproval, saved.Status);
        Assert.Equal(instance.Id, saved.WorkflowInstanceId);
        // Started by the policy (the weak rule), not a second time by the coordinator.
        var ledger = await db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync();
        Assert.Equal(instance.Id, ledger.WorkflowInstanceId);
        Assert.Contains(WeakRuleKey, ledger.DecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_Enforce_an_exception_offer_is_refused_when_the_policy_starts_a_weaker_workflow()
    {
        var (db, exception, _) = await ArrangeAsync();
        await using var _d = db;

        var (success, error) = await Offers(db, "Enforce").SubmitForApprovalAsync(exception.Id, CreatorId, "creator");

        Assert.False(success);
        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage, error);
        Assert.Equal("السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء", error);

        // The offer did not move.
        db.ChangeTracker.Clear();
        var saved = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == exception.Id);
        Assert.Equal(OfferStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);

        // The policy's instance exists, cancelled, with the reason in its history; nothing of the offer is in progress.
        var only = Assert.Single(await InstancesOfAsync(db, exception.Id));
        Assert.Equal(WorkflowStatus.Cancelled, only.Status);
        Assert.Equal(OfferCode, only.Code);
        var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == only.Id);
        Assert.NotNull(instance.CompletedAt);
        var last = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson)!.Last();
        Assert.Equal(PolicyWorkflowCodeGuard.CancelStepName, last.StepName);
        Assert.Equal(WorkflowStepStatus.Rejected, last.Status);
        Assert.Equal(CreatorId, last.ActorId);
        Assert.Contains(OfferCode, last.Comments, StringComparison.Ordinal);
        Assert.Contains(OverrideCode, last.Comments, StringComparison.Ordinal);

        // The audit and the ledger keep the refusal.
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == PolicyWorkflowCodeGuard.CancelAuditAction);
        Assert.Equal("OfferLetter", audit.EntityType);
        Assert.Equal(exception.Id, audit.EntityId);
        // The audit table is global (no tenant stamp): the guard gives the row the tenant of the instance it explains.
        Assert.Equal(instance.TenantId, audit.TenantId);
        Assert.Contains(only.Id.ToString(), audit.ChangedFields, StringComparison.OrdinalIgnoreCase);
        var ledger = await db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync();
        Assert.Equal(PolicyEnforcementModeValue.Enforce, ledger.Mode);
        Assert.Equal(only.Id, ledger.WorkflowInstanceId);
    }

    [Fact]
    public async Task A_cancelled_policy_instance_is_never_reused_and_the_right_workflow_starts_once_the_rule_is_gone()
    {
        var (db, exception, _) = await ArrangeAsync();
        await using var _d = db;
        var refused = await Offers(db, "Enforce").SubmitForApprovalAsync(exception.Id, CreatorId, "creator");
        Assert.False(refused.Success);

        // Submitting again while the rule stands: refused again, with a second cancelled instance and still none in progress.
        db.ChangeTracker.Clear();
        var again = await Offers(db, "Enforce").SubmitForApprovalAsync(exception.Id, CreatorId, "creator");
        Assert.False(again.Success);
        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage, again.Error);
        Assert.All(await InstancesOfAsync(db, exception.Id), i => Assert.Equal(WorkflowStatus.Cancelled, i.Status));
        Assert.Equal(2, (await InstancesOfAsync(db, exception.Id)).Count);

        // Without the rule the seeded rules choose the override workflow, the one the service asks for.
        db.ChangeTracker.Clear();
        await PolicySeedChangeSetApplier.Create(db, null).ApplyAsync([RetireWeakOfferRule]);
        db.ChangeTracker.Clear();
        var (success, error) = await Offers(db, "Enforce").SubmitForApprovalAsync(exception.Id, CreatorId, "creator");

        Assert.True(success, error);
        var instances = await InstancesOfAsync(db, exception.Id);
        Assert.Equal(3, instances.Count);
        var running = Assert.Single(instances, i => i.Status == WorkflowStatus.InProgress);
        Assert.Equal(OverrideCode, running.Code);
        var saved = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == exception.Id);
        Assert.Equal(OfferStatus.PendingApproval, saved.Status);
        Assert.Equal(running.Id, saved.WorkflowInstanceId);
    }

    [Fact]
    public async Task In_Shadow_the_weak_rule_changes_nothing_and_the_ledger_shows_what_it_would_have_started()
    {
        var (db, exception, ordinary) = await ArrangeAsync();
        await using var _d = db;
        var offers = Offers(db, "Shadow");

        var first = await offers.SubmitForApprovalAsync(exception.Id, CreatorId, "creator");
        var second = await offers.SubmitForApprovalAsync(ordinary.Id, CreatorId, "creator");

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        // The service's own choice starts, for each offer.
        Assert.Equal(OverrideCode, Assert.Single(await InstancesOfAsync(db, exception.Id)).Code);
        Assert.Equal(OfferCode, Assert.Single(await InstancesOfAsync(db, ordinary.Id)).Code);
        Assert.False(await db.WorkflowInstances.AnyAsync(i => i.Status == WorkflowStatus.Cancelled));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == PolicyWorkflowCodeGuard.CancelAuditAction));

        // Shadow starts nothing itself; its decision for the exception offer names the weaker code, which is the difference a
        // Shadow report shows before anybody thinks of Enforce.
        var ledger = await db.PolicyEnforcementLedgers.AsNoTracking().Where(l => l.EntityId == exception.Id).SingleAsync();
        Assert.Equal(PolicyEnforcementModeValue.Shadow, ledger.Mode);
        Assert.Null(ledger.WorkflowInstanceId);
        var decision = JsonSerializer.Deserialize<PolicyDecision>(ledger.DecisionJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(OfferCode, decision.GetWorkflowCode());
    }

    [Fact]
    public async Task Without_the_weak_rule_Enforce_starts_the_requested_workflow_for_both_offers()
    {
        // The regression: the seeded rules alone, which pick the override workflow for an exception offer.
        var (db, exception, ordinary) = await ArrangeAsync(weakRule: false);
        await using var _d = db;
        var offers = Offers(db, "Enforce");

        Assert.True((await offers.SubmitForApprovalAsync(exception.Id, CreatorId, "creator")).Success);
        Assert.True((await offers.SubmitForApprovalAsync(ordinary.Id, CreatorId, "creator")).Success);

        Assert.Equal(OverrideCode, Assert.Single(await InstancesOfAsync(db, exception.Id)).Code);
        Assert.Equal(OfferCode, Assert.Single(await InstancesOfAsync(db, ordinary.Id)).Code);
        Assert.False(await db.WorkflowInstances.AnyAsync(i => i.Status == WorkflowStatus.Cancelled));
    }

    [Fact]
    public async Task A_policy_answer_that_names_an_instance_nobody_can_read_is_refused()
    {
        var (db, _, ordinary) = await ArrangeAsync(weakRule: false);
        await using var _d = db;
        var orchestrator = new Mock<IPolicyOrchestrator>();
        orchestrator.Setup(o => o.EnforceAsync(It.IsAny<PolicyEnforceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyEnforceResponse { Allowed = true, WorkflowInstanceId = Guid.NewGuid() });
        var offers = CreateOfferService(db, Config(policyMode: "Enforce"), orchestrator.Object);

        var (success, error) = await offers.SubmitForApprovalAsync(ordinary.Id, CreatorId, "creator");

        Assert.False(success);
        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage, error);
        Assert.Empty(await InstancesOfAsync(db, ordinary.Id));
        Assert.Equal(OfferStatus.Draft, (await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == ordinary.Id)).Status);
    }

    [Fact]
    public async Task The_guard_compares_the_code_of_the_instance_and_cancels_only_another_workflow()
    {
        var db = CreateContext();
        await using var _d = db;
        await SeedReferenceDataAsync(db);
        var starter = CreateStarter(db);
        var entity = Guid.NewGuid();
        var instanceId = await starter.StartByWorkflowCodeAsync(OfferCode, "OfferLetter", entity, null, CreatorId, "creator");
        Assert.NotNull(instanceId);

        // The same code, in any case: taken, untouched.
        Assert.Null(await PolicyWorkflowCodeGuard.RefuseOtherWorkflowAsync(db, instanceId!.Value, OfferCode.ToLowerInvariant(), CreatorId, "creator", default));
        db.ChangeTracker.Clear();
        Assert.Equal(WorkflowStatus.InProgress, (await db.WorkflowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.False(await db.AuditLogs.AnyAsync());

        // Another code: refused and cancelled.
        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage,
            await PolicyWorkflowCodeGuard.RefuseOtherWorkflowAsync(db, instanceId.Value, OverrideCode, CreatorId, "creator", default));
        db.ChangeTracker.Clear();
        Assert.Equal(WorkflowStatus.Cancelled, (await db.WorkflowInstances.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task The_appeal_review_which_starts_outside_the_coordinator_refuses_another_workflow_too()
    {
        await using var db = CreateContext();
        var (_, application) = await Hcm028AppealTestSupport.SeedRejectedAsync(db, "P49");
        var starter = CreateStarter(db);
        // A policy answer that carries an instance of another workflow for an appeal. Since the engine starts only what the catalog
        // gives an action, no rule can produce it any more; a binding pinned by hand to another definition still could, and the
        // guard trusts neither.
        var other = await db.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.Code == OfferCode && d.IsPublished);
        var orchestrator = new Mock<IPolicyOrchestrator>();
        orchestrator.Setup(o => o.EnforceAsync(It.IsAny<PolicyEnforceRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (PolicyEnforceRequest request, CancellationToken ct) =>
            {
                var started = new HR.Core.Domain.Entities.Workflow.WorkflowInstance
                {
                    DefinitionId = other.Id,
                    DefinitionVersion = other.Version,
                    EntityType = request.SubjectType,
                    EntityId = request.EntityId!.Value,
                    InitiatedBy = request.ActorUserId
                };
                db.WorkflowInstances.Add(started);
                await db.SaveChangesAsync(ct);
                return new PolicyEnforceResponse { Allowed = true, WorkflowInstanceId = started.Id };
            });
        var service = Hcm028AppealTestSupport.CreateService(db, Config(policyMode: "Enforce"), starter: starter, orchestrator: orchestrator.Object);
        var appeal = await Hcm028AppealTestSupport.FileAsync(service, application);
        var reviewer = await Hcm028TestSupport.SeedEmployeeAsync(db, "مراجع");

        var (success, error) = await service.AssignReviewerAsync(appeal.Id, reviewer.Id, Hcm028AppealTestSupport.HrUserId);

        Assert.False(success);
        Assert.Equal(PolicyWorkflowCodeGuard.RefusedMessage, error);
        var instance = Assert.Single(await InstancesOfAsync(db, appeal.Id));
        Assert.Equal(WorkflowStatus.Cancelled, instance.Status);
        Assert.Equal(OfferCode, instance.Code);
        Assert.Null((await Hcm028AppealTestSupport.ReloadAsync(db, appeal.Id)).WorkflowInstanceId);
    }

    [Fact]
    public async Task A_registered_external_type_never_gets_an_instance_from_the_policy_so_the_check_never_meets_one()
    {
        // PLAN-43: the approval of a type another service owns starts through ExternalApprovalService, which asks no policy and
        // does not go through the coordinator. In Enforce the engine keeps the TriggerWorkflow effect and starts nothing.
        await using var h = await Ten048PolicyHarness.CreateAsync();
        await using var db = h.Session(Ten048PolicyHarness.National);
        var starter = new Mock<IWorkflowStarter>(MockBehavior.Strict);
        var orchestrator = new PolicyOrchestrator(db, starter.Object,
            new PolicyPublishedSnapshotCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PolicyOrchestrator>.Instance);
        Assert.True(ExternalApprovalTypes.IsRegistered("AssetAssignment"));

        var response = await orchestrator.EnforceAsync(new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            TenantId = Ten048PolicyHarness.National,
            SubjectType = "AssetAssignment",
            Action = "Assign",
            EntityId = Guid.NewGuid(),
            Mode = PolicyEnforcementMode.Enforce,
            Facts = Ten048PolicyHarness.PrivilegedFacts()
        });

        Assert.Contains("TriggerWorkflow", response.Effects);
        Assert.Null(response.WorkflowInstanceId);
        // What stops it since PLAN-49 is the catalog: the handover's trigger is the owner's (the approval contract), so the engine
        // does not reach its own stop for registered types (PLAN-43), which stays behind it. Pol049PolicyDomainCatalogTests keeps
        // every registered type out of the engine's own starts.
        Assert.Contains(PolicyWorkflowTriggerAllowlist.NotAllowedExplanation, response.Explanations);
        starter.VerifyNoOtherCalls();

        // The service that starts those types takes neither the coordinator nor the policy engine: it cannot be handed a policy
        // instance at all (it borrows one message constant of the coordinator, nothing more).
        var dependencies = typeof(ExternalApprovalService).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();
        Assert.NotEmpty(dependencies);
        Assert.DoesNotContain(typeof(ApprovalStartCoordinator), dependencies);
        Assert.DoesNotContain(typeof(IPolicyOrchestrator), dependencies);
        var external = global::System.IO.File.ReadAllText(RepoPaths.Resolve(
            "src", "Services", "HRCore", "HR.Core.Infrastructure", "Services", "Workflow", "External", "ExternalApprovalService.cs"));
        Assert.DoesNotContain("IPolicyOrchestrator", external, StringComparison.Ordinal);
        Assert.DoesNotContain("policyResult", external, StringComparison.Ordinal);
    }

    /// <summary>
    /// The review of PLAN-49 (finding 2): a requisition owns its approval workflow, but only its submission starts it. A rule that
    /// fires it when the requisition is published is refused by the validator; one that is in the data all the same (written
    /// before the validator, or by hand) starts nothing in Enforce, because the publish call site takes no instance: it would
    /// wait in an inbox for nobody and block the requisition's next approval.
    /// </summary>
    [Fact]
    public async Task In_Enforce_the_engine_starts_a_workflow_only_at_the_action_the_catalog_gives_it()
    {
        var db = CreateContext();
        await using var _d = db;
        await SeedReferenceDataAsync(db);
        await PolicyEngineSeeder.SeedAsync(db);
        var requisition = await SeedRequisitionAsync(db, "P49P");

        // A version nobody validated: the effective rules, and the requisition approval fired on Publish at the first priority.
        var template = await db.PolicyTemplates.Include(t => t.Versions).ThenInclude(v => v.Rules)
            .SingleAsync(t => t.Domain == PolicyDomains.CoreRecruitment && t.Scope == PolicyTemplateScope.National);
        var effective = HR.Core.Infrastructure.Services.Policy.PolicyDraftService.EffectiveVersion(template, DateTime.UtcNow)!;
        var rules = effective.Rules.Select(r => new PolicyRule
        {
            TenantId = r.TenantId,
            RuleKey = r.RuleKey,
            Name = r.Name,
            Priority = r.Priority,
            WhenJson = r.WhenJson,
            ThenJson = r.ThenJson
        }).ToList();
        rules.Add(new PolicyRule
        {
            TenantId = template.TenantId,
            RuleKey = "LEGACY-PUBLISH-TRIGGER",
            Name = "legacy",
            Priority = 1,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Publish"}]}""",
            ThenJson = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-REQUISITION-APPROVAL"}}]"""
        });
        await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            PolicyTemplateId = template.Id,
            TenantId = template.TenantId,
            VersionNumber = template.Versions.Max(v => v.VersionNumber) + 1,
            EffectiveFrom = DateTime.UtcNow.AddMinutes(-1)
        }, rules, PolicyVersionStatus.Published, new PolicyPublication { TenantId = template.TenantId, EvidenceRef = "test-legacy" });
        db.ChangeTracker.Clear();
        var warnings = new WarningList();
        var orchestrator = new PolicyOrchestrator(
            db,
            CreateStarter(db),
            new PolicyPublishedSnapshotCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())),
            warnings);

        PolicyEnforceRequest Ask(string action, string status)
        {
            var request = PolicyDomainFactsBuilders.ForRecruitmentRequisition(
                requisition.Id, requisition.TenantId, action, status, positionVacant: true, requisition.FundedPositionId);
            request.Mode = PolicyEnforcementMode.Enforce;
            return request;
        }

        // Publish: the rule matches and the decision names the workflow, but nothing starts, and the answer says why.
        var published = await orchestrator.EnforceAsync(Ask("Publish", "Approved"));
        Assert.True(published.Allowed);
        Assert.Contains("LEGACY-PUBLISH-TRIGGER", published.Decision.MatchedRuleIds);
        Assert.Contains(PolicyEffect.TriggerWorkflow, published.Decision.Effects);
        Assert.Null(published.WorkflowInstanceId);
        Assert.Contains(PolicyWorkflowTriggerAllowlist.NotAllowedExplanation, published.Explanations);
        Assert.Empty(await InstancesOfAsync(db, requisition.Id));
        var publishRow = await db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync(l => l.Action == "Publish");
        Assert.Null(publishRow.WorkflowInstanceId);
        // Inside HR.Core nobody reads the answer's explanation, and the ledger row shows a trigger with no instance: the log says
        // why, once, with the version the row carries and the code.
        var declined = Assert.Single(warnings.Messages);
        Assert.Contains("nothing was started", declined, StringComparison.Ordinal);
        Assert.Contains("RECRUITMENT-REQUISITION-APPROVAL", declined, StringComparison.Ordinal);
        Assert.NotNull(publishRow.PolicyVersionId);
        Assert.Contains(publishRow.PolicyVersionId.Value.ToString(), declined, StringComparison.OrdinalIgnoreCase);

        // SubmitForApproval: the action that starts the requisition's approval, by the seeded rule.
        var submitted = await orchestrator.EnforceAsync(Ask("SubmitForApproval", "Draft"));
        Assert.True(submitted.Allowed);
        Assert.DoesNotContain(PolicyWorkflowTriggerAllowlist.NotAllowedExplanation, submitted.Explanations);
        Assert.Single(warnings.Messages);
        var instance = Assert.Single(await InstancesOfAsync(db, requisition.Id));
        Assert.Equal(submitted.WorkflowInstanceId, instance.Id);
        Assert.Equal("RECRUITMENT-REQUISITION-APPROVAL", instance.Code);

        // And the validator refuses the rule itself, so no draft and no change set can publish it again.
        var issues = PolicyRuleValidator.ValidateRule(
            new PolicyRuleInput("LEGACY-PUBLISH-TRIGGER", "legacy", 1,
                """{"all":[{"field":"action","operator":"Equals","value":"Publish"}]}""",
                """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-REQUISITION-APPROVAL"}}]"""),
            await PolicyRuleValidationService.ForCatalog(db).LoadContextAsync(PolicyDomains.CoreRecruitment, PolicyTemplateScope.National));
        Assert.Contains(issues, i => i.Code == "THEN_WORKFLOW_NOT_ALLOWED");
    }

    [Fact]
    public void Every_place_that_takes_an_instance_from_a_policy_answer_in_HR_Core_goes_through_the_guard()
    {
        var root = RepoPaths.Root;
        var takers = Directory.EnumerateFiles(Path.Combine(root, "src", "Services", "HRCore"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (File: Path.GetFileName(f), Text: global::System.IO.File.ReadAllText(f)))
            .Where(f => f.Text.Contains("policyResult.WorkflowInstanceId", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(["ApprovalStartCoordinator.cs", "RecruitmentAppealService.cs"], takers.Select(t => t.File).OrderBy(f => f, StringComparer.Ordinal));
        Assert.All(takers, t => Assert.Contains("PolicyWorkflowCodeGuard.RefuseOtherWorkflowAsync(", t.Text, StringComparison.Ordinal));
    }

    /// <summary>Keeps the warnings the engine writes: inside HR.Core a declined start is told to the log and nowhere else.</summary>
    private sealed class WarningList : Microsoft.Extensions.Logging.ILogger<PolicyOrchestrator>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning) Messages.Add(formatter(state, exception));
        }
    }
}
