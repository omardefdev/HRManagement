using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using HRManagement.ServiceDefaults.ModuleCatalog;
using HRManagement.SharedKernel.Tenancy;

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
        IOptions<ModuleCatalogOptions>? moduleCatalogOptions = null,
        IOptions<GatewayEssOptions>? essOptions = null)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;

        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

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
            // PLAN-51 (§1, P-3): the self-service decision comes first, by exact segment. On the ess segment it is final: nothing under
            // /ui/ess reaches PLAN-50's table, Gateway.ApprovalReach or any other rule, and what is not exposed answers 404 here.
            switch (GatewayEssRouting.Decide(method, path.Value ?? "", essOptions?.Value ?? new GatewayEssOptions()))
            {
                case GatewayEssDecision.Ess:
                    effectivePolicy = HrManagementPolicies.ModuleEss;
                    break;
                case GatewayEssDecision.StaticFile:
                    effectivePolicy = HrManagementPolicies.GatewayAuthenticated;
                    break;
                case GatewayEssDecision.NotExposed:
                    context.Items[GatewayEssOptions.AuditDecisionItemKey] = GatewayEssOptions.NotExposedDecision;
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                default:
                    effectivePolicy = TryGetFineGrainedPolicy(moduleSegment, method, path, out var fineGrained)
                        ? fineGrained
                        : modulePolicy;
                    break;
            }
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
            if (RequiresTenantClaim(context, path, moduleSegment)
                && !HasValidTenantClaim(context.User))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: missing or invalid tenant_id claim.");
                return;
            }

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

    /// <summary>PLAN-47: the policy lifecycle pages and API of HR.Core.</summary>
    private static bool TryGetPolicyLifecyclePolicy(string httpMethod, string pathValue, out string policy)
    {
        policy = "";
        var ui = pathValue.StartsWith("/ui/hr-core/PolicyTemplates", StringComparison.OrdinalIgnoreCase);
        var api = pathValue.Contains("/api/policies/", StringComparison.OrdinalIgnoreCase)
                  && !pathValue.Contains("/api/internal/", StringComparison.OrdinalIgnoreCase);
        if (!ui && !api)
            return false;

        // PLAN-50 (P-4): the two publish actions (PolicyApprover) are rows of GatewayHrCoreActionPolicies, matched before this rule.
        if (IsMutating(httpMethod))
        {
            if (ui && !pathValue.EndsWith("/PolicyTemplates/Simulate", StringComparison.OrdinalIgnoreCase))
            {
                policy = HrManagementPolicies.PolicyAuthor;
                return true;
            }

            if (api && (pathValue.Contains("/api/policies/versions/", StringComparison.OrdinalIgnoreCase)
                        || PolicyApiDrafts.IsMatch(pathValue)))
            {
                policy = HrManagementPolicies.PolicyAuthor;
                return true;
            }

            return false;
        }

        if (string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase) && !IsHrCoreStaticAsset(pathValue))
        {
            policy = HrManagementPolicies.PolicyReader;
            return true;
        }

        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex PolicyApiDrafts = new(
        @"/api/policies/(templates/[0-9a-fA-F-]{36}/(drafts|rollback-drafts)|drafts)/?$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

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
            // PLAN-50 (§3, P-4): the one table of HR.Core's approval exceptions (HrCore.Approve, SoD.ManageDelegations, and PLAN-47's
            // publish), matched by exact segment before every other rule; no row overlaps another rule.
            if (GatewayHrCoreActionPolicies.TryGetPolicy(httpMethod, pathValue, out policy))
                return true;

            // PLAN-50 (§4, D-50-3): the workflow step actions admit any interactive staff user, matched by segment; WorkflowStepAction and
            // the evaluator decide in the service (a delegate may hold no operational role).
            if (GatewayApprovalPaths.IsHrCoreWorkflowApprovalPath(httpMethod, pathValue))
            {
                policy = HrManagementPolicies.GatewayApprovalReach;
                return true;
            }

            // Platform reconciliation is governed by the approval policy for reads and writes.
            if (pathValue.Contains("/api/tenant-quarantine", StringComparison.OrdinalIgnoreCase))
            {
                policy = HrManagementPolicies.TenantQuarantineReconcile;
                return true;
            }

            // PLAN-43 (G-WF-04): definitions have their own readers and writers, and hr-core is neither a writer nor needed to read.
            if (pathValue.Contains("/api/workflow-definitions", StringComparison.OrdinalIgnoreCase))
            {
                policy = IsMutating(httpMethod)
                    ? HrManagementPolicies.WorkflowDefinitionAdmin
                    : HrManagementPolicies.WorkflowDefinitionRead;
                return true;
            }

            // PLAN-47 (D-POL-3): publishing a policy version is the approver's, and writing a draft the author's; hr-core is no
            // longer both. Reading the pages admits both. Evaluate and simulate keep the default HR.Core rules below.
            if (TryGetPolicyLifecyclePolicy(httpMethod, pathValue, out policy))
                return true;

            // Shared pickers and table hydration: interactive staff and services with hr-core.lookup (PLAN-51, D-51-3); the controller
            // enforces the same HrCoreLookup.
            if (pathValue.Contains("/api/lookup", StringComparison.OrdinalIgnoreCase))
            {
                policy = HrManagementPolicies.HrCoreLookup;
                return true;
            }

            // HCM-028 (PLAN-25 slice): the compliance reviewer opens appeals and the work inbox; the controllers
            // keep HrCoreWrite on the HR-only appeal actions. Static assets carry no data and back their pages.
            if (pathValue.StartsWith("/ui/hr-core/RecruitmentAppeals", StringComparison.OrdinalIgnoreCase))
            {
                policy = HrManagementPolicies.HrCoreAppealReview;
                return true;
            }

            // HCM-031 (PLAN-31): the exam marker reaches the anonymous marking screens only (GET and POST of marks).
            if (pathValue.StartsWith("/ui/hr-core/ExamMarking", StringComparison.OrdinalIgnoreCase))
            {
                policy = HrManagementPolicies.HrCoreExamMarking;
                return true;
            }

            // PLAN-45: the generic approval page. Its two step actions are matched above (PLAN-50); the inbox audience reads it, and any other
            // POST under it keeps the step policy. The controller narrows both to the instance.
            if (pathValue.StartsWith("/ui/hr-core/WorkflowApprovals", StringComparison.OrdinalIgnoreCase))
            {
                policy = IsMutating(httpMethod) ? HrManagementPolicies.WorkflowStepAction : HrManagementPolicies.HrCoreWorkInbox;
                return true;
            }

            if (string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
            {
                // PLAN-51 (§2, P-4): HR.Core's static files carry no data and back the self-service and marking pages, whose users are not
                // staff: any signed-in user, not the staff-only lookup.
                policy = pathValue.StartsWith("/ui/hr-core/WorkInbox", StringComparison.OrdinalIgnoreCase)
                    ? HrManagementPolicies.HrCoreWorkInbox
                    : IsHrCoreStaticAsset(pathValue)
                        ? HrManagementPolicies.GatewayAuthenticated
                        : HrManagementPolicies.HrCoreReadOnly;
                return true;
            }

            if (IsMutating(httpMethod))
            {
                // PLAN-43 (G-WF-05): the rest of this API (its step actions are matched above) is for the owning service, which calls
                // HR.Core directly: no user passes.
                policy = pathValue.Contains("/api/workflow-instances", StringComparison.OrdinalIgnoreCase)
                    ? HrManagementPolicies.WorkflowExternalStart
                    : HrManagementPolicies.HrCoreWrite;
                return true;
            }
        }

        // PLAN-50 (§2, P-3): the four approval screens of the five movement types and the module's static files admit any interactive staff
        // user, because a delegate may hold no module role (D-50-3); HrMovements.ApprovalAccess and HrManagement.Approve decide in the
        // service. Every other path of the module keeps Module.HrMovements.
        if (string.Equals(moduleSegment, GatewayApprovalPaths.MovementsModule, StringComparison.OrdinalIgnoreCase))
        {
            if (GatewayApprovalPaths.IsMovementApprovalPath(httpMethod, pathValue)
                || GatewayApprovalPaths.IsModuleStaticAsset(GatewayApprovalPaths.MovementsModule, httpMethod, pathValue))
            {
                policy = HrManagementPolicies.GatewayApprovalReach;
                return true;
            }

            return false;
        }

        if (string.Equals(moduleSegment, "hr-contracts", StringComparison.OrdinalIgnoreCase)
            && pathValue.Contains("/HireWizard", StringComparison.OrdinalIgnoreCase))
        {
            if (IsMutating(httpMethod)
                && pathValue.Contains("/Step3Approve", StringComparison.OrdinalIgnoreCase))
                policy = HrManagementPolicies.HrManagementApprove;
            else if (IsMutating(httpMethod)
                     && pathValue.Contains("/Step3SubmitForApproval", StringComparison.OrdinalIgnoreCase))
                policy = HrManagementPolicies.HrContractsHireExecute;
            else if (IsMutating(httpMethod)
                     && pathValue.Contains("/Step3Execute", StringComparison.OrdinalIgnoreCase))
                policy = HrManagementPolicies.HrContractsHireExecute;
            // FUN-029: Step2 actions (manual pending contract, appointment decision, medical step).
            else if (IsMutating(httpMethod)
                     && (pathValue.Contains("/CreatePendingContract", StringComparison.OrdinalIgnoreCase)
                         || pathValue.Contains("/IssueAppointmentDecision", StringComparison.OrdinalIgnoreCase)
                         || pathValue.Contains("/CompleteMedicalStep", StringComparison.OrdinalIgnoreCase)))
                policy = HrManagementPolicies.HrContractsHireExecute;
            else if (IsMutating(httpMethod)
                     && pathValue.Contains("/Step4Complete", StringComparison.OrdinalIgnoreCase))
                policy = HrManagementPolicies.HrContractsApprove;
            else
                policy = HrManagementPolicies.HrContractsHireView;
            return true;
        }

        if (string.Equals(moduleSegment, "hr-contracts", StringComparison.OrdinalIgnoreCase) && IsMutating(httpMethod))
        {
            policy = HrManagementPolicies.HrContractsApprove;
            return true;
        }

        if (string.Equals(moduleSegment, "payroll", StringComparison.OrdinalIgnoreCase) && IsMutating(httpMethod))
        {
            // Controllers own action-level process/approve/post/reverse authorization. The gateway
            // verifies module membership and must not incorrectly require the processor role for
            // an approver's POST.
            policy = HrManagementPolicies.ModulePayroll;
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

    // PLAN-51: PATCH writes too (HR.Core's EmployeesApi.Patch); it used to fall through to the module policy here.
    private static bool IsMutating(string httpMethod) =>
        string.Equals(httpMethod, HttpMethods.Post, StringComparison.OrdinalIgnoreCase)
        || string.Equals(httpMethod, HttpMethods.Put, StringComparison.OrdinalIgnoreCase)
        || string.Equals(httpMethod, HttpMethods.Patch, StringComparison.OrdinalIgnoreCase)
        || string.Equals(httpMethod, HttpMethods.Delete, StringComparison.OrdinalIgnoreCase);

    private static readonly string[] HrCoreStaticPrefixes =
        ["/ui/hr-core/css/", "/ui/hr-core/js/", "/ui/hr-core/lib/", "/ui/hr-core/fonts/", "/ui/hr-core/images/", "/ui/hr-core/_content/"];

    private static bool IsHrCoreStaticAsset(string pathValue) =>
        HrCoreStaticPrefixes.Any(p => pathValue.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        || string.Equals(pathValue, "/ui/hr-core/favicon.ico", StringComparison.OrdinalIgnoreCase)
        || (pathValue.StartsWith("/ui/hr-core/", StringComparison.OrdinalIgnoreCase)
            && pathValue.EndsWith(".styles.css", StringComparison.OrdinalIgnoreCase)
            && pathValue.IndexOf('/', "/ui/hr-core/".Length) < 0);

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

    private static bool RequiresTenantClaim(HttpContext context, PathString path, string moduleSegment)
    {
        if (context.User.IsInRole(HrManagementRoles.PlatformAdmin))
            return false;

        if (IsUpstreamHealthPath(path))
            return false;

        var pathValue = path.Value ?? "";
        if (pathValue.StartsWith("/api/platform/", StringComparison.OrdinalIgnoreCase))
            return false;

        return !string.IsNullOrEmpty(moduleSegment)
            || pathValue.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || pathValue.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasValidTenantClaim(System.Security.Claims.ClaimsPrincipal user)
    {
        var value = user.FindFirst(TenantContext.TenantClaimType)?.Value;
        return Guid.TryParse(value, out _);
    }
}

public static class GatewayPathAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayPathAuthorization(this IApplicationBuilder app) =>
        app.UseMiddleware<GatewayPathAuthorizationMiddleware>();
}
