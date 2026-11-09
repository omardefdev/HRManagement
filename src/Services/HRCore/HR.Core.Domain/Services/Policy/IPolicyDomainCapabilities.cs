using HRManagement.SharedKernel.Policy;

namespace HR.Core.Domain.Services.Policy;

/// <summary>
/// PLAN-47: what a policy domain may do: the effects it accepts, the fields a SetField may write and the workflow codes a
/// TriggerWorkflow may name. The default allows everything; PLAN-49 fills it from the domain catalog.
/// </summary>
public interface IPolicyDomainCapabilities
{
    bool AllowsEffect(string domain, PolicyEffect effect);

    bool AllowsSetField(string domain, string field);

    bool AllowsWorkflowCode(string domain, string workflowCode);
}

public sealed class AllowAllPolicyDomainCapabilities : IPolicyDomainCapabilities
{
    public static readonly AllowAllPolicyDomainCapabilities Instance = new();

    public bool AllowsEffect(string domain, PolicyEffect effect) => true;

    public bool AllowsSetField(string domain, string field) => true;

    public bool AllowsWorkflowCode(string domain, string workflowCode) => true;
}
