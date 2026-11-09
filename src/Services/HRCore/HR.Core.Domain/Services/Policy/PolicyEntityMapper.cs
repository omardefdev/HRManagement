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

    /// <summary>
    /// PLAN-47 (G-POL-06): a stored rule the strict parser refuses never matches (<see cref="PolicyConditionGroup.Never"/>, no
    /// effect) and its key is listed in <see cref="PolicyVersionSnapshot.InvalidRuleKeys"/>. It used to become "match everything"
    /// (an odd When) or "no effect" (an odd Then).
    /// </summary>
    public static PolicyVersionSnapshot ToSnapshot(
        PolicyTemplate template,
        PolicyTemplateVersion version,
        PolicyFactsSchemaDefinition? schema = null)
    {
        var invalid = new List<string>();
        var rules = new List<PolicyRuleDefinition>();
        // PLAN-48 (§4): an override's rules are the agency's, so the ceiling bounds them (PolicyCeiling).
        var origin = template.Scope == PolicyTemplateScope.TenantOverride ? PolicyRuleOrigin.Agency : PolicyRuleOrigin.National;
        foreach (var r in version.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal))
        {
            var issues = new List<PolicyRuleIssue>();
            var when = PolicyRuleJson.ParseWhenStrict(r.WhenJson, issues);
            var then = PolicyRuleJson.ParseThenStrict(r.ThenJson, issues);
            var valid = when is not null && then is not null;
            if (!valid)
                invalid.Add(r.RuleKey ?? r.Id.ToString());

            rules.Add(new PolicyRuleDefinition
            {
                Id = r.RuleKey,
                Name = r.Name,
                Priority = r.Priority,
                When = valid ? when! : PolicyConditionGroup.Never,
                Then = valid ? then! : [],
                Origin = origin
            });
        }

        return new PolicyVersionSnapshot
        {
            VersionId = version.Id,
            Domain = template.Domain,
            TenantId = template.Scope == PolicyTemplateScope.TenantOverride ? template.TenantId : null,
            VersionNumber = version.VersionNumber,
            Rules = rules,
            FactsSchema = schema,
            InvalidRuleKeys = invalid
        };
    }

    /// <summary>The strict reading of a stored When; a refused one is <see cref="PolicyConditionGroup.Never"/>.</summary>
    public static PolicyConditionGroup DeserializeWhen(string json) =>
        PolicyRuleJson.ParseWhenStrict(json, []) ?? PolicyConditionGroup.Never;

    /// <summary>The strict reading of a stored Then; a refused one has no effect (and its rule never matches in a snapshot).</summary>
    public static List<PolicyEffectDefinition> DeserializeThen(string json) =>
        PolicyRuleJson.ParseThenStrict(json, []) ?? [];

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
