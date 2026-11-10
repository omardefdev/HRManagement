using System.Text.Json.Serialization;
using HRManagement.SharedKernel.Workflow;

namespace HRManagement.SharedKernel.Policy;

public sealed class PolicyConditionLeaf
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "Equals";
    public string Value { get; set; } = string.Empty;

    public bool Evaluate(IReadOnlyDictionary<string, string> facts)
    {
        var wf = new WorkflowCondition { Field = Field, Operator = Operator, Value = Value };
        return wf.Evaluate(facts as Dictionary<string, string> ?? new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class PolicyConditionGroup
{
    [JsonPropertyName("all")]
    public List<PolicyConditionNode>? All { get; set; }

    [JsonPropertyName("any")]
    public List<PolicyConditionNode>? Any { get; set; }

    [JsonPropertyName("not")]
    public PolicyConditionNode? Not { get; set; }

    /// <summary>PLAN-47: a condition that never matches (a stored rule the strict parser refuses).</summary>
    [JsonIgnore]
    public bool IsNever { get; init; }

    /// <summary>PLAN-47: what a malformed stored rule becomes: it matches nothing.</summary>
    public static PolicyConditionGroup Never => new() { IsNever = true };

    public bool Evaluate(IReadOnlyDictionary<string, string> facts)
    {
        if (IsNever)
            return false;

        if (All is { Count: > 0 })
            return All.All(n => n.Evaluate(facts));

        if (Any is { Count: > 0 })
            return Any.Any(n => n.Evaluate(facts));

        if (Not is not null)
            return !Not.Evaluate(facts);

        // PLAN-47 (G-POL-06): an empty group matches nothing; it used to match everything.
        return false;
    }
}

[JsonConverter(typeof(PolicyConditionNodeJsonConverter))]
public sealed class PolicyConditionNode
{
    public PolicyConditionLeaf? Leaf { get; init; }
    public PolicyConditionGroup? Group { get; init; }

    public bool Evaluate(IReadOnlyDictionary<string, string> facts)
    {
        if (Leaf is not null) return Leaf.Evaluate(facts);
        if (Group is not null) return Group.Evaluate(facts);
        return false;
    }
}

public sealed class PolicyEffectDefinition
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PolicyEffect Effect { get; set; }
    public Dictionary<string, string>? Params { get; set; }
}

/// <summary>PLAN-48 (§4): where a rule comes from. The ceiling (<see cref="PolicyCeiling"/>) bounds the agency's rules.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PolicyRuleOrigin
{
    National,
    Agency
}

public sealed class PolicyRuleDefinition
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public PolicyConditionGroup When { get; set; } = new();
    public List<PolicyEffectDefinition> Then { get; set; } = [];
    public int Priority { get; set; } = 100;

    /// <summary>PLAN-48: National by default (compatible with every rule stored or serialized before); an override's rules are Agency.</summary>
    public PolicyRuleOrigin Origin { get; set; } = PolicyRuleOrigin.National;
}
