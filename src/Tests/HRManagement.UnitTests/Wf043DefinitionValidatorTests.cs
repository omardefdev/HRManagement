using System.Reflection;
using HR.Core.Domain.Services.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>PLAN-43 (G-WF-04): the validator and the guardrail table, row by row.</summary>
public sealed class Wf043DefinitionValidatorTests
{
    private const string Core = HrManagementRoles.HrCore;
    private const string Approver = HrManagementRoles.HrManagementApprover;

    private static WorkflowStepSnapshot Step(int level, string role, string name = "خطوة", WorkflowCondition? condition = null) =>
        new() { Level = level, Name = name, RoleRequired = role, Condition = condition };

    private static IReadOnlyList<string> Validate(
        string entityType, string code = "CODE", IReadOnlyList<string>? guardrailRoles = null, params WorkflowStepSnapshot[] steps) =>
        WorkflowDefinitionValidator.Validate(
            new WorkflowDefinitionDraft(code, "اسم", entityType, steps, guardrailRoles ?? []),
            WorkflowDefinitionCatalog.KnownRoles,
            WorkflowDefinitionCatalog.KnownEntityTypes);

    [Theory]
    [InlineData("RecruitmentRequisition", 2, Approver)]
    [InlineData("EstablishmentChangeRequest", 2, Approver)]
    [InlineData("RecruitmentCompetition", 2, Approver)]
    [InlineData("CompetitionResultVersion", 2, Approver)]
    [InlineData("OfferLetter", 2, Approver)]
    [InlineData("SelectionCommitteeSession", 3, Approver)]
    [InlineData("RecruitmentAppeal", 1, HrManagementRoles.Compliance)]
    [InlineData("AssetAssignment", 2, Approver)]
    [InlineData("AssetLossReport", 2, Approver)]
    public void Each_guardrail_row_holds_its_floor_and_its_last_role(string entityType, int minSteps, string? lastRole)
    {
        var guardrail = WorkflowGuardrails.For(entityType);
        Assert.Equal(minSteps, guardrail.MinSteps);
        Assert.Equal(lastRole, guardrail.LastStepRole);

        var enough = Enumerable.Range(1, minSteps)
            .Select(level => Step(level, level == minSteps ? lastRole ?? Core : guardrail.FirstStepRole ?? Core))
            .ToArray();
        Assert.Empty(Validate(entityType, steps: enough));

        if (minSteps > 1)
            Assert.Contains($"هذا النوع يتطلب {minSteps} خطوات على الأقل.", Validate(entityType, steps: enough[1..].Select(s => s with { Level = s.Level - 1 }).ToArray()));

        if (lastRole is not null)
        {
            var wrongLast = enough.Take(minSteps - 1).Append(Step(minSteps, HrManagementRoles.Auditor)).ToArray();
            Assert.Contains($"الخطوة الأخيرة لهذا النوع يجب أن تكون بدور «{lastRole}».", Validate(entityType, steps: wrongLast));
        }
    }

    [Fact]
    public void The_offer_override_needs_three_steps_and_the_plain_offer_two()
    {
        var two = new[] { Step(1, Core), Step(2, Approver) };
        Assert.Empty(Validate("OfferLetter", "RECRUITMENT-OFFER-APPROVAL", steps: two));
        Assert.Contains("هذا النوع يتطلب 3 خطوات على الأقل.", Validate("OfferLetter", "RECRUITMENT-OFFER-OVERRIDE-APPROVAL", steps: two));
        Assert.Empty(Validate("OfferLetter", "RECRUITMENT-OFFER-OVERRIDE-APPROVAL", steps: [.. two, Step(3, Approver)]));
    }

    [Fact]
    public void Another_known_type_needs_one_step_with_a_catalog_role()
    {
        var guardrail = WorkflowGuardrails.For("SomethingElse");
        Assert.Equal(1, guardrail.MinSteps);
        Assert.Null(guardrail.LastStepRole);
    }

    [Fact]
    public void A_required_role_of_a_guardrail_must_be_on_a_step()
    {
        var errors = new List<string>();
        var draft = new WorkflowDefinitionDraft("C", "N", "T", [Step(1, Core)], []);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Core };
        Assert.Empty(WorkflowDefinitionValidator.Validate(draft, known, new HashSet<string> { "T" }));

        errors.AddRange(WorkflowDefinitionValidator.Validate(draft with { GuardrailRoles = [HrManagementRoles.Compliance] }, known, new HashSet<string> { "T" }));
        Assert.Contains($"دور الحماية «{HrManagementRoles.Compliance}» غير موجود في كتالوج الأدوار.", errors);

