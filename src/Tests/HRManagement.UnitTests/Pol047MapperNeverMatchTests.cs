using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §1: a stored rule in a form the evaluator does not understand matches nothing and is listed in InvalidRuleKeys. It
/// used to become "match everything" (an odd When) or "no effect" (an odd Then).
/// </summary>
public sealed class Pol047MapperNeverMatchTests
{
    private static readonly Dictionary<string, string> Facts = new() { ["category"] = "x", ["value"] = "5" };

    private static PolicyVersionSnapshot Snapshot(params (string Key, string When, string Then)[] rules)
    {
        var template = new PolicyTemplate { Domain = PolicyDomains.AssetsCustody, Name = "t", SubjectType = "Asset" };
        var version = new PolicyTemplateVersion { PolicyTemplate = template, Status = PolicyVersionStatus.Published };
        var priority = 1;
        foreach (var (key, when, then) in rules)
            version.Rules.Add(new PolicyRule { RuleKey = key, Priority = priority++, WhenJson = when, ThenJson = then });
        return PolicyEntityMapper.ToSnapshot(template, version);
    }

    [Theory]
    [InlineData("{}", """[{"effect":"Deny","params":{"reason":"r"}}]""")]
    [InlineData("""{"all":[]}""", """[{"effect":"Deny","params":{"reason":"r"}}]""")]
    [InlineData("""{"when":{"all":[{"field":"category","operator":"Equals","value":"x"}]}}""", """[{"effect":"Deny","params":{"reason":"r"}}]""")]
    [InlineData("""{"all":[{"field":"value","operator":"LessThanOrEqualz","value":"9"}]}""", """[{"effect":"Deny","params":{"reason":"r"}}]""")]
    [InlineData("""{"all":[{"field":"category","operator":"Equals","value":"x"}]}""", "[]")]
    [InlineData("""{"all":[{"field":"category","operator":"Equals","value":"x"}]}""", """[{"effect":2}]""")]
    [InlineData("""{"all":[{"field":"category","operator":"Equals","value":"x"}]}""", """[{"effect":"Deny"}]""")]
    [InlineData("not json", """[{"effect":"Deny","params":{"reason":"r"}}]""")]
    public void A_malformed_stored_rule_never_matches_and_is_reported(string when, string then)
    {
        var snapshot = Snapshot(("R-BROKEN", when, then));

        Assert.Equal(["R-BROKEN"], snapshot.InvalidRuleKeys);
        var rule = Assert.Single(snapshot.Rules);
        Assert.True(rule.When.IsNever);
        Assert.Empty(rule.Then);

        var decision = PolicyEvaluator.Evaluate(snapshot, Facts);
        Assert.True(decision.Allowed);
        Assert.Empty(decision.MatchedRuleIds);
    }

    [Fact]
    public void A_valid_rule_beside_a_broken_one_still_matches()
    {
        var snapshot = Snapshot(
            ("R-BROKEN", "{}", """[{"effect":"Deny","params":{"reason":"broken"}}]"""),
            ("R-OK", """{"all":[{"field":"value","operator":"LessThanOrEqual","value":"5"}]}""", """[{"effect":"RequireEvidence"}]"""));

        Assert.Equal(["R-BROKEN"], snapshot.InvalidRuleKeys);
        var decision = PolicyEvaluator.Evaluate(snapshot, Facts);
        Assert.True(decision.Allowed);
        Assert.Equal(["R-OK"], decision.MatchedRuleIds);
        Assert.Contains(PolicyEffect.RequireEvidence, decision.Effects);
    }

    [Fact]
    public void The_retired_hire_row_of_plan25_matches_nothing()
    {
        var snapshot = Snapshot(("R-REC-HIRE", """{"all":[{"field":"action","operator":"Equals","value":"__deactivated__"}]}""", "[]"));

        Assert.Equal(["R-REC-HIRE"], snapshot.InvalidRuleKeys);
        Assert.Empty(PolicyEvaluator.Evaluate(snapshot, new Dictionary<string, string> { ["action"] = "__deactivated__" }).MatchedRuleIds);
    }

    [Fact]
    public void Merging_keeps_the_invalid_keys_of_both_levels()
    {
        var national = Snapshot(("R-N", "{}", "[]"));
        var tenant = Snapshot(("R-T", """{"all":[]}""", """[{"effect":"Allow"}]"""));

        var merged = PolicyEvaluator.MergeNationalAndTenant(national, tenant);

        Assert.Equal(["R-N", "R-T"], merged.InvalidRuleKeys);
    }

    [Fact]
    public void An_empty_group_built_in_code_matches_nothing()
    {
        Assert.False(new PolicyConditionGroup().Evaluate(Facts));
        Assert.False(new PolicyConditionNode().Evaluate(Facts));
        Assert.False(PolicyConditionGroup.Never.Evaluate(Facts));
        Assert.False(PolicyEntityMapper.DeserializeWhen("{}").Evaluate(Facts));
    }
}
