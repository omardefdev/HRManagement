using System.Text.Json;
using HRManagement.SharedKernel.Policy;
using HR.Core.Domain.Entities.Policy;

namespace HR.Core.Domain.Services.Policy;

public static class PolicyEntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new PolicyConditionNodeJsonConverter() }
    };

    public static PolicyVersionSnapshot ToSnapshot(
        PolicyTemplate template,
        PolicyTemplateVersion version,
        PolicyFactsSchemaDefinition? schema = null)
    {
        var rules = version.Rules
            .OrderBy(r => r.Priority)
            .Select(r => new PolicyRuleDefinition
            {
                Id = r.RuleKey,
                Name = r.Name,
                Priority = r.Priority,
                When = DeserializeWhen(r.WhenJson),
                Then = DeserializeThen(r.ThenJson)
            })
            .ToList();

        return new PolicyVersionSnapshot
        {
            VersionId = version.Id,
            Domain = template.Domain,
            TenantId = template.Scope == PolicyTemplateScope.TenantOverride ? template.TenantId : null,
            VersionNumber = version.VersionNumber,
            Rules = rules,
            FactsSchema = schema
        };
    }

    public static PolicyConditionGroup DeserializeWhen(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new PolicyConditionGroup();

        try
        {
            var node = JsonSerializer.Deserialize<PolicyConditionNode>(json, JsonOptions);
            return node?.Group ?? WrapLeafAsGroup(node);
        }
        catch
        {
            return JsonSerializer.Deserialize<PolicyConditionGroup>(json, JsonOptions) ?? new PolicyConditionGroup();
        }
    }

    private static PolicyConditionGroup WrapLeafAsGroup(PolicyConditionNode? node)
    {
        if (node?.Leaf is null)
            return new PolicyConditionGroup();
        return new PolicyConditionGroup { All = [node] };
    }

    public static List<PolicyEffectDefinition> DeserializeThen(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<PolicyEffectDefinition>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static PolicyFactsSchemaDefinition? ParseSchema(PolicyFactsSchemaEntity? entity)
    {
        if (entity is null) return null;
        var fields = JsonSerializer.Deserialize<List<PolicyFieldSchema>>(entity.FieldsJson, JsonOptions) ?? [];
        return new PolicyFactsSchemaDefinition
        {
            Domain = entity.Domain,
            SubjectType = entity.SubjectType,
            Fields = fields
        };
    }
}
