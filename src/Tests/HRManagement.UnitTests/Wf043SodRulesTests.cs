using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-43 (G-WF-06): the three rules, on the server and in the panel, and enforcement by default.</summary>
public sealed class Wf043SodRulesTests
{
    private static readonly Guid Requester = Guid.Parse("43000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Excluded = Guid.Parse("43000000-0000-4000-8000-0000000000a2");
    private static readonly Guid Other = Guid.Parse("43000000-0000-4000-8000-0000000000a3");
    private static readonly Guid SubjectEmployee = Guid.Parse("43000000-0000-4000-8000-0000000000a4");

    private static WorkflowSodFacts Facts(bool distinct = true, int level = 2, params WorkflowStepHistoryEntry[] history) =>
        new(Requester, [Excluded], SubjectEmployee, distinct, level, history);

    private static WorkflowStepHistoryEntry Approved(int level, Guid actor) =>
        new() { Level = level, StepName = "s", Status = WorkflowStepStatus.Approved, ActorId = actor, ActionAt = DateTime.UtcNow };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_requester_and_the_excluded_act_on_no_step(bool approving)
    {
        Assert.Equal(WorkflowSodRules.ExcludedMessage, WorkflowSodRules.Evaluate(Facts(), Requester, null, approving));
        Assert.Equal(WorkflowSodRules.ExcludedMessage, WorkflowSodRules.Evaluate(Facts(), Excluded, null, approving));
        Assert.Null(WorkflowSodRules.Evaluate(Facts(), Other, null, approving));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_subject_employee_acts_on_no_step(bool approving) =>
        Assert.Equal(WorkflowSodRules.SubjectMessage, WorkflowSodRules.Evaluate(Facts(), Other, SubjectEmployee, approving));

    [Fact]
    public void Distinct_approvers_bind_approving_only_and_only_where_the_definition_asks()
    {
        var history = Approved(1, Other);

        Assert.Equal(WorkflowSodRules.DistinctApproverMessage, WorkflowSodRules.Evaluate(Facts(history: history), Other, null, approving: true));
        Assert.Null(WorkflowSodRules.Evaluate(Facts(history: history), Other, null, approving: false));
        Assert.Null(WorkflowSodRules.Evaluate(Facts(distinct: false, history: history), Other, null, approving: true));
        Assert.Null(WorkflowSodRules.Evaluate(Facts(history: history), Guid.NewGuid(), null, approving: true));
    }

    [Fact]
    public void A_rejected_earlier_entry_is_not_an_approval()
    {
        var rejected = Approved(1, Other) with { Status = WorkflowStepStatus.Rejected };
        Assert.Null(WorkflowSodRules.DistinctApprover(Facts(history: rejected), Other));
    }

    [Fact]
    public void No_requester_means_no_rule_for_instances_started_inside_HR_Core()
    {
        var facts = new WorkflowSodFacts(null, [], null, false, 1, []);
        Assert.Null(WorkflowSodRules.Evaluate(facts, Other, SubjectEmployee, approving: true));
        Assert.Null(WorkflowSodRules.Evaluate(facts, null, null, approving: false));
    }

    [Fact]
    public void Every_type_is_enforced_by_default_except_the_recorded_exemptions()
    {
        var options = new WorkflowStepRoleOptions();

        foreach (var type in new[] { "AssetAssignment", "TransferRequest", "ServiceTransaction", "RecruitmentApplication", "AnythingNew", "RecruitmentRequisition" })
            Assert.True(options.IsEnforced(type), type);
        foreach (var type in new[] { "AssetLossReport", "assetlossreport", "SeparationRecord", "DisciplinaryAction", "CardRequest", "Ticket" })
            Assert.False(options.IsEnforced(type), type);
    }

    [Fact]
    public void The_exemptions_come_from_the_new_key_and_an_empty_list_exempts_nothing()
    {
        var bound = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflow:StepRole:ExemptEntityTypes:0"] = "TransferRequest",
            ["Workflow:EnforceStepRoleEntityTypes:0"] = "AssetLossReport"
        }).Build().GetSection(WorkflowStepRoleOptions.SectionName).Get<WorkflowStepRoleOptions>()!;

        Assert.False(bound.IsEnforced("TransferRequest"));
        Assert.True(bound.IsEnforced("AssetLossReport"));
        Assert.True(new WorkflowStepRoleOptions { StepRole = new() { ExemptEntityTypes = [] } }.IsEnforced("AssetLossReport"));
    }

