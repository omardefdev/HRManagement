using System.Globalization;
using System.Text.Json;

namespace HRManagement.SharedKernel.Policy;

/// <summary>PLAN-47: one problem in a policy rule. <see cref="JsonPath"/> names where it is ("$.when.all[1].operator").</summary>
public sealed record PolicyRuleIssue(string Code, string MessageAr, string JsonPath);

/// <summary>
/// PLAN-47 (G-POL-06): strict parsing of a rule's When and Then. Nothing is tolerated: an unknown property, an empty group,
/// an unknown operator, a number for an effect or an unknown parameter is an issue, never a silent default. The runtime mapper
/// turns a rule with any issue here into a condition that never matches.
/// </summary>
public static class PolicyRuleJson
{
    public const int MaxDepth = 6;
    public const int MaxLeaves = 50;
    public const int MaxDenyReasonLength = 500;

    public static readonly IReadOnlyList<string> Operators =
    [
        "Equals", "NotEquals", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual", "Contains", "In"
    ];

    public static readonly IReadOnlySet<string> NumericOperators = new HashSet<string>(StringComparer.Ordinal)
    {
        "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual"
    };

    /// <summary>The parameters each effect takes; an effect not listed takes none.</summary>
    public static readonly IReadOnlyDictionary<PolicyEffect, string[]> EffectParameters = new Dictionary<PolicyEffect, string[]>
    {
        [PolicyEffect.Deny] = ["reason"],
        [PolicyEffect.TriggerWorkflow] = ["workflowCode"],
        [PolicyEffect.SetField] = ["field", "value"],
        [PolicyEffect.SelectCodePolicy] = ["policyKey"]
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    /// <summary>A number the evaluator understands: invariant culture, a dot for decimals, no thousands separator.</summary>
    public static bool TryParseNumber(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    public static PolicyConditionGroup? ParseWhenStrict(string? json, List<PolicyRuleIssue> issues)
    {
        const string root = "$.when";
        if (string.IsNullOrWhiteSpace(json))
        {
            issues.Add(new("WHEN_EMPTY", "الشرط (When) فارغ: الشرط الفارغ يطابق كل شيء ولا يُقبل", root));
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            issues.Add(new("WHEN_JSON", $"الشرط (When) ليس JSON صالحاً: {ex.Message}", root));
            return null;
        }

        using (doc)
        {
            var before = issues.Count;
            var leaves = 0;
            var node = ParseNode(doc.RootElement, root, 1, ref leaves, issues);
            if (leaves > MaxLeaves)
                issues.Add(new("WHEN_TOO_MANY_LEAVES", $"الشرط فيه {leaves} مقارنة، والحد {MaxLeaves}", root));
            if (issues.Count > before || node is null)
                return null;
            return node.Group ?? new PolicyConditionGroup { All = [node] };
        }
    }

    public static List<PolicyEffectDefinition>? ParseThenStrict(string? json, List<PolicyRuleIssue> issues)
    {
        const string root = "$.then";
        if (string.IsNullOrWhiteSpace(json))
        {
            issues.Add(new("THEN_EMPTY", "الأثر (Then) فارغ: يجب أن يكون مصفوفة فيها أثر واحد على الأقل", root));
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            issues.Add(new("THEN_JSON", $"الأثر (Then) ليس JSON صالحاً: {ex.Message}", root));
            return null;
        }

        using (doc)
        {
            var before = issues.Count;
            var el = doc.RootElement;
            if (el.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new("THEN_NOT_ARRAY", "الأثر (Then) يجب أن يكون مصفوفة", root));
                return null;
            }

            if (el.GetArrayLength() == 0)
            {
                issues.Add(new("THEN_EMPTY", "الأثر (Then) مصفوفة فارغة: القاعدة بلا أثر لا تُقبل", root));
                return null;
            }

            var effects = new List<PolicyEffectDefinition>();
            var i = 0;
            foreach (var item in el.EnumerateArray())
            {
                var effect = ParseEffect(item, $"{root}[{i}]", issues);
                if (effect is not null)
                    effects.Add(effect);
                i++;
            }

            return issues.Count > before ? null : effects;
        }
    }

    /// <summary>Every comparison of a parsed condition, with its path.</summary>
    public static IEnumerable<(PolicyConditionLeaf Leaf, string Path)> Leaves(PolicyConditionGroup group, string path = "$.when")
    {
        foreach (var item in GroupChildren(group, path))
        {
            if (item.Node.Leaf is not null)
                yield return (item.Node.Leaf, item.Path);
            else if (item.Node.Group is not null)
                foreach (var nested in Leaves(item.Node.Group, item.Path))
                    yield return nested;
        }
    }

    private static IEnumerable<(PolicyConditionNode Node, string Path)> GroupChildren(PolicyConditionGroup group, string path)
    {
        if (group.All is not null)
            for (var i = 0; i < group.All.Count; i++)
                yield return (group.All[i], $"{path}.all[{i}]");
        if (group.Any is not null)
            for (var i = 0; i < group.Any.Count; i++)
                yield return (group.Any[i], $"{path}.any[{i}]");
        if (group.Not is not null)
            yield return (group.Not, $"{path}.not");
    }

    private static PolicyConditionNode? ParseNode(JsonElement el, string path, int depth, ref int leaves, List<PolicyRuleIssue> issues)
    {
        if (depth > MaxDepth)
        {
            issues.Add(new("WHEN_TOO_DEEP", $"الشرط أعمق من {MaxDepth} مستويات", path));
            return null;
        }

        if (el.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new("WHEN_NOT_OBJECT", "كل عقدة في الشرط يجب أن تكون كائناً", path));
            return null;
        }

        // Property names match whatever their case (rules serialized from the models carry "Field"); a name used twice in any
        // case is refused.
        var props = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var p in el.EnumerateObject())
        {
            var canonical = Canonical(p.Name, WhenNames);
            if (props.ContainsKey(canonical))
            {
                issues.Add(new("WHEN_DUPLICATE_PROPERTY", $"الخاصية \"{p.Name}\" مكررة", $"{path}.{p.Name}"));
                return null;
            }
            props[canonical] = p.Value;
            names.Add(canonical);
        }

