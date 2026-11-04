using System.Security.Claims;
using HRManagement.SharedKernel.Policy;
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

        // PLAN-42: azp is set by Keycloak and decides when present. A signed-in user carries azp=hr-gateway-client, so a user who
        // is merely named service-account-x is not a service, and the gateway proof check can rely on this answer.
        var azp = user.FindFirst("azp")?.Value ?? user.FindFirst("client_id")?.Value;
        if (!string.IsNullOrWhiteSpace(azp))
            return ServiceClientIds.All.Contains(azp, StringComparer.OrdinalIgnoreCase);

        // Keycloak service accounts typically have preferred_username like "service-account-svc-payroll"
        var preferred = user.FindFirst("preferred_username")?.Value
            ?? user.Identity?.Name;
        return !string.IsNullOrWhiteSpace(preferred)
            && preferred.StartsWith("service-account-", StringComparison.OrdinalIgnoreCase);
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
                        ServiceClientIds.HrMovements, ServiceClientIds.AdminLogistics, ServiceClientIds.Compliance,
                        ServiceClientIds.Gateway))));

            options.AddPolicy(ServicePolicies.FileStorageReferences, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.FileStorageReferences)
                && IsAllowedClient(ctx.User,
                    ServiceClientIds.HrCore,
                    ServiceClientIds.HrContracts,
                    ServiceClientIds.HrTraining,
                    ServiceClientIds.IdCard,
                    ServiceClientIds.RecordsArchive,
                    ServiceClientIds.AdminLogistics,
                    // PLAN-37: complaint and legal-case attachments (ComplaintsController, ComplianceExtendedControllers).
                    ServiceClientIds.Compliance)));

            options.AddPolicy(ServicePolicies.DsarSubjectRead, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.DsarSubjectRead)
                && IsAllowedClient(ctx.User, ServiceClientIds.Compliance)));

            // HCM-027: HR.Core validates offer salaries against Payroll grade bounds.
            options.AddPolicy(ServicePolicies.PayrollGradeLookup, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.PayrollGradesRead)
                && IsAllowedClient(ctx.User, ServiceClientIds.HrCore)));

            // PLAN-30 (FS-030): HR.Core uploads candidate documents to File.Storage on behalf of anonymous candidates.
            options.AddPolicy(ServicePolicies.FileStorageServiceUpload, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.FileStorageServiceUpload)
                && IsAllowedClient(ctx.User, ServiceClientIds.HrCore)));

            // PLAN-42 (G-POL-08): the internal enforce endpoint; which domain a client owns is checked per request.
            options.AddPolicy(ServicePolicies.PolicyEnforceS2S, p => p.RequireAssertion(ctx =>
                ServiceCallerPrincipal.IsServiceAccount(ctx.User)
                && ServiceCallerPrincipal.HasScope(ctx.User, ServiceScopes.HrCoreWorkflow)
                && PolicyDomainOwners.IsOwnerOfAnyDomain(ServiceCallerPrincipal.GetClientId(ctx.User))));

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
