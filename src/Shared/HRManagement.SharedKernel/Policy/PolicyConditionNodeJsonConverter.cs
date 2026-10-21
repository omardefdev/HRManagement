using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRManagement.SharedKernel.Policy;

public sealed class PolicyConditionNodeJsonConverter : JsonConverter<PolicyConditionNode>
{
    public override PolicyConditionNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        return ParseElement(root);
    }

    public override void Write(Utf8JsonWriter writer, PolicyConditionNode value, JsonSerializerOptions options)
    {
        if (value.Leaf is not null)
        {
            JsonSerializer.Serialize(writer, value.Leaf, options);
            return;
        }

        if (value.Group is not null)
        {
            JsonSerializer.Serialize(writer, value.Group, options);
            return;
        }

        writer.WriteStartObject();
        writer.WriteEndObject();
    }

    internal static PolicyConditionNode ParseElement(JsonElement el)
    {
        if (TryGetProperty(el, "field", out _))
        {
            var leaf = new PolicyConditionLeaf
            {
                Field = GetString(el, "field"),
                Operator = GetString(el, "operator", "Equals"),
                Value = GetString(el, "value")
            };
            return new PolicyConditionNode { Leaf = leaf };
        }

        if (TryGetProperty(el, "all", out _)
            || TryGetProperty(el, "any", out _)
            || TryGetProperty(el, "not", out _))
        {
            var group = new PolicyConditionGroup();
            if (TryGetProperty(el, "all", out var allEl))
                group.All = ParseNodeList(allEl);
            if (TryGetProperty(el, "any", out var anyEl))
                group.Any = ParseNodeList(anyEl);
            if (TryGetProperty(el, "not", out var notEl))
                group.Not = ParseElement(notEl);
            return new PolicyConditionNode { Group = group };
        }

        return new PolicyConditionNode { Group = new PolicyConditionGroup() };
    }

    private static List<PolicyConditionNode> ParseNodeList(JsonElement arr)
    {
        var list = new List<PolicyConditionNode>();
        foreach (var item in arr.EnumerateArray())
            list.Add(ParseElement(item));
        return list;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string name, string defaultValue = "") =>
        TryGetProperty(element, name, out var value) ? value.ToString() : defaultValue;
}
