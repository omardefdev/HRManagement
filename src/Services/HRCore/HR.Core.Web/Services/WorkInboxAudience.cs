using System.Security.Claims;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;
using System.Text.Json;

namespace HR.Core.Web.Services;

/// <summary>
/// PLAN-45: the roles from outside HR.Core that reach the work inbox, read from <see cref="ExternalApprovalTypes"/>: here
/// <c>admin-logistics</c>, and PLAN-46 adds its own. Such a user sees the inbox and the approval page and nothing else of HR.Core, so the
/// inbox reads only their types and the menu shows only the inbox.
/// </summary>
public static class WorkInboxAudience
{
    private static readonly string[] HrCoreReadRoles =
    [
        HrManagementRoles.Admin,
        HrManagementRoles.HrCore,
        HrManagementRoles.HrCoreSupervisory,
        HrManagementRoles.HrCoreExecutive,
        HrManagementRoles.BranchManager,
        HrManagementRoles.HrManagementApprover
    ];

    public static IReadOnlyList<string> Roles { get; } =
        ExternalApprovalTypes.All.SelectMany(t => t.AudienceRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool HasHrCoreReadRole(ClaimsPrincipal user) => HrCoreReadRoles.Any(user.IsInRole);

    /// <summary>A role of the audience and no HR.Core read role.</summary>
    public static bool IsAudienceOnly(ClaimsPrincipal user) => !HasHrCoreReadRole(user) && Roles.Any(user.IsInRole);

    /// <summary>The external types this user's roles belong to.</summary>
    public static IReadOnlyList<string> EntityTypesFor(ClaimsPrincipal user) =>
        ExternalApprovalTypes.All.Where(t => t.AudienceRoles.Any(user.IsInRole)).Select(t => t.EntityType).ToList();

    /// <summary>
    /// Who may read an approval by its id: a holder of a step role of the pinned definition, the requester, or <c>admin</c>. Holding
    /// <c>HrCoreWorkInbox</c> alone opens nothing.
    /// </summary>
    public static bool CanRead(ClaimsPrincipal user, Guid? userId, WorkflowInstance instance, WorkflowDefinition definition)
    {
        if (user.IsInRole(HrManagementRoles.Admin))
            return true;
        if (userId is not null && userId == instance.RequestedByUserId)
            return true;

        var steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(definition.StepsJson) ?? [];
        return steps.Any(s => !string.IsNullOrWhiteSpace(s.RoleRequired) && user.IsInRole(s.RoleRequired));
    }
}
