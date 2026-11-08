using System.Text.RegularExpressions;
using HRManagement.SharedKernel.Policy;

namespace HR.Core.Domain.Services.Policy;

/// <summary>A rule as an author submits it, or as a seed change set or a version holds it.</summary>
public sealed record PolicyRuleInput(string? RuleKey, string? Name, int Priority, string? WhenJson, string? ThenJson);

/// <summary>What the validator checks a rule against, loaded once per domain.</summary>
public sealed class PolicyRuleValidationContext
{
    public required string Domain { get; init; }

    /// <summary>The domain's facts schema; null when none is visible, and then every field is refused.</summary>
    public PolicyFactsSchemaDefinition? Schema { get; init; }

    /// <summary>Workflow definitions that are published and not retired.</summary>
    public IReadOnlySet<string> PublishedWorkflowCodes { get; init; } = new HashSet<string>();

    /// <summary>PLAN-25 and PLAN-46 retired workflow codes: a rule that triggers one is refused with the reason.</summary>
    public IReadOnlySet<string> RetiredWorkflowCodes { get; init; } = new HashSet<string>();

    /// <summary>Workflow codes bound to this domain in PolicyWorkflowBindings.</summary>
    public IReadOnlySet<string> BoundWorkflowCodes { get; init; } = new HashSet<string>();

    /// <summary>Policy keys of the code generation templates (SelectCodePolicy).</summary>
    public IReadOnlySet<string> CodePolicyKeys { get; init; } = new HashSet<string>();

    public IPolicyDomainCapabilities Capabilities { get; init; } = AllowAllPolicyDomainCapabilities.Instance;
}

/// <summary>
/// PLAN-47 (G-POL-06): no rule is saved in a form the evaluator does not understand. Runs on every added or changed rule,
/// on a whole version at publish and on every seed change set. Messages are Arabic and name the JSON path.
/// </summary>
public static class PolicyRuleValidator
{
    public const int MaxNameLength = 200;
    public const int MinPriority = 1;
    public const int MaxPriority = 1000;

    private static readonly Regex RuleKeyPattern = new("^[A-Z][A-Z0-9:-]{2,99}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool IsValidRuleKey(string? ruleKey) => ruleKey is not null && RuleKeyPattern.IsMatch(ruleKey);

    public static IReadOnlyList<PolicyRuleIssue> ValidateRule(PolicyRuleInput rule, PolicyRuleValidationContext context)
    {
        var issues = new List<PolicyRuleIssue>();

        if (!IsValidRuleKey(rule.RuleKey))
            issues.Add(new("RULE_KEY", $"مفتاح القاعدة \"{rule.RuleKey}\" غير صالح: حروف لاتينية كبيرة وأرقام و: و- من 3 إلى 100 محرف، ويبدأ بحرف", "$.ruleKey"));

        if (rule.Name is { Length: > MaxNameLength })
            issues.Add(new("RULE_NAME", $"اسم القاعدة أطول من {MaxNameLength} محرف", "$.name"));

        if (rule.Priority is < MinPriority or > MaxPriority)
            issues.Add(new("RULE_PRIORITY", $"الأولوية {rule.Priority} خارج المدى {MinPriority}–{MaxPriority}", "$.priority"));

        var when = PolicyRuleJson.ParseWhenStrict(rule.WhenJson, issues);
        if (when is not null)
            ValidateFields(when, context, issues);

        var then = PolicyRuleJson.ParseThenStrict(rule.ThenJson, issues);
        if (then is not null)
            ValidateEffects(then, context, issues);

        return issues;
    }

    /// <summary>Every rule of a version, plus what only the whole version shows (a key used twice).</summary>
    public static IReadOnlyList<PolicyRuleIssue> ValidateVersion(IReadOnlyList<PolicyRuleInput> rules, PolicyRuleValidationContext context)
    {
        var issues = new List<PolicyRuleIssue>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < rules.Count; i++)
        {
            var prefix = $"$.rules[{i}]";
            foreach (var issue in ValidateRule(rules[i], context))
                issues.Add(issue with
                {
                    JsonPath = prefix + issue.JsonPath[1..],
                    MessageAr = $"القاعدة {rules[i].RuleKey}: {issue.MessageAr}"
                });

            if (rules[i].RuleKey is { } key && !seen.TryAdd(key, i))
                issues.Add(new("RULE_KEY_DUPLICATE", $"المفتاح {key} مكرر في الإصدار (القاعدتان {seen[key]} و{i})", $"{prefix}.ruleKey"));
        }

        return issues;
    }