        known.Add(HrManagementRoles.Compliance);
        Assert.Contains(
            $"دور الحماية «{HrManagementRoles.Compliance}» لا تحمله أي خطوة.",
            WorkflowDefinitionValidator.Validate(draft with { GuardrailRoles = [HrManagementRoles.Compliance] }, known, new HashSet<string> { "T" }));
    }

    [Theory]
    [InlineData("security")]
    [InlineData("it-support")]
    public void A_role_outside_the_catalog_is_an_error(string role) =>
        Assert.Contains($"دور الخطوة 1 «{role}» غير موجود في كتالوج الأدوار.", Validate("SeparationRecord", steps: Step(1, role)));

    [Fact]
    public void An_unknown_entity_type_is_an_error() =>
        Assert.Contains("نوع الكيان «Nothing» غير معروف.", Validate("Nothing", steps: Step(1, Core)));

    [Fact]
    public void Steps_must_exist_have_a_name_and_a_role()
    {
        Assert.Contains("لا يمكن قبول تعريف بدون خطوات.", Validate("RecruitmentAppeal"));

        var errors = Validate("RecruitmentAppeal", steps: Step(1, "", ""));
        Assert.Contains("الخطوة 1 بلا اسم.", errors);
        Assert.Contains("الخطوة 1 بلا دور.", errors);
    }

    [Theory]
    [InlineData(new[] { 1, 1 }, "مستويات الخطوات يجب أن تكون فريدة.")]
    [InlineData(new[] { 1, 3 }, "مستويات الخطوات يجب أن تكون متصلة وتبدأ من 1.")]
    [InlineData(new[] { 2, 3 }, "مستويات الخطوات يجب أن تكون متصلة وتبدأ من 1.")]
    public void Levels_are_unique_and_connected_from_one(int[] levels, string error) =>
        Assert.Contains(error, Validate("RecruitmentAppeal", steps: levels.Select(l => Step(l, HrManagementRoles.Compliance)).ToArray()));

    [Fact]
    public void A_condition_needs_a_field_and_a_known_operator()
    {
        var errors = Validate("RecruitmentAppeal", steps: Step(1, HrManagementRoles.Compliance, condition: new WorkflowCondition { Field = "", Operator = "Matches" }));
        Assert.Contains("شرط الخطوة 1 بلا حقل.", errors);
        Assert.Contains("شرط الخطوة 1 يستعمل مشغّلاً غير معروف «Matches».", errors);

        foreach (var known in WorkflowDefinitionValidator.KnownOperators)
            Assert.Empty(Validate("RecruitmentAppeal", steps: [Step(1, HrManagementRoles.Compliance, condition: new WorkflowCondition { Field = "value", Operator = known }), Step(2, HrManagementRoles.Compliance)]));
    }

    [Fact]
    public void A_conditional_step_does_not_count_towards_the_limits_and_the_last_step_has_no_condition()
    {
        var never = new WorkflowCondition { Field = "x", Operator = "Equals", Value = "never" };

        var skippedApprover = Validate("AssetAssignment", steps: [Step(1, HrManagementRoles.AdminLogistics), Step(2, Approver, condition: never)]);
        Assert.Contains("الخطوة الأخيرة لا تكون مشروطة.", skippedApprover);
        Assert.Contains("هذا النوع يتطلب 2 خطوات على الأقل.", skippedApprover);

        Assert.Contains("الخطوة الأخيرة لا تكون مشروطة.", Validate("RecruitmentAppeal", steps: Step(1, HrManagementRoles.Compliance, condition: never)));
        Assert.Empty(Validate("RecruitmentAppeal", steps: [Step(1, Core, condition: never), Step(2, HrManagementRoles.Compliance)]));
    }

    [Fact]
    public void Custody_keeps_distinct_approvers()
    {
        var draft = new WorkflowDefinitionDraft("C", "N", "AssetAssignment", [Step(1, HrManagementRoles.AdminLogistics), Step(2, Approver)], [], RequireDistinctApprovers: false);

        Assert.Contains(
            "هذا النوع يتطلب معتمداً مختلفاً لكل خطوة.",
            WorkflowDefinitionValidator.Validate(draft, WorkflowDefinitionCatalog.KnownRoles, WorkflowDefinitionCatalog.KnownEntityTypes));
        Assert.Empty(WorkflowDefinitionValidator.Validate(
            draft with { RequireDistinctApprovers = true }, WorkflowDefinitionCatalog.KnownRoles, WorkflowDefinitionCatalog.KnownEntityTypes));
    }

    [Fact]
    public void Every_error_comes_back_at_once() =>
        Assert.True(Validate("Nothing", steps: Step(1, "", "")).Count >= 3);

    [Fact]
    public void The_role_list_holds_every_role_constant()
    {
        var constants = typeof(HrManagementRoles)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(constants, HrManagementRoles.All.Order(StringComparer.Ordinal));
        Assert.Contains("workflow-admin", HrManagementRoles.All);
    }

    [Fact]
    public async Task Seeded_definitions_fail_only_for_the_known_reasons()
    {
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);

        var invalid = (await db.WorkflowDefinitions.AsNoTracking().Where(d => d.IsPublished).ToListAsync())
            .Where(d => WorkflowDefinitionCatalog.Validate(d).Count > 0)
            .Select(d => d.Code)
            .Order(StringComparer.Ordinal);

        // PLAN-46 raised the card and the discipline to their chains and retired the four orphans: every published definition is valid.
        Assert.Empty(invalid);
    }
}
