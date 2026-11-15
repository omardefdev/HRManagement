using System.Globalization;
using System.Text.Json;

namespace HRManagement.SharedKernel.Policy;

/// <summary>What a piece of a readable rule is: plain words, a field's name, an operator, a value, an effect, or an internal code.</summary>
public enum PolicyReadableKind
{
    Text,
    Field,
    Operator,
    Value,
    Effect,
    Code
}

/// <summary>One piece of a readable rule. <see cref="Code"/> is the key behind an Arabic name (a field key, an action, a workflow code).</summary>
public sealed record PolicyReadableSegment(PolicyReadableKind Kind, string Text, string? Code = null);

/// <summary>A rule as an Arabic sentence: its condition and its effects, as segments the page styles, and as plain text.</summary>
public sealed class PolicyRuleReadable
{
    public required IReadOnlyList<PolicyReadableSegment> When { get; init; }

    public required IReadOnlyList<PolicyReadableSegment> Then { get; init; }

    /// <summary>True when a node or an effect could not be read: the sentence says so and points to the technical view.</summary>
    public bool HasUnknown { get; init; }

    /// <summary>
    /// True when the engine's strict reading refuses the rule (<see cref="PolicyRuleJson.ParseWhenStrict"/>, <see cref="PolicyRuleJson.ParseThenStrict"/>):
    /// a stored rule written before the validator, which the engine never applies. The page says so beside the sentence.
    /// </summary>
    public bool IsInvalid { get; init; }

    public string WhenText => Plain(When);

    public string ThenText => Plain(Then);

    /// <summary>The whole sentence: «إذا …، …».</summary>
    public string Sentence => $"إذا {WhenText}، {ThenText}.";

    private static string Plain(IEnumerable<PolicyReadableSegment> segments) => string.Concat(segments.Select(s => s.Text));
}

/// <summary>
/// PLAN-67 (§5, G-UX-06, P-8): turns a rule's <c>WhenJson</c> and <c>ThenJson</c>, with its domain's facts schema, into one Arabic
/// sentence: <c>all</c>, <c>any</c> and <c>not</c> as «جميع الشروط التالية», «أيٌّ مما يلي» and «ليس»; the eight operators in Arabic
/// (<see cref="PolicyVocabularyAr.Operators"/>); each value by its field's type (نعم/لا for a flag, a number as it is, text in
/// quotes, an action by its Arabic name); and the eleven effects as verbs with their parameters. It never throws: a node it cannot
/// read says «شرط غير معروف — انظر العرض التقني», and a field missing from the schema is shown by its key. The rule itself is not
/// changed: this is display only (published versions are immutable since PLAN-47).
/// </summary>
public static class PolicyRuleReadableRenderer
{
    public const string UnknownCondition = "شرط غير معروف — انظر العرض التقني";
    public const string UnknownEffect = "أثر غير معروف — انظر العرض التقني";
    public const string AllGroup = "جميع الشروط التالية";
    public const string AnyGroup = "أيٌّ مما يلي";
    public const string NotWord = "ليس";
    public const string Yes = "نعم";
    public const string No = "لا";
    public const string InvalidNote = "قاعدة غير صالحة — لا تُطبَّق";

    private const int MaxDepth = 12;

    public static PolicyRuleReadable Render(string? whenJson, string? thenJson, PolicyFactsSchemaDefinition? schema)
    {
        var fields = (schema?.Fields ?? [])
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var unknown = false;
        var when = new List<PolicyReadableSegment>();
        var then = new List<PolicyReadableSegment>();
        try
        {
            using var doc = JsonDocument.Parse(whenJson ?? "");
            RenderNode(doc.RootElement, fields, when, 0, ref unknown, topLevel: true);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            when.Clear();
            when.Add(new(PolicyReadableKind.Text, UnknownCondition));
            unknown = true;
        }

        try
        {
            using var doc = JsonDocument.Parse(thenJson ?? "");
            RenderEffects(doc.RootElement, fields, then, ref unknown);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            then.Clear();
            then.Add(new(PolicyReadableKind.Text, UnknownEffect));
            unknown = true;
        }

        // PLAN-67 (review): the sentence of a rule the engine refuses must not read as a rule in force.
        bool invalid;
        try
        {
            var issues = new List<PolicyRuleIssue>();
            invalid = PolicyRuleJson.ParseWhenStrict(whenJson, issues) is null | PolicyRuleJson.ParseThenStrict(thenJson, issues) is null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            invalid = true;
        }

        return new PolicyRuleReadable { When = when, Then = then, HasUnknown = unknown, IsInvalid = invalid };
    }

