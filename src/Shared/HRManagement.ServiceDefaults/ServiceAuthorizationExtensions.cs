using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>Helpers to recognize Keycloak service-account principals (IAM-004).</summary>
public static class ServiceCallerPrincipal
{
    public static bool IsServiceAccount(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        // Keycloak service accounts typically have preferred_username like "service-account-svc-payroll"
        var preferred = user.FindFirst("preferred_username")?.Value
            ?? user.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(preferred)
            && preferred.StartsWith("service-account-", StringComparison.OrdinalIgnoreCase))
            return true;

        var azp = user.FindFirst("azp")?.Value ?? user.FindFirst("client_id")?.Value;
        return !string.IsNullOrWhiteSpace(azp)
            && ServiceClientIds.All.Contains(azp, StringComparer.OrdinalIgnoreCase);
    }

    public static string? GetClientId(ClaimsPrincipal? user)
    {
        if (user is null)
            return null;
        return user.FindFirst("azp")?.Value
            ?? user.FindFirst("client_id")?.Value
            ?? ExtractFromPreferredUsername(user.FindFirst("preferred_username")?.Value);
    }

    public static bool HasScope(ClaimsPrincipal? user, string scope)
    {
        if (user is null || string.IsNullOrWhiteSpace(scope))
            return false;

        foreach (var claim in user.FindAll("scope").Concat(user.FindAll("scp")))
        {
            var parts = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Any(p => string.Equals(p, scope, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return user.HasClaim("scope", scope);
    }

    private static string? ExtractFromPreferredUsername(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred))
            return null;
        const string prefix = "service-account-";
        return preferred.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? preferred[prefix.Length..]
            : null;
    }
}

public static class ServiceAuthorizationExtensions
{
    /// <summary>Registers S2S authorization policies (IAM-004).</summary>
    public static IServiceCollection AddServiceToServiceAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ServicePolicies.DirectorySync, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.DirectorySync)
                && IsAllowedClient(ctx.User, ServiceClientIds.ItInfra, ServiceClientIds.HrCore, ServiceClientIds.AuthDirectory)));

            options.AddPolicy(ServicePolicies.HrCoreLookupS2S, p => p.RequireAssertion(ctx =>
                // Interactive UI users
                (ctx.User.Identity?.IsAuthenticated == true && !ServiceCallerPrincipal.IsServiceAccount(ctx.User))
                // Or service token with lookup scope
                || (ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                    && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.HrCoreLookup)
                    && IsAllowedClient(ctx.User, ServiceClientIds.Payroll, ServiceClientIds.HrContracts,
                        ServiceClientIds.HrMovements, ServiceClientIds.Gateway))));

            options.AddPolicy(ServicePolicies.FileStorageReferences, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.FileStorageReferences)
                && IsAllowedClient(ctx.User,
                    ServiceClientIds.HrCore,
                    ServiceClientIds.HrContracts,
                    ServiceClientIds.HrTraining,
                    ServiceClientIds.IdCard,
                    ServiceClientIds.RecordsArchive)));

            // EVD-003: CI/service accounts publish evidence; append-only (no update/delete policy).
            options.AddPolicy(ServicePolicies.EvidenceIngest, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.EvidenceIngest)));
        });

        return services;
    }

    private static bool IsAllowedClient(ClaimsPrincipal user, params string[] allowed)
    {
        var clientId = ServiceCallerPrincipal.GetClientId(user);
        return !string.IsNullOrWhiteSpace(clientId)
            && allowed.Any(a => string.Equals(a, clientId, StringComparison.OrdinalIgnoreCase));
    }
}
