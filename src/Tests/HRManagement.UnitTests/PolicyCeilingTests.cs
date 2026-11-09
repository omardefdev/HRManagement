using HRManagement.SharedKernel.Policy;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§4, D-TEN-8 = D-POL-4): an agency rule never weakens a national decision. At evaluation the ceiling drops an agency's
/// SetField of a field the national policy set, an agency's TriggerWorkflow when the national policy triggered one, and every agency
/// SelectCodePolicy, with an explanation; Deny and the stricter effects pass. At publish the same rules refuse the agency rule.
/// </summary>
public class PolicyCeilingTests
{
    private static readonly PolicyConditionGroup Always = new()
    {
        All = [new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "isPrivileged", Operator = "Equals", Value = "true" } }]
    };

    private static readonly Dictionary<string, string> Facts = new(StringComparer.OrdinalIgnoreCase) { ["isPrivileged"] = "true" };

    private static PolicyRuleDefinition Rule(string id, int priority, PolicyRuleOrigin origin, PolicyEffect effect, Dictionary<string, string>? parameters = null) =>
        new()
        {
            Id = id,
            Name = id,
            Priority = priority,
            Origin = origin,
            When = Always,
            Then = [new PolicyEffectDefinition { Effect = effect, Params = parameters }]
        };

    private static PolicyVersionSnapshot Merge(IEnumerable<PolicyRuleDefinition> national, IEnumerable<PolicyRuleDefinition> agency) =>
        PolicyEvaluator.MergeNationalAndTenant(
            new PolicyVersionSnapshot { Domain = "Assets.Custody", Rules = national.ToList() },
            new PolicyVersionSnapshot { Domain = "Assets.Custody", TenantId = Guid.NewGuid(), Rules = agency.ToList() });

    [Fact]
    public void An_agency_SetField_of_a_field_the_national_policy_sets_is_dropped_with_an_explanation()
    {
        var snapshot = Merge(
            [Rule("R-CLASSIFY", 5, PolicyRuleOrigin.National, PolicyEffect.SetField, new() { ["field"] = "isPrivileged", ["value"] = "true" })],
            [Rule("A-UNPRIV", 1, PolicyRuleOrigin.National, PolicyEffect.SetField, new() { ["field"] = "isPrivileged", ["value"] = "false" })]);

        // The merge marks the override's rules as the agency's, whatever they said.
        Assert.Equal(PolicyRuleOrigin.Agency, snapshot.Rules.Single(r => r.Id == "A-UNPRIV").Origin);

        var decision = PolicyEvaluator.Evaluate(snapshot, Facts);
        Assert.Equal("true", decision.GetSetFieldValue("isPrivileged"));
        Assert.Contains(decision.Explanations, e => e.Contains(PolicyCeiling.ExplanationAr) && e.Contains("A-UNPRIV"));
    }

    [Fact]
    public void An_agency_SetField_of_another_field_passes()
    {
        var decision = PolicyEvaluator.Evaluate(Merge(
            [Rule("R-CLASSIFY", 5, PolicyRuleOrigin.National, PolicyEffect.SetField, new() { ["field"] = "isPrivileged", ["value"] = "true" })],
            [Rule("A-NOTE", 6, PolicyRuleOrigin.Agency, PolicyEffect.SetField, new() { ["field"] = "note", ["value"] = "x" })]), Facts);
        Assert.Equal("x", decision.GetSetFieldValue("note"));
        Assert.DoesNotContain(decision.Explanations, e => e.Contains(PolicyCeiling.ExplanationAr));
    }

    [Fact]
    public void An_agency_TriggerWorkflow_is_dropped_when_the_national_policy_triggers_one_even_with_a_higher_priority()
    {
        var decision = PolicyEvaluator.Evaluate(Merge(
            [Rule("R-PRIV-WF", 15, PolicyRuleOrigin.National, PolicyEffect.TriggerWorkflow, new() { ["workflowCode"] = "ASSET-ASSIGN-APPROVAL" })],
            [Rule("A-FAST-WF", 1, PolicyRuleOrigin.Agency, PolicyEffect.TriggerWorkflow, new() { ["workflowCode"] = "LIGHT-APPROVAL" })]), Facts);
        Assert.Equal("ASSET-ASSIGN-APPROVAL", decision.GetWorkflowCode());
        Assert.Contains(decision.Explanations, e => e.Contains(PolicyCeiling.ExplanationAr) && e.Contains("A-FAST-WF"));
        Assert.DoesNotContain(decision.EffectDetails, e => e.Params?.GetValueOrDefault("workflowCode") == "LIGHT-APPROVAL");
    }

    [Fact]
    public void An_agency_TriggerWorkflow_stands_where_the_national_policy_triggers_none()
    {
        var decision = PolicyEvaluator.Evaluate(Merge(
            [Rule("R-EVIDENCE", 10, PolicyRuleOrigin.National, PolicyEffect.RequireEvidence)],
            [Rule("A-WF", 20, PolicyRuleOrigin.Agency, PolicyEffect.TriggerWorkflow, new() { ["workflowCode"] = "AGENCY-REVIEW" })]), Facts);
        Assert.Equal("AGENCY-REVIEW", decision.GetWorkflowCode());
    }

    [Fact]
    public void An_agency_never_selects_a_code_policy()
    {
        var decision = PolicyEvaluator.Evaluate(Merge(
            [],
            [Rule("A-CODE", 1, PolicyRuleOrigin.Agency, PolicyEffect.SelectCodePolicy, new() { ["policyKey"] = "ASSET-PRIVILEGED" })]), Facts);
        Assert.Null(decision.GetCodePolicyKey());
        Assert.Contains(decision.Explanations, e => e.Contains(PolicyCeiling.ExplanationAr));

        var national = PolicyEvaluator.Evaluate(Merge(
            [Rule("R-PRIV-CODE", 12, PolicyRuleOrigin.National, PolicyEffect.SelectCodePolicy, new() { ["policyKey"] = "ASSET-PRIVILEGED" })], []), Facts);
        Assert.Equal("ASSET-PRIVILEGED", national.GetCodePolicyKey());
    }

    [Theory]
    [InlineData(PolicyEffect.Deny)]
    [InlineData(PolicyEffect.RequireApproval)]
    [InlineData(PolicyEffect.RequireDualApproval)]
    [InlineData(PolicyEffect.BlockUntilChecklist)]
    [InlineData(PolicyEffect.RequirePrivacyMask)]
    [InlineData(PolicyEffect.RequireOriginReceipt)]
    [InlineData(PolicyEffect.RequireEvidence)]
    public void Deny_and_the_stricter_effects_of_an_agency_pass(PolicyEffect effect)
    {
        var parameters = effect == PolicyEffect.Deny ? new Dictionary<string, string> { ["reason"] = "agency" } : null;
        var decision = PolicyEvaluator.Evaluate(Merge(
            [Rule("R-WF", 15, PolicyRuleOrigin.National, PolicyEffect.TriggerWorkflow, new() { ["workflowCode"] = "ASSET-ASSIGN-APPROVAL" })],
            [Rule("A-STRICT", 1, PolicyRuleOrigin.Agency, effect, parameters)]), Facts);
        Assert.Contains(effect, decision.Effects);
        Assert.DoesNotContain(decision.Explanations, e => e.Contains(PolicyCeiling.ExplanationAr));
        if (effect == PolicyEffect.Deny)
            Assert.False(decision.Allowed);
    }

    [Fact]
    public void An_agency_Allow_never_lifts_a_national_Deny()
    {
        var decision = PolicyEvaluator.Evaluate(Merge(
            [Rule("R-DENY", 5, PolicyRuleOrigin.National, PolicyEffect.Deny, new() { ["reason"] = "national" })],
            [Rule("A-ALLOW", 1, PolicyRuleOrigin.Agency, PolicyEffect.Allow)]), Facts);
        Assert.False(decision.Allowed);
        Assert.Equal("national", decision.DenyReason);
    }

    [Fact]
    public void An_agency_with_no_override_receives_the_national_decision_unchanged()
    {
        var national = new PolicyVersionSnapshot
        {
            Domain = "Assets.Custody",
            Rules = [Rule("R-PRIV-WF", 15, PolicyRuleOrigin.National, PolicyEffect.TriggerWorkflow, new() { ["workflowCode"] = "ASSET-ASSIGN-APPROVAL" })]
        };
        var merged = PolicyEvaluator.MergeNationalAndTenant(national, null);
        var decision = PolicyEvaluator.Evaluate(merged, Facts);
        Assert.Equal(PolicyEvaluator.Evaluate(national, Facts).Effects, decision.Effects);
        Assert.Equal("ASSET-ASSIGN-APPROVAL", decision.GetWorkflowCode());
    }

    [Fact]
    public void An_override_alone_still_counts_as_the_agencys()
    {
        var merged = PolicyEvaluator.MergeNationalAndTenant(null, new PolicyVersionSnapshot
        {
            Domain = "Assets.Custody",
            TenantId = Guid.NewGuid(),
            Rules = [Rule("A-CODE", 1, PolicyRuleOrigin.National, PolicyEffect.SelectCodePolicy, new() { ["policyKey"] = "X" })]
        });
        Assert.All(merged.Rules, r => Assert.Equal(PolicyRuleOrigin.Agency, r.Origin));
        Assert.Null(PolicyEvaluator.Evaluate(merged, Facts).GetCodePolicyKey());
    }

    [Fact]
    public void At_publish_the_ceiling_refuses_the_three_overrides_and_lets_the_rest_through()
    {
        var nationalFields = new HashSet<string> { "isPrivileged" };
        Assert.Equal(new[] { PolicyCeiling.SetFieldCode }, PolicyCeiling.CheckAgencyRule(
            [new PolicyEffectDefinition { Effect = PolicyEffect.SetField, Params = new() { ["field"] = "isPrivileged", ["value"] = "false" } }],
            nationalFields, nationalTriggersWorkflow: false).Select(i => i.Code));
        Assert.Equal(new[] { PolicyCeiling.TriggerWorkflowCode }, PolicyCeiling.CheckAgencyRule(
            [new PolicyEffectDefinition { Effect = PolicyEffect.TriggerWorkflow, Params = new() { ["workflowCode"] = "X" } }],
            nationalFields, nationalTriggersWorkflow: true).Select(i => i.Code));
        Assert.Equal(new[] { PolicyCeiling.SelectCodePolicyCode }, PolicyCeiling.CheckAgencyRule(
            [new PolicyEffectDefinition { Effect = PolicyEffect.SelectCodePolicy, Params = new() { ["policyKey"] = "X" } }],
            new HashSet<string>(), nationalTriggersWorkflow: false).Select(i => i.Code));
        Assert.Empty(PolicyCeiling.CheckAgencyRule(
            [new PolicyEffectDefinition { Effect = PolicyEffect.Deny, Params = new() { ["reason"] = "x" } },
             new PolicyEffectDefinition { Effect = PolicyEffect.TriggerWorkflow, Params = new() { ["workflowCode"] = "X" } },
             new PolicyEffectDefinition { Effect = PolicyEffect.SetField, Params = new() { ["field"] = "note", ["value"] = "x" } }],
            nationalFields, nationalTriggersWorkflow: false));
    }
}
