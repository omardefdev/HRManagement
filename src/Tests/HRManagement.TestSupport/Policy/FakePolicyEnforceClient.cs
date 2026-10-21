using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.TestSupport.Policy;

/// <summary>CAP-POL-01: inject deterministic policy decisions in integration tests (PLAN-02 / W2.3).</summary>
public sealed class FakePolicyEnforceClient : IPolicyEnforceClient
{
    private readonly List<PolicyEnforceRequest> _requests = [];
    private readonly Dictionary<string, PolicyVersionSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public Func<PolicyEnforceRequest, PolicyEnforceResponse>? EnforceHandler { get; set; }

    public IReadOnlyList<PolicyEnforceRequest> Requests => _requests;

    public void SetSnapshot(string domain, PolicyVersionSnapshot snapshot) =>
        _snapshots[domain] = snapshot;

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
            Allowed = request.Mode == PolicyEnforcementMode.Shadow || decision.Allowed,
            Decision = decision,
            Explanations = decision.Explanations.ToList()
        });
    }

    public Task<PolicyVersionSnapshot?> GetSnapshotAsync(string domain, Guid? tenantId, CancellationToken ct = default)
    {
        _snapshots.TryGetValue(domain, out var snap);
        return Task.FromResult(snap);
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
