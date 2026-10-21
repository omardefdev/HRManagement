namespace HRManagement.SharedKernel.Policy;

public sealed class PolicyFieldSchema
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string? DisplayNameAr { get; set; }
    public bool Required { get; set; }
}

public sealed class PolicyFactsSchemaDefinition
{
    public string Domain { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public List<PolicyFieldSchema> Fields { get; set; } = [];
}

public static class PolicyFactsValidator
{
    public static IReadOnlyList<string> Validate(
        PolicyFactsSchemaDefinition schema,
        IReadOnlyDictionary<string, string> facts)
    {
        var errors = new List<string>();
        var allowed = new HashSet<string>(schema.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var key in facts.Keys)
        {
            if (!allowed.Contains(key))
                errors.Add($"حقل غير مسموح في schema: {key}");
        }

        foreach (var field in schema.Fields.Where(f => f.Required))
        {
            if (!facts.TryGetValue(field.Name, out var v) || string.IsNullOrWhiteSpace(v))
                errors.Add($"حقل مطلوب: {field.DisplayNameAr ?? field.Name}");
        }

        return errors;
    }
}
