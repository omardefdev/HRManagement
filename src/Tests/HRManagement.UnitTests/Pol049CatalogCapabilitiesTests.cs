using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.SharedKernel.Policy;
using static HRManagement.UnitTests.Ten048PolicyHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (G-POL-02, G-POL-11): the validator answers from the domain catalog. One test per refusal: an effect no call site of
/// the domain consumes, a SetField of a field the domain does not write, a workflow the catalog does not give to the entity
/// type of the rule's action, an action the domain does not have, and any rule of a domain out of the engine's scope. The
/// context is the real one: the seeded facts schemas, bindings and workflow definitions of an HR.Core database.
/// </summary>
public sealed class Pol049CatalogCapabilitiesTests
{
    private static string When(params string[] leaves) => "{\"all\":[" + string.Join(",", leaves) + "]}";

    private static string Leaf(string field, string value, string op = "Equals") =>
        $$"""{"field":"{{field}}","operator":"{{op}}","value":"{{value}}"}""";

    private static string Action(string value, string op = "Equals") => Leaf("action", value, op);

    private static string Trigger(string code) => $$$"""[{"effect":"TriggerWorkflow","params":{"workflowCode":"{{{code}}}"}}]""";

    private const string Deny = """[{"effect":"Deny","params":{"reason":"سبب"}}]""";
    private const string Evidence = """[{"effect":"RequireEvidence"}]""";

