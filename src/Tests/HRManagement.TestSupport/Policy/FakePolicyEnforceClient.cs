using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.TestSupport.Policy;

/// <summary>CAP-POL-01: inject deterministic policy decisions in integration tests (PLAN-02 / W2.3).</summary>
public sealed class FakePolicyEnforceClient : IPolicyEnforceClient
{
    private readonly List<PolicyEnforceRequest> _requests = [];

    public Func<PolicyEnforceRequest, PolicyEnforceResponse>? EnforceHandler { get; set; }

    public IReadOnlyList<PolicyEnforceRequest> Requests => _requests;

    /// <summary>The mode the default answer reports; a handler sets its own.</summary>
    public PolicyEnforcementMode EffectiveMode { get; set; } = PolicyEnforcementMode.Enforce;

    /// <summary>PLAN-42: a denial as the real client returns it when HR.Core cannot be reached under FailClosed.</summary>
    public static PolicyEnforceResponse FailedClosed(string domain, string reason = "http-503")
    {
        var message = $"policy {domain} could not be checked";
        return new PolicyEnforceResponse
        {
            Allowed = false,
            Degraded = true,
            FailureReason = reason,
            EffectiveMode = PolicyEnforcementMode.Enforce,
            Explanations = [message],
            Decision = new PolicyDecision { Allowed = false, DenyReason = message, Explanations = [message] }
        };
    }

    /// <summary>PLAN-42: Shadow as the real client returns it: the caller gets the Off decision, the server's is on the side.</summary>
    public static PolicyEnforceResponse Shadow(PolicyDecision offDecision, PolicyDecision serverDecision) => new()
    {
        Allowed = true,
        EffectiveMode = PolicyEnforcementMode.Shadow,
        Decision = offDecision,
        ShadowDecision = serverDecision
    };

    public Task<PolicyEnforceResponse> EnforceAsync(PolicyEnforceRequest request, CancellationToken ct = default)
    {
        _requests.Add(CloneRequest(request));
        if (EnforceHandler is not null)
            return Task.FromResult(EnforceHandler(request));

        var decision = new PolicyDecision
        {
            Allowed = true,
            Explanations = ["FakePolicyEnforceClient default allow"]
        };
        return Task.FromResult(new PolicyEnforceResponse
        {
            Allowed = decision.Allowed,
            EffectiveMode = EffectiveMode,
            Decision = decision,
            Explanations = decision.Explanations.ToList()
        });
    }

    private static PolicyEnforceRequest CloneRequest(PolicyEnforceRequest request) => new()
    {
        Domain = request.Domain,
        TenantId = request.TenantId,
        SubjectType = request.SubjectType,
        Action = request.Action,
        Facts = new Dictionary<string, string>(request.Facts, StringComparer.OrdinalIgnoreCase),
        CorrelationId = request.CorrelationId,
        Mode = request.Mode,
        EntityId = request.EntityId,
        ContextJson = request.ContextJson,
        ActorUserId = request.ActorUserId,
        ActorUserName = request.ActorUserName
    };
}

public static class FakePolicyEnforceClientExtensions
{
    public static IServiceCollection ReplacePolicyEnforceClientWithFake(this IServiceCollection services)
    {
        services.AddSingleton<FakePolicyEnforceClient>();
        services.AddSingleton<IPolicyEnforceClient>(sp => sp.GetRequiredService<FakePolicyEnforceClient>());
        return services;
    }
}