        if (names.Count == 0)
        {
            issues.Add(new("WHEN_EMPTY", "عقدة فارغة ({}) في الشرط: الشرط الفارغ يطابق كل شيء ولا يُقبل", path));
            return null;
        }

        if (names.Contains("field", StringComparer.Ordinal) || names.Contains("operator", StringComparer.Ordinal) || names.Contains("value", StringComparer.Ordinal))
            return ParseLeaf(props, names, path, ref leaves, issues);

        if (names.Count != 1 || names[0] is not ("all" or "any" or "not"))
        {
            foreach (var unknown in names.Where(n => n is not ("all" or "any" or "not")))
                issues.Add(new("WHEN_UNKNOWN_PROPERTY", $"خاصية غير معروفة \"{unknown}\": المسموح all أو any أو not أو ورقة field/operator/value", $"{path}.{unknown}"));
            if (names.Count(n => n is "all" or "any" or "not") > 1)
                issues.Add(new("WHEN_MANY_GROUPS", "العقدة تحمل أكثر من واحد من all/any/not", path));
            return null;
        }

        var name = names[0];
        var child = props[name];
        var childPath = $"{path}.{name}";
        if (name == "not")
        {
            var inner = ParseNode(child, childPath, depth + 1, ref leaves, issues);
            return inner is null ? null : new PolicyConditionNode { Group = new PolicyConditionGroup { Not = inner } };
        }

