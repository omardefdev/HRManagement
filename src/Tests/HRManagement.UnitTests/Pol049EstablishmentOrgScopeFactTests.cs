using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (G-POL-04): the establishment submit tells the policy who really submits. The fact hasOrgScope ("the actor is limited
/// to a branch") was a constant false, so R-EST-DENY-BRANCH could never match. With the real orchestrator over the seeded
/// rules: a branch-scoped actor is refused in Enforce and only recorded in Shadow; a central one is not matched by the rule in
/// either mode. Both controllers pass HrOrgScopeVisibility.IsBranchScoped(User), after their own central-write guard.
/// </summary>
public sealed class Pol049EstablishmentOrgScopeFactTests
{
    private const string DenyReason = "تغييرات الملاك للموارد البشرية المركزية فقط";
    private static readonly Guid Actor = Guid.Parse("dddddddd-dddd-dddd-dddd-490000000001");

    private static async Task<(HRCoreDbContext Db, EstablishmentChangeLifecycleService Lifecycle, EstablishmentChangeRequest Request)> ArrangeAsync(string mode)
    {
        var db = Est024EstablishmentWorkflowUiTests.CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);
        await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db);
        db.WorkflowDefinitions.Add(Est024EstablishmentWorkflowUiTests.CreateDefinition());
        await db.SaveChangesAsync();

        var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);
        var orchestrator = new PolicyOrchestrator(
            db, starter, new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PolicyEngine:Core.Establishment"] = mode,
                ["NationalScope:IsLabScale"] = "true"
            })
            .Build();
        var lifecycle = new EstablishmentChangeLifecycleService(
            db,
            Est024EstablishmentWorkflowUiTests.CreateCodeGeneratorMock().Object,
            orchestrator,
            new EstablishmentLegalSourceValidator(new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db))),
            Est024EstablishmentWorkflowUiTests.CreateApplier(db),
            starter,
            configuration);

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-49-" + Guid.NewGuid().ToString("N")[..6],
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.Draft,
            PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"G5","OccupationalGroup":"JC-ADM"}""",
            RequestedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        db.EstablishmentChangeRequests.Add(request);
        await db.SaveChangesAsync();
        return (db, lifecycle, request);
    }

    private static Task<(bool Success, string? Error)> SubmitAsync(EstablishmentChangeLifecycleService lifecycle, Guid id, bool actorIsBranchScoped) =>
        lifecycle.SubmitForApprovalAsync(id, Syr001LegislationIds.Law50_2004, "DEC-49", Actor, "actor", actorIsBranchScoped);

    private static PolicyDecision Decision(string json) =>
        JsonSerializer.Deserialize<PolicyDecision>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public async Task In_Enforce_a_branch_scoped_actor_is_refused_by_the_rule_and_nothing_starts()
    {
        var (db, lifecycle, request) = await ArrangeAsync("Enforce");
        await using var _ = db;

        var (success, error) = await SubmitAsync(lifecycle, request.Id, actorIsBranchScoped: true);

        Assert.False(success);
        Assert.Equal(DenyReason, error);
        db.ChangeTracker.Clear();
        var saved = await db.EstablishmentChangeRequests.SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
        Assert.False(await db.WorkflowInstances.AnyAsync());
        // The refusal is recorded (the coordinator keeps the ledger row of a refused start).
        var ledger = await db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync();
        Assert.Equal(PolicyDomains.CoreEstablishment, ledger.Domain);
        Assert.Equal("SubmitForApproval", ledger.Action);
        Assert.Equal("EstablishmentChangeRequest", ledger.SubjectType);
        Assert.Equal(request.Id, ledger.EntityId);
        Assert.Null(ledger.CallerClientId);
        var decision = Decision(ledger.DecisionJson);
        Assert.False(decision.Allowed);
        Assert.Contains("R-EST-DENY-BRANCH", decision.MatchedRuleIds);
    }

    [Fact]
    public async Task In_Enforce_a_central_actor_is_not_matched_by_the_deny_rule_and_the_approval_starts()
    {
        var (db, lifecycle, request) = await ArrangeAsync("Enforce");
        await using var _ = db;

        var (success, error) = await SubmitAsync(lifecycle, request.Id, actorIsBranchScoped: false);

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        var saved = await db.EstablishmentChangeRequests.SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
        var decision = Decision((await db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync()).DecisionJson);
        Assert.True(decision.Allowed);
        Assert.DoesNotContain("R-EST-DENY-BRANCH", decision.MatchedRuleIds);
        Assert.Contains("R-EST-SUBMIT", decision.MatchedRuleIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task In_Shadow_the_decision_is_recorded_and_the_submission_goes_its_own_way(bool actorIsBranchScoped)
    {
        var (db, lifecycle, request) = await ArrangeAsync("Shadow");
        await using var _ = db;

        var (success, error) = await SubmitAsync(lifecycle, request.Id, actorIsBranchScoped);

        // Shadow changes no outcome: the approval starts for either actor.
        Assert.True(success, error);
        db.ChangeTracker.Clear();
        var saved = await db.EstablishmentChangeRequests.SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.PendingApproval, saved.Status);
        var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync();
        Assert.Equal(saved.WorkflowInstanceId, instance.Id);
        Assert.Equal(WorkflowStatus.InProgress, instance.Status);

        // The ledger says what the engine would have decided: a refusal by R-EST-DENY-BRANCH for the branch-scoped actor only.
        var ledger = await db.PolicyEnforcementLedgers.AsNoTracking().SingleAsync();
        Assert.Equal(PolicyEnforcementModeValue.Shadow, ledger.Mode);
        Assert.Null(ledger.WorkflowInstanceId);
        var decision = Decision(ledger.DecisionJson);
        Assert.Equal(!actorIsBranchScoped, decision.Allowed);
        Assert.Equal(actorIsBranchScoped, decision.MatchedRuleIds.Contains("R-EST-DENY-BRANCH"));
        if (actorIsBranchScoped)
            Assert.Equal(DenyReason, decision.DenyReason);
    }

    [Fact]
    public async Task In_Off_the_engine_is_not_asked()
    {
        var (db, lifecycle, request) = await ArrangeAsync("Off");
        await using var _ = db;

        var (success, error) = await SubmitAsync(lifecycle, request.Id, actorIsBranchScoped: true);

        Assert.True(success, error);
        Assert.False(await db.PolicyEnforcementLedgers.AnyAsync());
    }

    [Fact]
    public void Both_controllers_send_the_actors_scope_and_the_service_holds_no_constant()
    {
        var service = global::System.IO.File.ReadAllText(RepoPaths.Resolve(
            "src", "Services", "HRCore", "HR.Core.Infrastructure", "Services", "Establishment", "EstablishmentChangeLifecycleService.cs"));
        Assert.DoesNotContain("hasOrgScope: false", service, StringComparison.Ordinal);
        Assert.Contains("actorIsBranchScoped: actorIsBranchScoped", service, StringComparison.Ordinal);

        foreach (var controller in new[] { "EstablishmentChangesController.cs", "EstablishmentPlanningApiController.cs" })
        {
            var text = global::System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "Controllers", controller));
            var call = text.IndexOf(".SubmitForApprovalAsync(", StringComparison.Ordinal);
            Assert.True(call > 0, controller);
            var end = text.IndexOf(");", call, StringComparison.Ordinal);
            Assert.Contains("HrOrgScopeVisibility.IsBranchScoped(User)", text[call..end], StringComparison.Ordinal);
            // The controller's own guard stays in front of it: a branch-scoped user is turned away before the service.
            var guard = text.LastIndexOf("ValidateCentralWriteAsync(User", call, StringComparison.Ordinal);
            Assert.True(guard > 0 && call - guard < 2500, $"{controller}: the central-write guard no longer precedes the submit.");
        }

        var parameter = typeof(EstablishmentChangeLifecycleService).GetMethod(nameof(EstablishmentChangeLifecycleService.SubmitForApprovalAsync))!
            .GetParameters().Single(p => p.Name == "actorIsBranchScoped");
        Assert.Equal(typeof(bool), parameter.ParameterType);
        Assert.False(parameter.HasDefaultValue);

        // What the controllers send: a branch manager is branch-scoped, a central HR user is not.
        static System.Security.Claims.ClaimsPrincipal User(params string[] roles) =>
            new(new System.Security.Claims.ClaimsIdentity(roles.Select(r => new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, r)), "test"));
        Assert.True(HrOrgScopeVisibility.IsBranchScoped(User(HrManagementRoles.BranchManager, HrManagementRoles.HrCore)));
        Assert.False(HrOrgScopeVisibility.IsBranchScoped(User(HrManagementRoles.HrCore)));
    }
}
