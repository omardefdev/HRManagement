using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace HR.Movements.Web;

/// <summary>
/// PLAN-50 (D-IAM-1, P-3): what the five movement screens show to whom. A hidden button is not the control: the 403 of the action's
/// policy is. The buttons follow the same policies, so a user sees only what the server would accept.
/// </summary>
public static class MovementApprovalUi
{
    public const string ShowAll = "all";
    public const string ShowPending = "pending";
    public const string PendingOnlyKey = "MovementListPendingOnly";

    /// <summary>
    /// The approver's list (a user without the module role) shows what awaits approval by default; <c>?show=all</c> lists every request
    /// in scope, <c>?show=pending</c> narrows any user's list.
    /// </summary>
    public static bool PendingOnly(ClaimsPrincipal user, string? show, ViewDataDictionary viewData)
    {
        var pendingOnly = string.Equals(show, ShowPending, StringComparison.OrdinalIgnoreCase)
                          || (!string.Equals(show, ShowAll, StringComparison.OrdinalIgnoreCase)
                              && !user.IsInRole(HrManagementRoles.HrMovements)
                              && !user.IsInRole(HrManagementRoles.Admin));
        viewData[PendingOnlyKey] = pendingOnly;
        return pendingOnly;
    }

    /// <summary>Create, edit, execute, activate, close and cancel (<see cref="HrManagementPolicies.HrMovementsWrite"/>).</summary>
    public static async Task<bool> CanWriteAsync(IAuthorizationService authorization, ClaimsPrincipal user) =>
        (await authorization.AuthorizeAsync(user, HrManagementPolicies.HrMovementsWrite)).Succeeded;

    /// <summary>Approve and reject (<see cref="HrManagementPolicies.HrManagementApprove"/>) for this movement type.</summary>
    public static async Task<bool> CanApproveAsync(IAuthorizationService authorization, ClaimsPrincipal user, string entityType)
    {
        _ = entityType;
        return (await authorization.AuthorizeAsync(user, HrManagementPolicies.HrManagementApprove)).Succeeded;
    }
}
