namespace HRManagement.SharedKernel.Policy;

public static class PolicyEvaluator
{
    public static PolicyDecision Evaluate(
        PolicyVersionSnapshot snapshot,
        IReadOnlyDictionary<string, string> facts,
        string? action = null)
    {
        if (snapshot.FactsSchema is not null)
        {
            var schemaErrors = PolicyFactsValidator.Validate(snapshot.FactsSchema, facts);
            if (schemaErrors.Count > 0)
            {
                return new PolicyDecision
                {
                    Allowed = false,
                    DenyReason = string.Join("; ", schemaErrors),
                    PolicyVersionId = snapshot.VersionId,
                    Explanations = schemaErrors.ToList()
                };
            }
        }

        var matched = snapshot.Rules
            .OrderBy(r => r.Priority)
            .Where(r => r.When.Evaluate(facts))
            .ToList();

        if (matched.Count == 0)
        {
            return new PolicyDecision
            {
                Allowed = true,
                PolicyVersionId = snapshot.VersionId,
                Explanations = ["لا تطابق — السماح الافتراضي"]
            };
        }

        var effects = new List<PolicyEffectDefinition>();
        var explanations = new List<string>();
        var ruleIds = new List<string>();
        var denied = false;
        string? denyReason = null;

        foreach (var rule in matched)
        {
            ruleIds.Add(rule.Id ?? rule.Name ?? $"P{rule.Priority}");
            explanations.Add($"قاعدة {rule.Name ?? rule.Id ?? rule.Priority.ToString()}: تطابق");

            foreach (var effect in rule.Then)
            {
                effects.Add(effect);
                if (effect.Effect == PolicyEffect.Deny)
                {
                    denied = true;
                    denyReason = effect.Params?.GetValueOrDefault("reason") ?? "مرفوض بالسياسة";
                }
            }
        }

        if (denied)
        {
            return new PolicyDecision
            {
                Allowed = false,
                DenyReason = denyReason,
                Effects = effects.Select(e => e.Effect).Distinct().ToList(),
                EffectDetails = effects,
                MatchedRuleIds = ruleIds,
                Explanations = explanations,
                PolicyVersionId = snapshot.VersionId
            };
        }

        var distinctEffects = effects.Select(e => e.Effect).Distinct().ToList();
        if (!distinctEffects.Contains(PolicyEffect.Allow) && distinctEffects.Count > 0)
            distinctEffects.Insert(0, PolicyEffect.Allow);

        return new PolicyDecision
        {
            Allowed = true,
            Effects = distinctEffects,
            EffectDetails = effects,
            MatchedRuleIds = ruleIds,
            Explanations = explanations,
            PolicyVersionId = snapshot.VersionId
        };
    }

    public static PolicyVersionSnapshot MergeNationalAndTenant(
        PolicyVersionSnapshot? national,
        PolicyVersionSnapshot? tenant)
    {
        if (national is null && tenant is null)
            return new PolicyVersionSnapshot { Domain = tenant?.Domain ?? national?.Domain ?? "" };

        if (tenant is null)
            return national!;

        if (national is null)
            return tenant;

        return new PolicyVersionSnapshot
        {
            VersionId = tenant.VersionId,
            Domain = national.Domain,
            TenantId = tenant.TenantId,
            VersionNumber = tenant.VersionNumber,
            FactsSchema = tenant.FactsSchema ?? national.FactsSchema,
            Rules = national.Rules.Concat(tenant.Rules).OrderBy(r => r.Priority).ToList()
        };
    }
}
