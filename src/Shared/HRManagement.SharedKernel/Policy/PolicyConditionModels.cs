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

    public bool Evaluate(IReadOnlyDictionary<string, string> facts)
    {
        if (All is { Count: > 0 })
            return All.All(n => n.Evaluate(facts));

        if (Any is { Count: > 0 })
            return Any.Any(n => n.Evaluate(facts));

        if (Not is not null)
            return !Not.Evaluate(facts);

        return true;
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
        return true;
    }
}

public sealed class PolicyEffectDefinition
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PolicyEffect Effect { get; set; }
    public Dictionary<string, string>? Params { get; set; }
}

public sealed class PolicyRuleDefinition
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public PolicyConditionGroup When { get; set; } = new();
    public List<PolicyEffectDefinition> Then { get; set; } = [];
    public int Priority { get; set; } = 100;
}
