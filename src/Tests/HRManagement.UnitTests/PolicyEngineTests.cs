using System.Text.Json;
using HR.Core.Domain.Services.Policy;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.UnitTests;

public class PolicyConditionGroupTests
{
    [Fact]
    public void Seed_json_with_lowercase_property_names_preserves_leaf_values()
    {
        const string json = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""";

        var group = PolicyEntityMapper.DeserializeWhen(json);

        Assert.True(group.Evaluate(new Dictionary<string, string> { ["isPrivileged"] = "true" }));
        var leaf = Assert.Single(group.All!).Leaf;
        Assert.NotNull(leaf);
        Assert.Equal("isPrivileged", leaf.Field);
        Assert.Equal("Equals", leaf.Operator);
        Assert.Equal("true", leaf.Value);
    }

    [Fact]
    public void Condition_json_property_names_are_case_insensitive()
    {
        const string json = """{"ALL":[{"Field":"value","Operator":"GreaterThan","Value":"1000"}]}""";

        var group = PolicyEntityMapper.DeserializeWhen(json);

        Assert.True(group.Evaluate(new Dictionary<string, string> { ["value"] = "1001" }));
    }

    [Fact]
    public void All_group_requires_every_leaf()
    {
        var group = new PolicyConditionGroup
        {
            All =
            [
                new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "value", Operator = "GreaterThan", Value = "1000" } },
                new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "isPrivileged", Operator = "Equals", Value = "true" } }
            ]
        };
        var facts = new Dictionary<string, string> { ["value"] = "5000", ["isPrivileged"] = "true" };
        Assert.True(group.Evaluate(facts));
        facts["value"] = "100";
        Assert.False(group.Evaluate(facts));
    }

    [Fact]
    public void Any_group_matches_one()
    {
        var group = new PolicyConditionGroup
        {
            Any =
            [
                new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "category", Operator = "Equals", Value = "مركبات" } },
                new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "isPrivileged", Operator = "Equals", Value = "true" } }
            ]
        };
        var facts = new Dictionary<string, string> { ["category"] = "حواسيب", ["isPrivileged"] = "true" };
        Assert.True(group.Evaluate(facts));
    }

    [Fact]
    public void Not_inverts_result()
    {
        var group = new PolicyConditionGroup
        {
            Not = new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "isLegacyEntry", Operator = "Equals", Value = "true" } }
        };
        Assert.True(group.Evaluate(new Dictionary<string, string> { ["isLegacyEntry"] = "false" }));
    }
}

public class PolicyEvaluatorTests
{
    [Fact]
    public void Privileged_rule_requires_evidence_effect()
    {
        var snapshot = new PolicyVersionSnapshot
        {
            VersionId = Guid.NewGuid(),
            Domain = PolicyDomains.AssetsCustody,
            Rules =
            [
                new PolicyRuleDefinition
                {
                    Id = "R1",
                    Priority = 10,
                    When = new PolicyConditionGroup
                    {
                        All = [new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "isPrivileged", Operator = "Equals", Value = "true" } }]
                    },
                    Then = [new PolicyEffectDefinition { Effect = PolicyEffect.RequireEvidence }]
                }
            ]
        };
        var decision = PolicyEvaluator.Evaluate(snapshot, new Dictionary<string, string> { ["isPrivileged"] = "true" });
        Assert.True(decision.Allowed);
        Assert.Contains(PolicyEffect.RequireEvidence, decision.Effects);
    }

    [Fact]
    public void Deny_effect_blocks()
    {
        var snapshot = new PolicyVersionSnapshot
        {
            Rules =
            [
                new PolicyRuleDefinition
                {
                    When = new PolicyConditionGroup { All = [new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "action", Operator = "Equals", Value = "Delete" } }] },
                    Then = [new PolicyEffectDefinition { Effect = PolicyEffect.Deny, Params = new() { ["reason"] = "محظور" } }]
                }
            ]
        };
        var decision = PolicyEvaluator.Evaluate(snapshot, new Dictionary<string, string> { ["action"] = "Delete" });
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void SelectCodePolicy_returns_policy_key_from_decision()
    {
        var decision = new PolicyDecision
        {
            Allowed = true,
            Effects = [PolicyEffect.SelectCodePolicy],
            EffectDetails =
            [
                new PolicyEffectDefinition
                {
                    Effect = PolicyEffect.SelectCodePolicy,
                    Params = new Dictionary<string, string> { ["policyKey"] = "ASSET-PRIVILEGED" }
                }
            ]
        };
        Assert.Equal("ASSET-PRIVILEGED", decision.GetCodePolicyKey());
    }

    [Fact]
    public void Privileged_create_rule_emits_select_code_policy()
    {
        var snapshot = new PolicyVersionSnapshot
        {
            Domain = PolicyDomains.AssetsCustody,
            Rules =
            [
                new PolicyRuleDefinition
                {
                    Id = "R-PRIV-CODE",
                    Priority = 12,
                    When = new PolicyConditionGroup
                    {
                        All =
                        [
                            new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "isPrivileged", Operator = "Equals", Value = "true" } },
                            new PolicyConditionNode { Leaf = new PolicyConditionLeaf { Field = "action", Operator = "Equals", Value = "Create" } }
                        ]
                    },
                    Then =
                    [
                        new PolicyEffectDefinition
                        {
                            Effect = PolicyEffect.SelectCodePolicy,
                            Params = new Dictionary<string, string> { ["policyKey"] = "ASSET-PRIVILEGED" }
                        }
                    ]
                }
            ]
        };
        var decision = PolicyEvaluator.Evaluate(snapshot, new Dictionary<string, string>
        {
            ["isPrivileged"] = "true",
            ["action"] = "Create"
        });
        Assert.Equal("ASSET-PRIVILEGED", decision.GetCodePolicyKey());
    }

    [Fact]
    public void National_tenant_merge_concatenates_rules()
    {
        var national = new PolicyVersionSnapshot
        {
            Domain = PolicyDomains.AssetsCustody,
            Rules = [new PolicyRuleDefinition { Id = "N1", Priority = 1, When = new(), Then = [] }]
        };
        var tenant = new PolicyVersionSnapshot
        {
            TenantId = Guid.NewGuid(),
            Rules = [new PolicyRuleDefinition { Id = "T1", Priority = 2, When = new(), Then = [] }]
        };
        var merged = PolicyEvaluator.MergeNationalAndTenant(national, tenant);
        Assert.Equal(2, merged.Rules.Count);
    }
}

public class PolicyShadowDiffTests
{
    [Fact]
    public void Shadow_comparer_detects_effect_mismatch()
    {
        var request = new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            Action = "Assign",
            Facts = new Dictionary<string, string> { ["isPrivileged"] = "true", ["value"] = "500" }
        };
        var policyDecision = new PolicyDecision
        {
            Allowed = true,
            Effects = [PolicyEffect.Allow],
            Explanations = ["policy only allow"]
        };
        var diffs = HR.Core.Infrastructure.Services.Policy.PolicyShadowComparer.CompareWithStaticFallback(request, policyDecision);
        Assert.NotEmpty(diffs);
    }
}
public class PolicyFactsValidatorTests
{
    [Fact]
    public void Rejects_unknown_fields()
    {
        var schema = new PolicyFactsSchemaDefinition
        {
            Fields = [new PolicyFieldSchema { Name = "value", Type = "decimal" }]
        };
        var errors = PolicyFactsValidator.Validate(schema, new Dictionary<string, string> { ["unknown"] = "x" });
        Assert.NotEmpty(errors);
    }
}