    private static async Task<IReadOnlyList<PolicyRuleIssue>> ValidateAsync(string domain, string when, string then, PolicyTemplateScope scope = PolicyTemplateScope.National)
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(National);
        return await PolicyRuleValidationService.ForCatalog(db).ValidateRuleAsync(domain, scope, new PolicyRuleInput("R-POL49-TEST", "اختبار", 10, when, then));
    }

    public static TheoryData<string, string, string> AcceptedRules() => new()
    {
        // Assets.Custody: each effect bound to the action whose call site consumes it.
        { PolicyDomains.AssetsCustody, When(Leaf("isPrivileged", "true"), Action("Assign")), Evidence },
        { PolicyDomains.AssetsCustody, When(Action("Assign")), Trigger("ASSET-ASSIGN-APPROVAL") },
        { PolicyDomains.AssetsCustody, When(Action("Assign,Assign", "In")), Trigger("ASSET-ASSIGN-APPROVAL") },
        { PolicyDomains.AssetsCustody, """{"not":{"not":{"field":"action","operator":"Equals","value":"Assign"}}}""", Trigger("ASSET-ASSIGN-APPROVAL") },
        { PolicyDomains.AssetsCustody, When(Action("Classify"), Leaf("value", "10000", "GreaterThan")), """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]""" },
        // RequireEvidence and Deny need no action: every call site of the domain reads a denial.
        { PolicyDomains.AssetsCustody, When(Leaf("isPrivileged", "true")), Evidence },
        { PolicyDomains.AssetsCustody, When(Action("Classify", "NotEquals")), Deny },
        { PolicyDomains.ContractsLeave, When(Leaf("isSelfApproval", "true"), Action("Approve")), Deny },
        { PolicyDomains.ContractsLeave, When(Action("Create,Update", "In")), Deny },
        { PolicyDomains.IDCardLifecycle, When(Action("Process")), Deny },
        { PolicyDomains.CoreRecruitment, When(Action("Issue")), Trigger("RECRUITMENT-OFFER-APPROVAL") },
        { PolicyDomains.CoreRecruitment, When(Action("Issue"), Leaf("isExceptionOffer", "true")), Trigger("RECRUITMENT-OFFER-OVERRIDE-APPROVAL") },
        { PolicyDomains.CoreRecruitment, When(Action("SubmitForApproval")), Trigger("RECRUITMENT-REQUISITION-APPROVAL") },
        { PolicyDomains.CoreRecruitment, When(Action("SubmitCommittee")), Trigger("RECRUITMENT-SELECTION-COMMITTEE") },
        { PolicyDomains.CoreRecruitment, When(Action("FileAppeal")), Trigger("RECRUITMENT-APPEAL-REVIEW") },
        { PolicyDomains.CoreRecruitment, When(Action("Hire"), Leaf("hasAcceptedOffer", "false")), Deny },
        { PolicyDomains.CoreEstablishment, When(Action("SubmitForApproval"), Leaf("hasOrgScope", "true")), Deny }
    };

    [Theory, MemberData(nameof(AcceptedRules))]
    public async Task A_rule_inside_its_domains_descriptor_is_accepted(string domain, string when, string then) =>
        Assert.Empty(await ValidateAsync(domain, when, then));

    public static TheoryData<string, string, string, string, string> RefusedRules() => new()
    {
        // 1. An effect no call site of the domain consumes (the plan's dead effects).
        { PolicyDomains.ContractsLeave, When(Action("Create")), """[{"effect":"RequireDualApproval"}]""", "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.ContractsLeave, When(Leaf("durationDays", "14", "GreaterThan")), """[{"effect":"RequireApproval"}]""", "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.ContractsLeave, When(Action("Create")), Evidence, "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.AssetsCustody, When(Action("Assign")), """[{"effect":"RequireOriginReceipt"}]""", "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.AssetsCustody, When(Action("Assign")), """[{"effect":"BlockUntilChecklist"}]""", "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.IDCardLifecycle, When(Action("Process")), """[{"effect":"RequirePrivacyMask"}]""", "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.CoreRecruitment, When(Action("Hire")), Evidence, "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.CoreEstablishment, When(Action("SubmitForApproval")), """[{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]""", "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        // 2. A SetField of a field the domain does not write.
        { PolicyDomains.AssetsCustody, When(Action("Classify")), """[{"effect":"SetField","params":{"field":"value","value":"1"}}]""", "THEN_SETFIELD_NOT_ALLOWED", "$.then[0].params.field" },
        { PolicyDomains.AssetsCustody, When(Action("Classify")), """[{"effect":"SetField","params":{"field":"IsPrivileged","value":"true"}}]""", "THEN_SETFIELD_NOT_ALLOWED", "$.then[0].params.field" },
        { PolicyDomains.ContractsLeave, When(Action("Create")), """[{"effect":"SetField","params":{"field":"durationDays","value":"1"}}]""", "THEN_SETFIELD_NOT_ALLOWED", "$.then[0].params.field" },
        // 3. A workflow the catalog does not give to the entity type of the rule's action (G-POL-11, the R-REC-HIRE pattern).
        { PolicyDomains.IDCardLifecycle, When(Action("Process")), Trigger("ID-CARD-ISSUANCE"), "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect" },
        { PolicyDomains.CoreRecruitment, When(Action("Hire")), Trigger("RECRUITMENT-OFFER-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.CoreRecruitment, When(Action("SubmitForApproval")), Trigger("RECRUITMENT-OFFER-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.CoreRecruitment, When(Action("Issue")), Trigger("RECRUITMENT-REQUISITION-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        // The entity type's own workflow at an action that does not start it: publishing a requisition takes no instance, so one
        // started there would wait in an inbox for nobody (the review of PLAN-49, finding 2).
        { PolicyDomains.CoreRecruitment, When(Action("Publish")), Trigger("RECRUITMENT-REQUISITION-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.CoreRecruitment, When(Action("PublishInternal")), Trigger("RECRUITMENT-REQUISITION-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.CoreRecruitment, When(Action("SubmitForApproval,Publish", "In")), Trigger("RECRUITMENT-REQUISITION-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        // No condition on the action: the rule would also fire on Classify and Create, whose entity type has no workflow.
        { PolicyDomains.AssetsCustody, When(Leaf("isPrivileged", "true")), Trigger("ASSET-ASSIGN-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.AssetsCustody, When(Leaf("value", "10000", "GreaterThan")), Trigger("ASSET-ASSIGN-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.AssetsCustody, When(Action("Create")), Trigger("ASSET-ASSIGN-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.AssetsCustody, When(Action("Classify", "NotEquals")), Trigger("ASSET-ASSIGN-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.AssetsCustody, When(Action("Assign,Create", "In")), Trigger("ASSET-ASSIGN-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.AssetsCustody, """{"any":[{"field":"action","operator":"Equals","value":"Assign"},{"field":"isPrivileged","operator":"Equals","value":"true"}]}""", Trigger("ASSET-ASSIGN-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        { PolicyDomains.AssetsCustody, """{"not":{"field":"action","operator":"Equals","value":"Assign"}}""", Trigger("ASSET-ASSIGN-APPROVAL"), "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode" },
        // 4. An action the domain does not have, or a comparison that does not say which actions it means.
        { PolicyDomains.AssetsCustody, When(Action("Dispose")), Deny, "WHEN_ACTION_UNKNOWN", "$.when.all[0].value" },
        { PolicyDomains.AssetsCustody, When(Action("assign")), Deny, "WHEN_ACTION_UNKNOWN", "$.when.all[0].value" },
        { PolicyDomains.AssetsCustody, When(Action("Assign,MarkLost", "In")), Deny, "WHEN_ACTION_UNKNOWN", "$.when.all[0].value" },
        { PolicyDomains.CoreRecruitment, When(Action("Assign")), Deny, "WHEN_ACTION_UNKNOWN", "$.when.all[0].value" },
        { PolicyDomains.ContractsLeave, When(Action("Cancel")), Deny, "WHEN_ACTION_UNKNOWN", "$.when.all[0].value" },
        { PolicyDomains.AssetsCustody, When(Action("Ass", "Contains")), Deny, "WHEN_ACTION_OPERATOR", "$.when.all[0].operator" },
        // 5. Any rule of a domain the catalog puts out of scope.
        { PolicyDomains.ITTickets, When(Action("Open")), Deny, "DOMAIN_OUT_OF_SCOPE", "$.domain" },
        { PolicyDomains.ArchiveRetention, When(Leaf("legalHold", "true")), Deny, "DOMAIN_OUT_OF_SCOPE", "$.domain" },
        { PolicyDomains.AnalyticsEtl, When(Leaf("environment", "Production")), """[{"effect":"RequireApproval"}]""", "DOMAIN_OUT_OF_SCOPE", "$.domain" },
        { "Unknown.Domain", When(Action("Open")), Deny, "DOMAIN_OUT_OF_SCOPE", "$.domain" }
    };

    [Theory, MemberData(nameof(RefusedRules))]
    public async Task A_rule_outside_its_domains_descriptor_is_refused_with_an_arabic_message(string domain, string when, string then, string code, string path)
    {
        var issues = await ValidateAsync(domain, when, then);

        var issue = Assert.Single(issues, i => i.Code == code);
        Assert.Equal(path, issue.JsonPath);
        Assert.Matches("[؀-ۿ]", issue.MessageAr);
    }

    [Fact]
    public async Task A_rule_of_an_out_of_scope_domain_gets_the_catalog_message_and_nothing_else()
    {
        // The rule is malformed in every other way too: only the scope is reported.
        var issues = await ValidateAsync(PolicyDomains.PayrollPeriod, "{}", "[]");

        var issue = Assert.Single(issues);
        Assert.Equal(PolicyRuleValidator.OutOfScopeCode, issue.Code);
        Assert.StartsWith(PolicyDomainCatalog.OutOfScopeMessageAr, issue.MessageAr, StringComparison.Ordinal);
        Assert.Contains(PolicyDomains.PayrollPeriod, issue.MessageAr);
    }

    [Fact]
    public async Task The_message_of_a_refused_workflow_names_the_action_and_the_entity_type()
    {
        var issue = Assert.Single(await ValidateAsync(PolicyDomains.CoreRecruitment, When(Action("Hire")), Trigger("RECRUITMENT-OFFER-APPROVAL")));

        Assert.Equal("THEN_WORKFLOW_NOT_ALLOWED", issue.Code);
        Assert.Contains("Hire (RecruitmentApplication)", issue.MessageAr);
        Assert.Contains("RECRUITMENT-OFFER-APPROVAL", issue.MessageAr);
    }

    [Fact]
    public async Task An_agency_rule_must_pass_the_catalog_and_the_ceiling()
    {
        // The ceiling alone lets a stricter effect through; the catalog refuses the ones no call site consumes.
        var dual = await ValidateAsync(PolicyDomains.AssetsCustody, When(Action("Assign")), """[{"effect":"RequireDualApproval"}]""", PolicyTemplateScope.TenantOverride);
        Assert.Equal(["THEN_EFFECT_NOT_ALLOWED"], dual.Select(i => i.Code));

        // The catalog alone lets a workflow bound to Assign through; the ceiling refuses it where the national policy has one.
        var workflow = await ValidateAsync(PolicyDomains.AssetsCustody, When(Action("Assign")), Trigger("ASSET-ASSIGN-APPROVAL"), PolicyTemplateScope.TenantOverride);
        Assert.Equal([PolicyCeiling.TriggerWorkflowCode], workflow.Select(i => i.Code));

        // Both accept a stricter effect a call site consumes.
        Assert.Empty(await ValidateAsync(PolicyDomains.AssetsCustody, When(Action("Assign")), Evidence, PolicyTemplateScope.TenantOverride));
        Assert.Empty(await ValidateAsync(PolicyDomains.ContractsLeave, When(Action("Create")), Deny, PolicyTemplateScope.TenantOverride));

        // And an agency has no say in a domain out of the engine's scope.
        Assert.Equal([PolicyRuleValidator.OutOfScopeCode],
            (await ValidateAsync(PolicyDomains.TrainingLearning, When(Action("Complete")), Deny, PolicyTemplateScope.TenantOverride)).Select(i => i.Code));
    }

    [Fact]
    public void The_capabilities_are_the_catalog()
    {
        var capabilities = CatalogPolicyDomainCapabilities.Instance;

        foreach (var d in PolicyDomainCatalog.All)
        {
            Assert.Equal(d.IsGoverned, capabilities.IsGoverned(d.Domain));
            foreach (var effect in Enum.GetValues<PolicyEffect>())
                Assert.Equal(d.IsGoverned && d.AllowedEffects.Contains(effect), capabilities.AllowsEffect(d.Domain, effect));
            Assert.Equal(d.Actions.OrderBy(a => a, StringComparer.Ordinal), capabilities.Actions(d.Domain).OrderBy(a => a, StringComparer.Ordinal));
            foreach (var (type, actions) in d.SubjectActions)
            {
                Assert.All(actions, action => Assert.Contains(type, capabilities.SubjectTypesOf(d.Domain, action)));
                foreach (var trigger in d.WorkflowTriggers.GetValueOrDefault(type) ?? [])
                {
                    Assert.True(capabilities.AllowsWorkflowCode(d.Domain, type, trigger.WorkflowCode));
                    // A rule may name the code only at the action that starts it, and that action is one of the type's.
                    Assert.Contains(trigger.Action, actions);
                    foreach (var action in actions)
                    {
                        Assert.Equal(action == trigger.Action, capabilities.AllowsWorkflowStart(d.Domain, type, action, trigger.WorkflowCode));
                        // The engine itself starts only what HR.Core's own approval start takes; an owner's workflow starts at its owner.
                        Assert.Equal(
                            action == trigger.Action && trigger.StartedBy == PolicyWorkflowStartPath.HrCoreCoordinator,
                            d.StartsInsideHrCore(type, action, trigger.WorkflowCode));
                    }
                }

                Assert.False(capabilities.AllowsWorkflowCode(d.Domain, type, "NOT-A-WORKFLOW"));
                Assert.All(actions, action => Assert.False(capabilities.AllowsWorkflowStart(d.Domain, type, action, "NOT-A-WORKFLOW")));
            }

            Assert.All(d.SetFields, field => Assert.True(capabilities.AllowsSetField(d.Domain, field)));
            Assert.False(capabilities.AllowsSetField(d.Domain, "notAField"));
        }

        Assert.False(capabilities.IsGoverned("Unknown.Domain"));
        Assert.Empty(capabilities.Actions("Unknown.Domain"));
        Assert.False(capabilities.AllowsEffect("Unknown.Domain", PolicyEffect.Allow));
    }

    public static TheoryData<string, string[]> ActionScopes() => new()
    {
        { """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""", ["Assign", "Classify", "Create"] },
        { """{"all":[{"field":"action","operator":"Equals","value":"Assign"}]}""", ["Assign"] },
        { """{"all":[{"field":"action","operator":"Equals","value":"ASSIGN"}]}""", ["Assign"] },
        { """{"all":[{"field":"action","operator":"NotEquals","value":"Assign"}]}""", ["Classify", "Create"] },
        { """{"all":[{"field":"action","operator":"In","value":"Assign, Create"}]}""", ["Assign", "Create"] },
        { """{"all":[{"field":"action","operator":"Equals","value":"Assign"},{"field":"action","operator":"Equals","value":"Create"}]}""", [] },
        { """{"any":[{"field":"action","operator":"Equals","value":"Assign"},{"field":"action","operator":"Equals","value":"Create"}]}""", ["Assign", "Create"] },
        { """{"any":[{"field":"action","operator":"Equals","value":"Assign"},{"field":"value","operator":"GreaterThan","value":"1"}]}""", ["Assign", "Classify", "Create"] },
        { """{"not":{"field":"action","operator":"Equals","value":"Assign"}}""", ["Classify", "Create"] },
        { """{"not":{"not":{"field":"action","operator":"Equals","value":"Assign"}}}""", ["Assign"] },
        { """{"not":{"all":[{"field":"action","operator":"Equals","value":"Assign"},{"field":"isPrivileged","operator":"Equals","value":"true"}]}}""", ["Assign", "Classify", "Create"] },
        { """{"all":[{"not":{"field":"action","operator":"In","value":"Classify,Create"}},{"field":"isPrivileged","operator":"Equals","value":"true"}]}""", ["Assign"] },
        { """{"all":[{"field":"action","operator":"Equals","value":"Dispose"}]}""", [] }
    };

    [Theory, MemberData(nameof(ActionScopes))]
    public void The_action_scope_of_a_condition_leaves_out_only_the_actions_it_cannot_match(string when, string[] expected)
    {
        var custody = PolicyDomainCatalog.Find(PolicyDomains.AssetsCustody)!;
        var group = PolicyRuleJson.ParseWhenStrict(when, [])!;

        var possible = PolicyRuleActionScope.PossibleActions(group, custody.Actions);

        Assert.Equal(expected, possible.OrderBy(a => a, StringComparer.Ordinal));
        // The analysis is an over-approximation of the evaluator: where the condition holds for an action, the action is in the set.
        foreach (var action in custody.Actions)
        {
            foreach (var privileged in new[] { "true", "false" })
            {
                var facts = new Dictionary<string, string>(StringComparer.Ordinal) { ["action"] = action, ["isPrivileged"] = privileged, ["value"] = "5" };
                if (group.Evaluate(facts))
                    Assert.Contains(action, possible);
            }
        }
    }

    [Fact]
    public void A_condition_that_never_matches_has_no_action_scope()
    {
        var custody = PolicyDomainCatalog.Find(PolicyDomains.AssetsCustody)!;

        Assert.Empty(PolicyRuleActionScope.PossibleActions(PolicyConditionGroup.Never, custody.Actions));
        Assert.Empty(PolicyRuleActionScope.PossibleActions(new PolicyConditionGroup(), custody.Actions));
    }
}