    // ------------------------------------------------------------------ condition

    private static void RenderNode(JsonElement node, IReadOnlyDictionary<string, PolicyFieldSchema> fields, List<PolicyReadableSegment> output,
        int depth, ref bool unknown, bool topLevel = false)
    {
        if (depth > MaxDepth || node.ValueKind != JsonValueKind.Object)
        {
            output.Add(new(PolicyReadableKind.Text, UnknownCondition));
            unknown = true;
            return;
        }

        var props = node.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (props.ContainsKey("field") || props.ContainsKey("operator"))
        {
            RenderLeaf(props, fields, output, ref unknown);
            return;
        }

        if (props.Count == 1 && props.TryGetValue("not", out var inner))
        {
            output.Add(new(PolicyReadableKind.Text, NotWord + " ("));
            RenderNode(inner, fields, output, depth + 1, ref unknown);
            output.Add(new(PolicyReadableKind.Text, ")"));
            return;
        }

        bool isAll;
        JsonElement children;
        if (props.Count == 1 && props.TryGetValue("all", out var all))
        {
            isAll = true;
            children = all;
        }
        else if (props.Count == 1 && props.TryGetValue("any", out var any))
        {
            isAll = false;
            children = any;
        }
        else
        {
            output.Add(new(PolicyReadableKind.Text, UnknownCondition));
            unknown = true;
            return;
        }

        if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() == 0)
        {
            output.Add(new(PolicyReadableKind.Text, UnknownCondition));
            unknown = true;
            return;
        }

        var items = children.EnumerateArray().ToList();
        if (items.Count == 1)
        {
            RenderNode(items[0], fields, output, depth + 1, ref unknown, topLevel);
            return;
        }