        if (child.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("WHEN_GROUP_NOT_ARRAY", $"\"{name}\" يجب أن تكون مصفوفة", childPath));
            return null;
        }

        if (child.GetArrayLength() == 0)
        {
            issues.Add(new("WHEN_EMPTY_GROUP", $"\"{name}\" مصفوفة فارغة: المجموعة الفارغة تطابق كل شيء ولا تُقبل", childPath));
            return null;
        }

        var list = new List<PolicyConditionNode>();
        var i = 0;
        foreach (var item in child.EnumerateArray())
        {
            var node = ParseNode(item, $"{childPath}[{i}]", depth + 1, ref leaves, issues);
            if (node is not null)
                list.Add(node);
            i++;
        }

        var group = name == "all" ? new PolicyConditionGroup { All = list } : new PolicyConditionGroup { Any = list };
        return new PolicyConditionNode { Group = group };
    }

    private static readonly string[] WhenNames = ["all", "any", "not", "field", "operator", "value"];
    private static readonly string[] EffectNames = ["effect", "params"];

    private static string Canonical(string name, string[] known) =>
        known.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name;

    private static PolicyConditionNode? ParseLeaf(Dictionary<string, JsonElement> props, List<string> names, string path, ref int leaves, List<PolicyRuleIssue> issues)
    {
        var ok = true;
        foreach (var unknown in names.Where(n => n is not ("field" or "operator" or "value")))
        {
            issues.Add(new("WHEN_UNKNOWN_PROPERTY", $"خاصية غير معروفة \"{unknown}\" في المقارنة: المسموح field وoperator وvalue", $"{path}.{unknown}"));
            ok = false;
        }

        foreach (var required in new[] { "field", "operator", "value" }.Where(r => !names.Contains(r, StringComparer.Ordinal)))
        {
            issues.Add(new("WHEN_MISSING_PROPERTY", $"المقارنة تنقصها \"{required}\"", $"{path}.{required}"));
            ok = false;
        }

        if (!ok)
            return null;

        var fieldEl = props["field"];
        var operatorEl = props["operator"];
        var valueEl = props["value"];

        if (fieldEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fieldEl.GetString()))
        {
            issues.Add(new("WHEN_FIELD", "اسم الحقل يجب أن يكون نصاً غير فارغ", $"{path}.field"));
            ok = false;
        }

        var op = operatorEl.ValueKind == JsonValueKind.String ? operatorEl.GetString() : null;
        if (op is null || !Operators.Contains(op, StringComparer.Ordinal))
        {
            issues.Add(new("WHEN_OPERATOR", $"عامل غير معروف \"{(op ?? operatorEl.GetRawText())}\": المسموح {string.Join("، ", Operators)}", $"{path}.operator"));
            ok = false;
        }

        string? value = valueEl.ValueKind switch
        {
            JsonValueKind.String => valueEl.GetString(),
            // A JSON number is written in the invariant form already; keep its text as it is.
            JsonValueKind.Number => valueEl.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        if (value is null)
        {
            issues.Add(new("WHEN_VALUE", "القيمة يجب أن تكون نصاً أو رقماً أو true/false", $"{path}.value"));
            ok = false;
        }
        else if (op is not null && NumericOperators.Contains(op) && !TryParseNumber(value, out _))
        {
            issues.Add(new("WHEN_VALUE_NOT_NUMBER", $"القيمة \"{value}\" ليست رقماً بالصيغة الثابتة (أرقام لاتينية والنقطة فاصلاً عشرياً)", $"{path}.value"));
            ok = false;
        }

        leaves++;
        if (!ok)
            return null;

        return new PolicyConditionNode
        {
            Leaf = new PolicyConditionLeaf { Field = fieldEl.GetString()!, Operator = op!, Value = value! }
        };
    }

    private static PolicyEffectDefinition? ParseEffect(JsonElement el, string path, List<PolicyRuleIssue> issues)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new("THEN_NOT_OBJECT", "كل أثر يجب أن يكون كائناً {effect, params}", path));
            return null;
        }

        var ok = true;
        JsonElement? effectEl = null;
        JsonElement? paramsEl = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in el.EnumerateObject())
        {
            var name = Canonical(p.Name, EffectNames);
            if (!seen.Add(name))
            {
                issues.Add(new("THEN_DUPLICATE_PROPERTY", $"الخاصية \"{p.Name}\" مكررة", $"{path}.{p.Name}"));
                ok = false;
                continue;
            }

            switch (name)
            {
                case "effect": effectEl = p.Value; break;
                case "params": paramsEl = p.Value; break;
                default:
                    issues.Add(new("THEN_UNKNOWN_PROPERTY", $"خاصية غير معروفة \"{p.Name}\" في الأثر: المسموح effect وparams", $"{path}.{p.Name}"));
                    ok = false;
                    break;
            }
        }

        PolicyEffect effect = default;
        var effectName = effectEl is { ValueKind: JsonValueKind.String } e ? e.GetString() : null;
        if (effectName is null
            || !Enum.TryParse(effectName, ignoreCase: true, out effect)
            || !Enum.IsDefined(effect)
            || effectName.Any(char.IsDigit))
        {
            issues.Add(new("THEN_EFFECT", $"أثر غير معروف \"{(effectName ?? effectEl?.GetRawText() ?? "")}\": المسموح اسم من {string.Join("، ", Enum.GetNames<PolicyEffect>())}", $"{path}.effect"));
            return null;
        }

        var allowed = EffectParameters.TryGetValue(effect, out var names) ? names : [];
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (paramsEl is { } pe)
        {
            if (pe.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new("THEN_PARAMS", "params يجب أن تكون كائناً", $"{path}.params"));
                return null;
            }

            foreach (var p in pe.EnumerateObject())
            {
                if (!allowed.Contains(p.Name, StringComparer.Ordinal))
                {
                    issues.Add(new("THEN_UNKNOWN_PARAM", $"معامل غير معروف \"{p.Name}\" للأثر {effect}", $"{path}.params.{p.Name}"));
                    ok = false;
                    continue;
                }

                if (p.Value.ValueKind != JsonValueKind.String)
                {
                    issues.Add(new("THEN_PARAM_NOT_STRING", $"المعامل \"{p.Name}\" يجب أن يكون نصاً", $"{path}.params.{p.Name}"));
                    ok = false;
                    continue;
                }

                if (!parameters.TryAdd(p.Name, p.Value.GetString()!))
                {
                    issues.Add(new("THEN_DUPLICATE_PROPERTY", $"المعامل \"{p.Name}\" مكرر", $"{path}.params.{p.Name}"));
                    ok = false;
                }
            }
        }

        foreach (var required in allowed)
        {
            if (!parameters.TryGetValue(required, out var v) || string.IsNullOrWhiteSpace(v))
            {
                issues.Add(new("THEN_MISSING_PARAM", $"الأثر {effect} يتطلب المعامل \"{required}\"", $"{path}.params.{required}"));
                ok = false;
            }
        }

        if (effect == PolicyEffect.Deny && parameters.TryGetValue("reason", out var reason) && reason.Length > MaxDenyReasonLength)
        {
            issues.Add(new("THEN_REASON_LENGTH", $"سبب الرفض أطول من {MaxDenyReasonLength} حرف", $"{path}.params.reason"));
            ok = false;
        }

        if (!ok)
            return null;

        return new PolicyEffectDefinition { Effect = effect, Params = parameters.Count == 0 ? null : parameters };
    }
}
