using HRManagement.SharedKernel.Policy;

namespace HRManagement.ServiceDefaults.Policy;

/// <summary>Thin gateway: enforce policy and map decision to domain error message.</summary>
public static class PolicyDomainGateway
{
    public static async Task<(bool Ok, string? Error, PolicyEnforceResponse Response)> EnforceOrErrorAsync(
        IPolicyEnforceClient client,
        PolicyEnforceRequest request,
        CancellationToken ct = default)
    {
        var response = await client.EnforceAsync(request, ct);
        if (response.Allowed)
            return (true, null, response);
        return (false, response.Decision.DenyReason ?? string.Join("; ", response.Explanations), response);
    }
}
