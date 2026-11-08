using System.Globalization;
using System.Text.Json;

namespace HRManagement.SharedKernel.Workflow;

/// <summary>
/// Simple condition evaluator for workflow step gating.
/// Supports: GreaterThan, LessThan, Equals, Contains, In operators on a named field.
/// </summary>
public sealed record WorkflowCondition
{
    public string Field { get; init; } = string.Empty;
    public string Operator { get; init; } = "Equals";
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Evaluates the condition against a flat JSON dictionary of entity properties.
    /// Returns true if condition passes (step should execute).
    /// </summary>
    public bool Evaluate(Dictionary<string, string>? context)
    {
        if (context is null || !context.TryGetValue(Field, out var actual))
            return false;

        return Operator switch
        {
            "Equals" => string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
            "NotEquals" => !string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
            // PLAN-47: numbers are read with the invariant culture, never the machine's (ar-SA on the owner's machine).
            "GreaterThan" => TryNumber(actual, out var a) && TryNumber(Value, out var b) && a > b,
            "LessThan" => TryNumber(actual, out var la) && TryNumber(Value, out var lb) && la < lb,
            "GreaterThanOrEqual" => TryNumber(actual, out var ga) && TryNumber(Value, out var gb) && ga >= gb,
            "LessThanOrEqual" => TryNumber(actual, out var le) && TryNumber(Value, out var lv) && le <= lv,
            "Contains" => actual.Contains(Value, StringComparison.OrdinalIgnoreCase),
            "In" => Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Any(v => string.Equals(v, actual, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static bool TryNumber(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    /// <summary>Parses a context dictionary from a JSON payload string.</summary>
    public static Dictionary<string, string>? ParseContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ToString();
            }
            return dict;
        }
        catch
        {
            return null;
        }
    }
}