    private static void ValidateFields(PolicyConditionGroup when, PolicyRuleValidationContext context, List<PolicyRuleIssue> issues)
    {
        foreach (var (leaf, path) in PolicyRuleJson.Leaves(when))
        {
            var field = context.Schema?.Fields.FirstOrDefault(f => string.Equals(f.Name, leaf.Field, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                issues.Add(context.Schema is null
                    ? new("WHEN_NO_SCHEMA", $"لا مخطط حقائق ظاهراً للمجال {context.Domain}، فلا يمكن التحقق من الحقل \"{leaf.Field}\"", $"{path}.field")
                    : new("WHEN_UNKNOWN_FIELD", $"الحقل \"{leaf.Field}\" ليس في مخطط حقائق المجال {context.Domain}", $"{path}.field"));
                continue;
            }

            var type = field.Type.ToLowerInvariant();
            var numericType = type is "int" or "decimal";
            if (PolicyRuleJson.NumericOperators.Contains(leaf.Operator))
            {
                if (!numericType)
                    issues.Add(new("WHEN_FIELD_NOT_NUMERIC", $"العامل {leaf.Operator} يتطلب حقلاً رقمياً، و\"{field.Name}\" من النوع {field.Type}", $"{path}.operator"));
                else if (type == "int" && PolicyRuleJson.TryParseNumber(leaf.Value, out var n) && n != decimal.Truncate(n))
                    issues.Add(new("WHEN_VALUE_NOT_INTEGER", $"الحقل \"{field.Name}\" عدد صحيح، والقيمة {leaf.Value} ليست كذلك", $"{path}.value"));
            }
            else if (type == "bool" && leaf.Value is not ("true" or "false"))
            {
                issues.Add(new("WHEN_VALUE_NOT_BOOL", $"الحقل \"{field.Name}\" منطقي، وقيمته true أو false فقط", $"{path}.value"));
            }
            else if (numericType && leaf.Operator is "Equals" or "NotEquals" && !PolicyRuleJson.TryParseNumber(leaf.Value, out _))
            {
                issues.Add(new("WHEN_VALUE_NOT_NUMBER", $"الحقل \"{field.Name}\" رقمي، والقيمة \"{leaf.Value}\" ليست رقماً بالصيغة الثابتة", $"{path}.value"));
            }

            if (leaf.Operator == "In" && leaf.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length == 0)
                issues.Add(new("WHEN_IN_EMPTY", "العامل In يتطلب قائمة قيم مفصولة بفواصل", $"{path}.value"));
        }
    }

    private static void ValidateEffects(List<PolicyEffectDefinition> then, PolicyRuleValidationContext context, List<PolicyRuleIssue> issues)
    {
        for (var i = 0; i < then.Count; i++)
        {
            var effect = then[i];
            var path = $"$.then[{i}]";
            if (!context.Capabilities.AllowsEffect(context.Domain, effect.Effect))
                issues.Add(new("THEN_EFFECT_NOT_ALLOWED", $"الأثر {effect.Effect} غير مسموح في المجال {context.Domain}", $"{path}.effect"));

            var p = effect.Params ?? [];
            switch (effect.Effect)
            {
                case PolicyEffect.TriggerWorkflow:
                    var code = p["workflowCode"];
                    if (context.RetiredWorkflowCodes.Contains(code))
                        issues.Add(new("THEN_WORKFLOW_RETIRED", $"المسار {code} سُحب ولا يُطلق بعد الآن (PLAN-46)، فلا تُقبل قاعدة تطلقه", $"{path}.params.workflowCode"));
                    else if (!context.PublishedWorkflowCodes.Contains(code))
                        issues.Add(new("THEN_WORKFLOW_UNKNOWN", $"لا تعريف مسار منشوراً بالرمز {code}", $"{path}.params.workflowCode"));
                    else if (!context.BoundWorkflowCodes.Contains(code))
                        issues.Add(new("THEN_WORKFLOW_UNBOUND", $"المسار {code} غير مربوط بالمجال {context.Domain} في روابط السياسة", $"{path}.params.workflowCode"));
                    else if (!context.Capabilities.AllowsWorkflowCode(context.Domain, code))
                        issues.Add(new("THEN_WORKFLOW_NOT_ALLOWED", $"المسار {code} غير مسموح في المجال {context.Domain}", $"{path}.params.workflowCode"));
                    break;
                case PolicyEffect.SetField:
                    if (!context.Capabilities.AllowsSetField(context.Domain, p["field"]))
                        issues.Add(new("THEN_SETFIELD_NOT_ALLOWED", $"لا يجوز للأثر SetField أن يكتب الحقل \"{p["field"]}\" في المجال {context.Domain}", $"{path}.params.field"));
                    break;
                case PolicyEffect.SelectCodePolicy:
                    if (!context.CodePolicyKeys.Contains(p["policyKey"]))
                        issues.Add(new("THEN_CODE_POLICY_UNKNOWN", $"لا قالب ترميز بالمفتاح {p["policyKey"]}", $"{path}.params.policyKey"));
                    break;
            }
        }
    }
}