    [Fact]
    public void The_old_key_draws_a_start_up_warning()
    {
        var logger = new Mock<ILogger>();
        var old = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflow:EnforceStepRoleEntityTypes:0"] = "OfferLetter"
        }).Build();
        var clean = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        Assert.True(WorkflowStepRoleOptions.WarnAboutIgnoredKey(old, logger.Object));
        Assert.False(WorkflowStepRoleOptions.WarnAboutIgnoredKey(clean, logger.Object));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Staging.json")]
    [InlineData("appsettings.Production.json")]
    public void Each_settings_file_holds_exactly_the_recorded_exemptions(string file)
    {
        var root = HRManagement.TestSupport.RepoPaths.Root;
        using var settings = JsonDocument.Parse(global::System.IO.File.ReadAllText(Path.Combine(root, "src", "Services", "HRCore", "HR.Core.Web", file)));
        var workflow = settings.RootElement.GetProperty("Workflow");

        Assert.False(workflow.TryGetProperty("EnforceStepRoleEntityTypes", out _));
        Assert.False(workflow.GetProperty("AdminBypass").GetBoolean());
        Assert.Equal(
            WorkflowStepRoleOptions.DefaultExemptEntityTypes.Order(StringComparer.Ordinal),
            workflow.GetProperty("StepRole").GetProperty("ExemptEntityTypes").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_exemption_is_registered_with_an_owner_a_reason_and_a_date()
    {
        var root = HRManagement.TestSupport.RepoPaths.Root;
        using var registry = JsonDocument.Parse(global::System.IO.File.ReadAllText(Path.Combine(root, "docs", "qa", "workflow-step-role-exemptions.json")));
        var entries = registry.RootElement.GetProperty("exemptions").EnumerateArray().ToList();

        Assert.Equal(
            WorkflowStepRoleOptions.DefaultExemptEntityTypes.Order(StringComparer.Ordinal),
            entries.Select(e => e.GetProperty("entityType").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(entries, e =>
        {
            Assert.Matches("^PLAN-4[56]$", e.GetProperty("ownerPlan").GetString()!);
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("reason").GetString()));
            Assert.True(DateOnly.TryParseExact(e.GetProperty("reviewBy").GetString(), "yyyy-MM-dd", global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.None, out _));
        });
    }

    private static async Task<(HRCoreDbContext Db, WorkflowInstance Instance, WorkflowDefinition Definition, WorkflowApprovalEvaluator Evaluator)> CustodyAsync(
        bool adminBypass = false, Action<WorkflowInstance>? arrange = null)
    {
        var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        await WorkflowPackSeeder.SeedAsync(db);
        var definition = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Code == "ASSET-ASSIGN-APPROVAL");
        var instance = new WorkflowInstance
        {
            TenantId = TenantDefaults.DefaultTenantId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "AssetAssignment",
            EntityId = Guid.NewGuid(),
            Status = WorkflowStatus.InProgress,
            CurrentStepLevel = 1,
            InitiatedBy = Requester,
            RequestedByUserId = Requester,
            SubjectEmployeeId = SubjectEmployee,
            ExcludedApproverUserIdsJson = JsonSerializer.Serialize(new[] { Excluded })
        };
        arrange?.Invoke(instance);
        return (db, instance, definition, WorkflowApprovalEvaluator.CreateDefault(db, new WorkflowStepRoleOptions { AdminBypass = adminBypass }));
    }

    private static WorkflowActionActor Actor(Guid user, string role, Guid? employee = null) =>
        new(user, "actor", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role }, employee);

    [Fact]
    public async Task The_server_applies_role_then_the_rules_in_order()
    {
        var (db, instance, definition, evaluator) = await CustodyAsync();
        await using var _ = db;
        var core = HrManagementRoles.HrCore;

        // Role first: the requester without the role sees the role.
        Assert.Equal(
            WorkflowStageGuard.StepRoleMessage(core),
            await evaluator.GetBlockReasonAsync(instance, definition, Actor(Requester, HrManagementRoles.HrManagementApprover), WorkflowActionKind.Approve));
        Assert.Equal(WorkflowSodRules.ExcludedMessage, await evaluator.GetBlockReasonAsync(instance, definition, Actor(Requester, core), WorkflowActionKind.Approve));
        Assert.Equal(WorkflowSodRules.ExcludedMessage, await evaluator.GetBlockReasonAsync(instance, definition, Actor(Requester, core), WorkflowActionKind.Reject));
        Assert.Equal(WorkflowSodRules.ExcludedMessage, await evaluator.GetBlockReasonAsync(instance, definition, Actor(Excluded, core), WorkflowActionKind.Reject));
        Assert.Equal(WorkflowSodRules.SubjectMessage, await evaluator.GetBlockReasonAsync(instance, definition, Actor(Other, core, SubjectEmployee), WorkflowActionKind.Approve));
        Assert.Null(await evaluator.GetBlockReasonAsync(instance, definition, Actor(Other, core), WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task One_person_with_both_roles_cannot_take_both_custody_steps_but_may_reject()
    {
        var (db, instance, definition, evaluator) = await CustodyAsync(arrange: i =>
        {
            i.CurrentStepLevel = 2;
            i.StepHistoryJson = JsonSerializer.Serialize(new[] { Approved(1, Other) });
        });
        await using var _ = db;
        var actor = Actor(Other, HrManagementRoles.HrManagementApprover);

        Assert.Equal(WorkflowSodRules.DistinctApproverMessage, await evaluator.GetBlockReasonAsync(instance, definition, actor, WorkflowActionKind.Approve));
        Assert.Null(await evaluator.GetBlockReasonAsync(instance, definition, actor, WorkflowActionKind.Reject));
    }

    [Fact]
    public async Task AdminBypass_passes_the_role_and_never_the_three_rules()
    {
        var (db, instance, definition, evaluator) = await CustodyAsync(adminBypass: true);
        await using var _ = db;

        Assert.Equal(
            WorkflowSodRules.ExcludedMessage,
            await evaluator.GetBlockReasonAsync(instance, definition, Actor(Requester, HrManagementRoles.Admin), WorkflowActionKind.Approve));
        Assert.Equal(
            WorkflowSodRules.SubjectMessage,
            await evaluator.GetBlockReasonAsync(instance, definition, Actor(Other, HrManagementRoles.Admin, SubjectEmployee), WorkflowActionKind.Approve));
        Assert.Null(await evaluator.GetBlockReasonAsync(instance, definition, Actor(Other, HrManagementRoles.Admin), WorkflowActionKind.Approve));
    }

    [Theory]
    [InlineData("requester")]
    [InlineData("excluded")]
    [InlineData("subject")]
    [InlineData("free")]
    [InlineData("distinct")]
    public async Task The_panel_and_the_server_judge_alike(string who)
    {
        var distinct = who == "distinct";
        var (db, instance, definition, evaluator) = await CustodyAsync(arrange: i =>
        {
            if (!distinct)
                return;
            i.CurrentStepLevel = 2;
            i.StepHistoryJson = JsonSerializer.Serialize(new[] { Approved(1, Other) });
        });
        await using var _ = db;
        var (user, employee) = who switch
        {
            "requester" => (Requester, (Guid?)null),
            "excluded" => (Excluded, null),
            "subject" => (Other, SubjectEmployee),
            _ => (Other, null)
        };
        var role = distinct ? HrManagementRoles.HrManagementApprover : HrManagementRoles.HrCore;
        var actor = Actor(user, role, employee);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString("D")), new(ClaimTypes.Role, role) };
        if (employee is Guid id)
            claims.Add(new Claim("employee_id", id.ToString("D")));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));

        var panel = WorkflowPanelBuilder.Build(instance, definition, principal, user, new WorkflowPanelBuildOptions
        {
            IsAwaitingApproval = true,
            StepRoleOptions = evaluator.Options
        });

        var approve = await evaluator.GetBlockReasonAsync(instance, definition, actor, WorkflowActionKind.Approve);
        var reject = await evaluator.GetBlockReasonAsync(instance, definition, actor, WorkflowActionKind.Reject);
        Assert.Equal(approve is null, panel.CanApprove);
        Assert.Equal(reject is null, panel.CanReject);
        Assert.Equal(approve, panel.BlockReason);
    }

    [Fact]
    public async Task An_exempt_type_has_no_role_check_on_either_side()
    {
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await WorkflowPackSeeder.SeedAsync(db);
        var definition = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Code == "ASSET-LOSS-APPROVAL");
        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = 1,
            EntityType = "AssetLossReport",
            Status = WorkflowStatus.InProgress,
            CurrentStepLevel = 1
        };
        var options = new WorkflowStepRoleOptions();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Other.ToString("D")), new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)],
            "test", ClaimTypes.Name, ClaimTypes.Role));

        var server = await WorkflowApprovalEvaluator.CreateDefault(db, options)
            .GetBlockReasonAsync(instance, definition, Actor(Other, HrManagementRoles.HrCore), WorkflowActionKind.Approve);
        var panel = WorkflowPanelBuilder.Build(instance, definition, principal, Other, new WorkflowPanelBuildOptions { IsAwaitingApproval = true, StepRoleOptions = options });

        Assert.Null(server);
        Assert.True(panel.CanApprove);
    }
}
