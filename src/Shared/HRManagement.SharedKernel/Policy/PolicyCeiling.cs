namespace HRManagement.SharedKernel.Policy;

/// <summary>An agency effect the ceiling dropped at evaluation, with its rule.</summary>
public sealed record PolicyCeilingDrop(string? RuleId, PolicyEffect Effect, string Reason);

/// <summary>An agency rule effect the ceiling refuses at publish.</summary>
public sealed record PolicyCeilingIssue(string Code, string MessageAr, int EffectIndex);

/// <summary>
/// PLAN-48 (§4, D-TEN-8 = D-POL-4): an agency inherits the national policy and may only make it stricter. It may add Deny and the
/// stricter effects (RequireApproval, RequireDualApproval, BlockUntilChecklist, RequirePrivacyMask, RequireOriginReceipt, and
/// RequireEvidence, which only adds a constraint); an Allow never lifts a national Deny (Deny wins in the evaluator). It may not:
/// <list type="bullet">
/// <item>set a field with SetField that the national policy sets;</item>
/// <item>trigger a workflow with TriggerWorkflow where the national policy triggers one;</item>
/// <item>use SelectCodePolicy at all.</item>
/// </list>
/// The ceiling applies twice: at publish (<see cref="CheckAgencyRule"/>, the rule is refused) and at evaluation (<see cref="Apply"/>,
/// the effect is dropped with an explanation), because older overlays may hold rules written before the check existed.
/// </summary>
public static class PolicyCeiling
{
    public const string ExplanationAr = "أُهمل أثر الجهة بسقف الوطني (D-POL-4)";

    public const string SelectCodePolicyCode = "CEILING_SELECT_CODE_POLICY";
    public const string SetFieldCode = "CEILING_SETFIELD";
    public const string TriggerWorkflowCode = "CEILING_TRIGGER_WORKFLOW";

    /// <summary>
    /// Evaluation: the effects of the matched rules (in priority order), with an agency's SetField of a field a matched national
    /// rule sets, an agency's TriggerWorkflow when a matched national rule triggers one, and every agency SelectCodePolicy dropped.
    /// </summary>
    public static (List<(PolicyRuleDefinition Rule, PolicyEffectDefinition Effect)> Kept, List<PolicyCeilingDrop> Dropped) Apply(
        IReadOnlyList<PolicyRuleDefinition> matched)
    {
        var nationalFields = new HashSet<string>(
            matched.Where(r => r.Origin == PolicyRuleOrigin.National)
                .SelectMany(r => r.Then)
                .Where(e => e.Effect == PolicyEffect.SetField)
                .Select(e => e.Params?.GetValueOrDefault("field") ?? string.Empty),
            StringComparer.Ordinal);
        var nationalTriggers = matched.Any(r => r.Origin == PolicyRuleOrigin.National && r.Then.Any(e => e.Effect == PolicyEffect.TriggerWorkflow));

        var kept = new List<(PolicyRuleDefinition, PolicyEffectDefinition)>();
        var dropped = new List<PolicyCeilingDrop>();
        foreach (var rule in matched)
        {
            foreach (var effect in rule.Then)
            {
                var reason = rule.Origin == PolicyRuleOrigin.Agency
                    ? DropReason(effect, nationalFields, nationalTriggers)
                    : null;
                if (reason is null)
                    kept.Add((rule, effect));
                else
                    dropped.Add(new PolicyCeilingDrop(rule.Id, effect.Effect, reason));
            }
        }

        return (kept, dropped);
    }

    /// <summary>Publish: what an agency rule's effects break of the ceiling, against the domain's published national policy.</summary>
    public static IReadOnlyList<PolicyCeilingIssue> CheckAgencyRule(
        IReadOnlyList<PolicyEffectDefinition> then,
        IReadOnlySet<string> nationalSetFields,
        bool nationalTriggersWorkflow)
    {
        var issues = new List<PolicyCeilingIssue>();
        for (var i = 0; i < then.Count; i++)
        {
            var effect = then[i];
            switch (effect.Effect)
            {
                case PolicyEffect.SelectCodePolicy:
                    issues.Add(new(SelectCodePolicyCode, "لا تختار الجهة سياسة ترميز (SelectCodePolicy): الترميز وطني (D-POL-4)", i));
                    break;
                case PolicyEffect.SetField when nationalSetFields.Contains(effect.Params?.GetValueOrDefault("field") ?? string.Empty):
                    issues.Add(new(SetFieldCode,
                        $"لا تغيّر الجهة الحقل «{effect.Params!["field"]}» الذي تضبطه السياسة الوطنية (D-POL-4)", i));
                    break;
                case PolicyEffect.TriggerWorkflow when nationalTriggersWorkflow:
                    issues.Add(new(TriggerWorkflowCode, "لا تطلق الجهة مساراً في مجال للسياسة الوطنية فيه مسار (D-POL-4)", i));
                    break;
            }
        }

        return issues;
    }

    private static string? DropReason(PolicyEffectDefinition effect, IReadOnlySet<string> nationalFields, bool nationalTriggers) =>
        effect.Effect switch
        {
            PolicyEffect.SelectCodePolicy => "SelectCodePolicy",
            PolicyEffect.SetField when nationalFields.Contains(effect.Params?.GetValueOrDefault("field") ?? string.Empty)
                => "SetField " + effect.Params!["field"],
            PolicyEffect.TriggerWorkflow when nationalTriggers => "TriggerWorkflow " + effect.Params?.GetValueOrDefault("workflowCode"),
            _ => null
        };
}
