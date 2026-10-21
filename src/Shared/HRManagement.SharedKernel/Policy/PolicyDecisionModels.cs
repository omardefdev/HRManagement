namespace HRManagement.SharedKernel.Policy;

public sealed class PolicyDecision
{
    public bool Allowed { get; init; } = true;
    public IReadOnlyList<PolicyEffect> Effects { get; init; } = [];
    public IReadOnlyList<PolicyEffectDefinition> EffectDetails { get; init; } = [];
    public IReadOnlyList<string> MatchedRuleIds { get; init; } = [];
    public IReadOnlyList<string> Explanations { get; init; } = [];
    public Guid? PolicyVersionId { get; init; }
    public string? DenyReason { get; init; }

    public string? GetWorkflowCode() =>
        EffectDetails.FirstOrDefault(e => e.Effect == PolicyEffect.TriggerWorkflow)
            ?.Params?.GetValueOrDefault("workflowCode");

    public string? GetCodePolicyKey() =>
        EffectDetails.FirstOrDefault(e => e.Effect == PolicyEffect.SelectCodePolicy)
            ?.Params?.GetValueOrDefault("policyKey");

    public string? GetSetFieldValue(string fieldName) =>
        EffectDetails
            .Where(e => e.Effect == PolicyEffect.SetField && e.Params?.GetValueOrDefault("field") == fieldName)
            .Select(e => e.Params?.GetValueOrDefault("value"))
            .LastOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed class PolicyEnforceRequest
{
    public string Domain { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? CorrelationId { get; set; }
    public PolicyEnforcementMode Mode { get; set; } = PolicyEnforcementMode.Enforce;
    public Guid? EntityId { get; set; }
    public string? ContextJson { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
}

public sealed class PolicyEnforceResponse
{
    public bool Allowed { get; init; }
    public IReadOnlyList<string> Effects { get; init; } = [];
    public Guid? WorkflowInstanceId { get; init; }
    public IReadOnlyList<string> Explanations { get; init; } = [];
    public Guid? PolicyVersionId { get; init; }
    public Guid? LedgerId { get; init; }
    public PolicyDecision Decision { get; init; } = new();
    public IReadOnlyList<string>? ShadowDiffs { get; init; }
}

public sealed record PolicyVersionSnapshot
{
    public Guid VersionId { get; init; }
    public string Domain { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public int VersionNumber { get; init; }
    public List<PolicyRuleDefinition> Rules { get; init; } = [];
    public PolicyFactsSchemaDefinition? FactsSchema { get; init; }
}
