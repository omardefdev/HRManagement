using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public enum PolicyEngineRoleKind
{
    Author,
    Approver,
    Reader
}

public sealed class PolicyEngineRoleRequirement(PolicyEngineRoleKind kind) : IAuthorizationRequirement
{
    public PolicyEngineRoleKind Kind { get; } = kind;
}

/// <summary>
/// PLAN-47 (D-POL-3): who authors and who publishes policy versions. The role lists come from PolicyEngine:Roles:Author and
/// PolicyEngine:Roles:Approver (an array, or one comma-separated value), so they change without code. A service account is
/// never an author or an approver. Holding both roles does not let anyone publish what they edited: the publish service checks
/// the version's editors.
/// </summary>
public sealed class PolicyEngineRoleHandler(IServiceProvider services) : AuthorizationHandler<PolicyEngineRoleRequirement>
{
    public const string AuthorRolesKey = "PolicyEngine:Roles:Author";
    public const string ApproverRolesKey = "PolicyEngine:Roles:Approver";

    public static readonly IReadOnlyList<string> DefaultAuthorRoles =
        [HrManagementRoles.Admin, HrManagementRoles.HrCore, HrManagementRoles.PolicyAuthor];

    public static readonly IReadOnlyList<string> DefaultApproverRoles =
        [HrManagementRoles.Admin, HrManagementRoles.HrManagementApprover, HrManagementRoles.PolicyApprover];

    /// <summary>The human roles of HrCoreReadOnly.</summary>
    private static readonly string[] HrCoreReaderRoles =
    [
        HrManagementRoles.Admin, HrManagementRoles.HrCore, HrManagementRoles.HrCoreSupervisory, HrManagementRoles.HrCoreExecutive,
        HrManagementRoles.BranchManager, HrManagementRoles.HrManagementApprover
    ];

    public static IReadOnlyList<string> AuthorRoles(IConfiguration? configuration) => Read(configuration, AuthorRolesKey, DefaultAuthorRoles);

    public static IReadOnlyList<string> ApproverRoles(IConfiguration? configuration) => Read(configuration, ApproverRolesKey, DefaultApproverRoles);

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyEngineRoleRequirement requirement)
    {
        var user = context.User;
        var configuration = services.GetService<IConfiguration>();
        var isService = ServiceCallerPrincipal.IsServiceAccount(user);

        // An author or an approver is a person the edit record and the publication can name: a GUID user id is required.
        var identified = HasUserId(user);
        var allowed = requirement.Kind switch
        {
            PolicyEngineRoleKind.Author => !isService && identified && AuthorRoles(configuration).Any(user.IsInRole),
            PolicyEngineRoleKind.Approver => !isService && identified && ApproverRoles(configuration).Any(user.IsInRole),
            _ => isService
                ? ServiceCallerPrincipal.HasScope(user, ServiceScopes.HrCoreLookup) || ServiceCallerPrincipal.HasScope(user, ServiceScopes.HrCoreWorkflow)
                : HrCoreReaderRoles.Concat(AuthorRoles(configuration)).Concat(ApproverRoles(configuration)).Any(user.IsInRole)
        };

        if (allowed)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private static bool HasUserId(System.Security.Claims.ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value, out var id)
        && id != Guid.Empty;

    private static IReadOnlyList<string> Read(IConfiguration? configuration, string key, IReadOnlyList<string> defaults)
    {
        var section = configuration?.GetSection(key);
        if (section is null)
            return defaults;

        var values = section.GetChildren().Select(c => c.Value).ToList();
        if (values.Count == 0 && !string.IsNullOrWhiteSpace(section.Value))
            values = section.Value.Split(',').Select(v => (string?)v).ToList();

        var roles = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return roles.Count == 0 ? defaults : roles;
    }
}