        if (!topLevel)
            output.Add(new(PolicyReadableKind.Text, "("));
        output.Add(new(PolicyReadableKind.Text, (isAll ? AllGroup : AnyGroup) + ": "));
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
                output.Add(new(PolicyReadableKind.Text, isAll ? "، و" : "، أو "));
            RenderNode(items[i], fields, output, depth + 1, ref unknown);
        }

        if (!topLevel)
            output.Add(new(PolicyReadableKind.Text, ")"));
    }

    private static void RenderLeaf(Dictionary<string, JsonElement> props, IReadOnlyDictionary<string, PolicyFieldSchema> fields,
        List<PolicyReadableSegment> output, ref bool unknown)
    {
        if (!props.TryGetValue("field", out var fieldEl) || fieldEl.ValueKind != JsonValueKind.String
            || !props.TryGetValue("operator", out var opEl) || opEl.ValueKind != JsonValueKind.String
            || !props.TryGetValue("value", out var valueEl))
        {
            output.Add(new(PolicyReadableKind.Text, UnknownCondition));
            unknown = true;
            return;
        }

        var key = fieldEl.GetString() ?? "";
        fields.TryGetValue(key, out var field);
        var op = opEl.GetString() ?? "";
        var value = valueEl.ValueKind == JsonValueKind.String ? valueEl.GetString() ?? "" : valueEl.GetRawText();

        output.Add(new(PolicyReadableKind.Field, $"«{field?.DisplayNameAr ?? key}»", key));
        output.Add(new(PolicyReadableKind.Text, " "));
        if (PolicyVocabularyAr.Operators.TryGetValue(op, out var opAr))
            output.Add(new(PolicyReadableKind.Operator, opAr, op));
        else
        {
            output.Add(new(PolicyReadableKind.Text, UnknownCondition));
            unknown = true;
            return;
        }

        output.Add(new(PolicyReadableKind.Text, " "));
        if (string.Equals(op, "In", StringComparison.OrdinalIgnoreCase))
        {
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            output.Add(new(PolicyReadableKind.Text, "("));
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    output.Add(new(PolicyReadableKind.Text, "، "));
                AddValue(output, key, field, parts[i]);
            }

            output.Add(new(PolicyReadableKind.Text, ")"));
        }
        else
        {
            AddValue(output, key, field, value);
        }
    }

    private static void AddValue(List<PolicyReadableSegment> output, string key, PolicyFieldSchema? field, string value)
    {
        var type = field?.Type?.ToLowerInvariant() ?? "string";
        if (type is "bool" or "boolean" && bool.TryParse(value, out var flag))
        {
            output.Add(new(PolicyReadableKind.Value, $"«{(flag ? Yes : No)}»", value));
            return;
        }

        if (type is "int" or "integer" or "decimal" or "number" or "long" or "double" && PolicyRuleJson.TryParseNumber(value, out var number))
        {
            output.Add(new(PolicyReadableKind.Value, number.ToString(CultureInfo.InvariantCulture), value));
            return;
        }

        if (string.Equals(key, "action", StringComparison.OrdinalIgnoreCase) && PolicyVocabularyAr.Actions.TryGetValue(value, out var action))
        {
            output.Add(new(PolicyReadableKind.Value, $"«{action}»", value));
            return;
        }

        output.Add(new(PolicyReadableKind.Value, $"«{value}»"));
    }

    // ------------------------------------------------------------------ effects

    private static void RenderEffects(JsonElement root, IReadOnlyDictionary<string, PolicyFieldSchema> fields, List<PolicyReadableSegment> output, ref bool unknown)
    {
        var effects = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList()
            : root.ValueKind == JsonValueKind.Object ? [root]
            : [];
        if (effects.Count == 0)
        {
            output.Add(new(PolicyReadableKind.Text, UnknownEffect));
            unknown = true;
            return;
        }

        for (var i = 0; i < effects.Count; i++)
        {
            if (i > 0)
                output.Add(new(PolicyReadableKind.Text, "، و"));
            RenderEffect(effects[i], fields, output, ref unknown);
        }
    }

    private static void RenderEffect(JsonElement el, IReadOnlyDictionary<string, PolicyFieldSchema> fields, List<PolicyReadableSegment> output, ref bool unknown)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            output.Add(new(PolicyReadableKind.Text, UnknownEffect));
            unknown = true;
            return;
        }

        var props = el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (!props.TryGetValue("effect", out var effectEl) || effectEl.ValueKind != JsonValueKind.String
            || !Enum.TryParse<PolicyEffect>(effectEl.GetString(), ignoreCase: false, out var effect)
            || !Enum.IsDefined(effect) || !PolicyVocabularyAr.Effects.TryGetValue(effect, out var verb))
        {
            output.Add(new(PolicyReadableKind.Text, UnknownEffect));
            unknown = true;
            return;
        }

        var parameters = props.TryGetValue("params", out var p) && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() ?? "" : x.Value.GetRawText(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        output.Add(new(PolicyReadableKind.Effect, verb, effect.ToString()));
        switch (effect)
        {
            case PolicyEffect.Deny when parameters.TryGetValue("reason", out var reason) && reason.Length > 0:
                output.Add(new(PolicyReadableKind.Text, " بسبب "));
                output.Add(new(PolicyReadableKind.Value, $"«{reason}»"));
                break;
            case PolicyEffect.TriggerWorkflow when parameters.TryGetValue("workflowCode", out var code):
                output.Add(new(PolicyReadableKind.Text, " "));
                output.Add(new(PolicyReadableKind.Value, $"«{PolicyVocabularyAr.Workflow(code)}»", code));
                break;
            case PolicyEffect.SetField when parameters.TryGetValue("field", out var target):
                fields.TryGetValue(target, out var targetField);
                output.Add(new(PolicyReadableKind.Text, " "));
                output.Add(new(PolicyReadableKind.Field, $"«{targetField?.DisplayNameAr ?? target}»", target));
                output.Add(new(PolicyReadableKind.Text, " على "));
                AddValue(output, target, targetField, parameters.GetValueOrDefault("value") ?? "");
                break;
            case PolicyEffect.SelectCodePolicy when parameters.TryGetValue("policyKey", out var policyKey):
                output.Add(new(PolicyReadableKind.Text, " "));
                output.Add(new(PolicyReadableKind.Value, $"«{policyKey}»", policyKey));
                break;
        }
    }
}
