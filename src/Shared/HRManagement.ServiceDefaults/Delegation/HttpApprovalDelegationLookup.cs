using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Delegation;

/// <summary>
/// PLAN-50 (§5, D-50-5, P-5): the delegations of a user, asked of HR.Core's internal endpoint with this service's own token
/// (<c>hr-core.lookup</c>) and the user's tenant in <c>X-Tenant-ID</c>, as <c>PolicyEnforceClient</c> carries it. The answer is kept
/// <c>Delegation:CacheSeconds</c> per tenant, user and employee, so a revocation, and a new delegation, act here within that time. Any
/// failure means no delegation and is not kept (fail-closed): it never stops a holder of the direct role, who never asks.
/// </summary>
public sealed class HttpApprovalDelegationLookup(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<HttpApprovalDelegationLookup> logger) : IApprovalDelegationLookup
{
    public const string HttpClientName = "HrCoreApprovalDelegations";
    public const string ActivePath = "api/internal/approval-delegations/active";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = DelegationIdentity.UserId(user);
        var employeeId = DelegationIdentity.EmployeeId(user);
        var tenantId = TenantIdFlow.Get()
                       ?? (Guid.TryParse(user.FindFirst(TenantContext.TenantClaimType)?.Value, out var claimed) ? claimed : null);
        if ((userId is null && employeeId is null) || tenantId is not Guid tenant || tenant == Guid.Empty)
            return [];

        var key = $"plan50:approval-delegations:{tenant:N}:{userId:N}:{employeeId:N}";
        if (cache.TryGetValue(key, out IReadOnlyList<ActiveDelegation>? cached) && cached is not null)
            return cached;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            if (client.BaseAddress is null)
                throw new InvalidOperationException($"{HttpClientName} has no base address; register it with {nameof(HrCoreEndpoint)} ({HrCoreEndpoint.Key}).");

            var query = new List<string>();
            if (userId is Guid u)
                query.Add("userId=" + u.ToString("D"));
            if (employeeId is Guid e)
                query.Add("employeeId=" + e.ToString("D"));
            using var request = new HttpRequestMessage(HttpMethod.Get, ActivePath + "?" + string.Join('&', query));
            // The tenant of the request this lookup serves, set explicitly (ForwardTenantHandler would add the same header).
            request.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenant.ToString("D"));

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("approval delegation lookup answered {StatusCode}; no delegation applies", (int)response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<ApprovalDelegationsResponse>(JsonOptions, ct);
            IReadOnlyList<ActiveDelegation> delegations = payload?.Delegations ?? [];
            var duration = DelegationOptions.CacheDuration(configuration);
            if (duration > TimeSpan.Zero)
                cache.Set(key, delegations, duration);
            return delegations;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "approval delegation lookup failed; no delegation applies");
            return [];
        }
    }
}

/// <summary>PLAN-50: the internal endpoint's answer.</summary>
public sealed record ApprovalDelegationsResponse(IReadOnlyList<ActiveDelegation> Delegations);

public static class HttpApprovalDelegationLookupExtensions
{
    /// <summary>
    /// PLAN-50 (P-7): the delegation lookup of a service other than HR.Core, and the acting context its approval actions read. Requires
    /// <c>Integration:HrCoreBaseUrl</c> and the service's own client credentials with <c>hr-core.lookup</c>.
    /// </summary>
    public static IServiceCollection AddHrCoreApprovalDelegationClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceEndpointWarnings();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddServiceAuthenticatedClient(HttpApprovalDelegationLookup.HttpClientName, HrCoreEndpoint.RequireBaseUri(configuration));
        services.AddScoped<IApprovalDelegationLookup, HttpApprovalDelegationLookup>();
        services.AddScoped<IApprovalActingContext, ApprovalActingContext>();
        return services;
    }
}
