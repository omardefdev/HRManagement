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
    /// <summary>PLAN-42: set by the caller inside HR.Core only; the internal endpoint derives it and ignores the body.</summary>
    public PolicyEnforcementMode Mode { get; set; } = PolicyEnforcementMode.Off;
    public Guid? EntityId { get; set; }
    public string? ContextJson { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }

    /// <summary>
    /// PLAN-47 (§6): evaluate the policy as it stood (or will stand) at this time, UTC. Honoured in HR.Core (EvaluateAsync,
    /// EnforceAsync, POST api/policies/evaluate, the simulation); PLAN-42's internal endpoint clears it.
    /// </summary>
    public DateTime? AsOfUtc { get; set; }
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

    /// <summary>PLAN-42: the mode the server applied. The remote client reads the reply by this value; a 200 without it is a failure.</summary>
    public PolicyEnforcementMode? EffectiveMode { get; init; }

    /// <summary>PLAN-42: HR.Core gave no usable decision, so this answer comes from the failure path.</summary>
    public bool Degraded { get; init; }

    /// <summary>PLAN-42: a fixed name for the failure (http-503, timeout, exception, invalid-response).</summary>
    public string? FailureReason { get; init; }

    /// <summary>PLAN-42: in Shadow, what the server decided. For the record only: no caller may act on it.</summary>
    public PolicyDecision? ShadowDecision { get; init; }
}

public sealed record PolicyVersionSnapshot
{
    public Guid VersionId { get; init; }
    public string Domain { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public int VersionNumber { get; init; }
    public List<PolicyRuleDefinition> Rules { get; init; } = [];
    public PolicyFactsSchemaDefinition? FactsSchema { get; init; }

    /// <summary>PLAN-47: stored rules the strict parser refuses. They stay in <see cref="Rules"/> and never match.</summary>
    public IReadOnlyList<string> InvalidRuleKeys { get; init; } = [];

    /// <summary>PLAN-47: the earliest EffectiveFrom of a published version after the evaluation time, if any (cache lifetime).</summary>
    public DateTime? NextEffectiveFrom { get; init; }
}
