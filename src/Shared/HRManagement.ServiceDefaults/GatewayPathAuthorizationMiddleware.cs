using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using HRManagement.ServiceDefaults.ModuleCatalog;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Enforces module policies for YARP paths /api/{segment}/ and /ui/{segment}/ on the gateway.
/// </summary>
public sealed class GatewayPathAuthorizationMiddleware(RequestDelegate next)
{
    private static readonly (string Segment, string Policy)[] SegmentPolicies =
    [
        ("hr-core", HrManagementPolicies.ModuleHrCore),
        ("hr-contracts", HrManagementPolicies.ModuleHrContracts),
        ("hr-health", HrManagementPolicies.ModuleHrHealth),
        ("hr-movements", HrManagementPolicies.ModuleHrMovements),
        ("hr-training", HrManagementPolicies.ModuleHrTraining),
        ("payroll", HrManagementPolicies.ModulePayroll),
        ("idcard", HrManagementPolicies.ModuleIdCard),
        ("records-archive", HrManagementPolicies.ModuleRecordsArchive),
        ("admin-logistics", HrManagementPolicies.ModuleAdminLogistics),
        ("it-infra", HrManagementPolicies.ModuleItInfra),
        ("dev-analytics", HrManagementPolicies.ModuleDevAnalytics),
        ("compliance", HrManagementPolicies.ModuleCompliance),
        ("auth-directory", HrManagementPolicies.ModuleAuthDirectory),
        ("files", HrManagementPolicies.ModuleFileStorage),
        ("file-storage", HrManagementPolicies.ModuleFileStorage),
        ("ess", HrManagementPolicies.ModuleEss),
    ];

    public async Task InvokeAsync(
        HttpContext context,
        IAuthorizationService authorization,
        IModuleEntitlementGuard? entitlementGuard = null,
        IOptions<ModuleCatalogOptions>? moduleCatalogOptions = null)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;

        // SEC-001: upstream health proxies — allow loopback callers (internal probe) without auth.
        string effectivePolicy;
        string moduleSegment = "";
        if (IsUpstreamHealthPath(path))
        {
            if (IsLoopbackRequest(context))
            {
                await next(context);
                return;
            }
            effectivePolicy = HrManagementPolicies.GatewayDashboard;
        }
        else if (!TryGetPolicy(path, out moduleSegment, out var modulePolicy))
        {
            await next(context);
            return;
        }
        else
        {
            effectivePolicy = TryGetEssPolicy(path, out var essPolicy)
                ? essPolicy
                : TryGetFineGrainedPolicy(moduleSegment, method, path, out var fineGrained)
                    ? fineGrained
                    : modulePolicy;
        }

        var opts = moduleCatalogOptions?.Value;
        if (opts?.EnforcePathEntitlement == true
            && entitlementGuard is not null
            && !string.IsNullOrEmpty(moduleSegment)
            && !string.Equals(moduleSegment, "ess", StringComparison.OrdinalIgnoreCase)
            && !IsUpstreamHealthPath(path))
        {
            if (!entitlementGuard.IsModuleExposed(moduleSegment))
            {
                if (context.User.Identity?.IsAuthenticated != true)
                {
                    if (KeycloakExtensions.HasBearerAuthorization(context.Request))
                        await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
                    else
                        await context.ChallengeAsync();
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: module not entitled (STR-003).");
                return;
            }
        }

        var result = await authorization.AuthorizeAsync(context.User, resource: null, effectivePolicy);
        if (result.Succeeded)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            if (KeycloakExtensions.HasBearerAuthorization(context.Request))
                await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
            else
                await context.ChallengeAsync();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Forbidden: missing role for this module.");
    }

    /// <summary>
    /// Returns a fine-grained policy for modules that support read/write differentiation.
    /// Currently only hr-core: GET → HrCoreReadOnly, POST/PUT/DELETE → HrCoreWrite.
    /// </summary>
    public static bool TryGetFineGrainedPolicy(string moduleSegment, string httpMethod, PathString path, out string policy)
    {
        policy = "";
        var pathValue = path.Value ?? "";

        if (string.Equals(moduleSegment, "hr-core", StringComparison.OrdinalIgnoreCase))
        {
            // Shared pickers: any authenticated user (controller enforces HrCoreLookup; no NationalId returned).
            if (pathValue.Contains("/api/lookup", StringComparison.OrdinalIgnoreCase)
                && string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
            {
                policy = HrManagementPolicies.HrCoreLookup;
                return true;
            }

            if (string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
            {
                policy = HrManagementPolicies.HrCoreReadOnly;
                return true;
            }

            if (IsMutating(httpMethod))
            {
                policy = HrManagementPolicies.HrCoreWrite;
                return true;
            }
        }

        if (string.Equals(moduleSegment, "hr-contracts", StringComparison.OrdinalIgnoreCase) && IsMutating(httpMethod))
        {
            if (pathValue.Contains("/HireWizard", StringComparison.OrdinalIgnoreCase))
                policy = HrManagementPolicies.HrContractsHireExecute;
            else
                policy = HrManagementPolicies.HrContractsApprove;
            return true;
        }

        if (string.Equals(moduleSegment, "payroll", StringComparison.OrdinalIgnoreCase) && IsMutating(httpMethod))
        {
            policy = HrManagementPolicies.PayrollProcess;
            return true;
        }

        return false;
    }

    private static bool TryGetEssPolicy(PathString path, out string policy)
    {
        policy = "";
        var p = path.Value ?? "";
        if (!p.Contains("/Ess", StringComparison.OrdinalIgnoreCase))
            return false;

        if (p.StartsWith("/ui/payroll", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/api/payroll", StringComparison.OrdinalIgnoreCase))
        {
            policy = HrManagementPolicies.ModuleEss;
            return true;
        }

        return false;
    }

    private static bool IsUpstreamHealthPath(PathString path)
    {
        var p = path.Value ?? "";
        return p.StartsWith("/health/upstreams/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null) return false;
        return System.Net.IPAddress.IsLoopback(remoteIp)
            || remoteIp.Equals(context.Connection.LocalIpAddress);
    }

    private static bool IsMutating(string httpMethod) =>
        string.Equals(httpMethod, HttpMethods.Post, StringComparison.OrdinalIgnoreCase)
        || string.Equals(httpMethod, HttpMethods.Put, StringComparison.OrdinalIgnoreCase)
        || string.Equals(httpMethod, HttpMethods.Delete, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPolicy(PathString path, out string moduleSegment, out string policyName)
    {
        moduleSegment = "";
        policyName = "";
        var p = path.Value;
        if (string.IsNullOrEmpty(p))
            return false;

        ReadOnlySpan<char> span = p.AsSpan().TrimEnd('/');
        ReadOnlySpan<char> rest;
        if (span.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            rest = span[5..];
        else if (span.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase))
            rest = span[4..];
        else
            return false;

        var slash = rest.IndexOf('/');
        var segment = slash >= 0 ? rest[..slash] : rest;
        if (segment.IsEmpty)
            return false;

        var segmentStr = segment.ToString();
        foreach (var (seg, policy) in SegmentPolicies)
        {
            if (string.Equals(seg, segmentStr, StringComparison.OrdinalIgnoreCase))
            {
                moduleSegment = seg;
                policyName = policy;
                return true;
            }
        }

        return false;
    }
}

public static class GatewayPathAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayPathAuthorization(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayPathAuthorizationMiddleware>();
}
