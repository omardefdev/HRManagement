namespace HRManagement.SharedKernel.Policy;

/// <summary>
/// PLAN-49: which actions a rule's condition can match. Every call site sends its action as the fact <c>action</c>, so a condition
/// on that fact binds the rule to actions, and through them to entity types (the catalog lists the actions of each type). The
/// answer is an over-approximation: an action is left out only when the condition cannot be true for it.
/// </summary>
public static class PolicyRuleActionScope
{
    /// <summary>The fact every facts builder sends, and every facts schema of a governed domain holds.</summary>
    public const string ActionField = "action";

    /// <summary>The only operators a condition on <see cref="ActionField"/> may use: the ones this analysis decides exactly.</summary>
    public static readonly IReadOnlySet<string> ActionOperators = new HashSet<string>(StringComparer.Ordinal)
    {
        "Equals", "NotEquals", "In"
    };

    /// <summary>The values a leaf on <see cref="ActionField"/> names (one for Equals and NotEquals, a list for In).</summary>
    public static IReadOnlyList<string> NamedActions(PolicyConditionLeaf leaf) =>
        leaf.Operator == "In"
            ? leaf.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [leaf.Value];

    /// <summary>The actions of <paramref name="domainActions"/> for which <paramref name="when"/> may be true.</summary>
    public static IReadOnlySet<string> PossibleActions(PolicyConditionGroup when, IReadOnlySet<string> domainActions) =>
        Evaluate(when, domainActions).MayBeTrue;

    private static (HashSet<string> MayBeTrue, HashSet<string> MayBeFalse) Evaluate(PolicyConditionGroup group, IReadOnlySet<string> all)
    {
        // The same order as PolicyConditionGroup.Evaluate: all, then any, then not; an empty group (and Never) matches nothing.
        if (!group.IsNever && group.All is { Count: > 0 })
        {
            var mayBeTrue = new HashSet<string>(all, StringComparer.Ordinal);
            var mayBeFalse = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (t, f) in group.All.Select(n => Evaluate(n, all)))
            {
                mayBeTrue.IntersectWith(t);
                mayBeFalse.UnionWith(f);
            }

            return (mayBeTrue, mayBeFalse);
        }

        if (!group.IsNever && group.Any is { Count: > 0 })
        {
            var mayBeTrue = new HashSet<string>(StringComparer.Ordinal);
            var mayBeFalse = new HashSet<string>(all, StringComparer.Ordinal);
            foreach (var (t, f) in group.Any.Select(n => Evaluate(n, all)))
            {
                mayBeTrue.UnionWith(t);
                mayBeFalse.IntersectWith(f);
            }

            return (mayBeTrue, mayBeFalse);
        }

        if (!group.IsNever && group.Not is not null)
        {
            var (t, f) = Evaluate(group.Not, all);
            return (f, t);
        }

        return (new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(all, StringComparer.Ordinal));
    }

    private static (HashSet<string> MayBeTrue, HashSet<string> MayBeFalse) Evaluate(PolicyConditionNode node, IReadOnlySet<string> all)
    {
        if (node.Group is not null)
            return Evaluate(node.Group, all);

        if (node.Leaf is not { } leaf)
            return (new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(all, StringComparer.Ordinal));

        // A condition on another fact says nothing about the action: it may hold, or not, for any of them. So does a condition
        // on the action with an operator this analysis does not decide (the validator refuses those).
        if (!string.Equals(leaf.Field, ActionField, StringComparison.Ordinal) || !ActionOperators.Contains(leaf.Operator))
            return (new HashSet<string>(all, StringComparer.Ordinal), new HashSet<string>(all, StringComparer.Ordinal));

        // The evaluator compares values without case, so the analysis does too.
        var named = new HashSet<string>(NamedActions(leaf), StringComparer.OrdinalIgnoreCase);
        var matching = new HashSet<string>(all.Where(named.Contains), StringComparer.Ordinal);
        var others = new HashSet<string>(all.Where(a => !named.Contains(a)), StringComparer.Ordinal);
        return leaf.Operator == "NotEquals" ? (others, matching) : (matching, others);
    }
}
