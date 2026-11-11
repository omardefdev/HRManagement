using System.Security.Claims;
using HR.Movements.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Web;

/// <summary>
/// PLAN-50 (§7, P-7): who approves a movement, and the rules a delegation adds. A holder of the direct role approves in his own name; a
/// delegate of <c>hr-management-approver</c> approves on behalf of his principal, for the delegation's type only, and never a request that
/// he or his principal created; where the action checks the org scope (transfers, loans), the principal's stored scope applies as well as
/// the delegate's own. A failed lookup never stops a holder of the direct role (he never asks) and always stops a delegate.
/// </summary>
public static class MovementApprovalActing
{
    public const string CreatorOrPrincipalMessage = "لا يجوز اعتماد طلب أنشأه المعتمد أو أصيله";

    public static readonly IReadOnlyCollection<string> DirectRoles = [HrManagementRoles.Admin, HrManagementRoles.HrManagementApprover];

    /// <summary>
    /// The actor. A controller built without the acting context (unit tests, manual constructions) keeps the behaviour of before PLAN-50:
    /// the policies are the gate, and the caller acts in his own name.
    /// </summary>
    public static Task<ApprovalActing> ResolveAsync(
        IApprovalActingContext? context, ClaimsPrincipal user, string entityType, CancellationToken ct = default) =>
        context is null
            ? Task.FromResult(new ApprovalActing(DelegationIdentity.UserId(user), DelegationIdentity.EmployeeId(user), true, null))
            : context.GetAsync(DirectRoles, HrManagementRoles.HrManagementApprover, entityType, ct);

    /// <summary>On behalf: the request was created by the delegate or by his principal.</summary>
    public static string? CreatorOrPrincipal(ApprovalActing acting, Guid? requestedBy) =>
        acting.OnBehalfOf is { } delegation
        && requestedBy is Guid creator
        && (creator == acting.UserId || creator == delegation.PrincipalUserId)
            ? CreatorOrPrincipalMessage
            : null;

    /// <summary>On behalf of a principal who was branch-scoped at creation: every org unit lies in his stored scope.</summary>
    public static async Task<bool> PrincipalScopeAllowsAsync(
        ApprovalActing acting, IQueryable<OrgUnitSnapshot> units, params Guid[] orgUnitIds)
    {
        if (acting.OnBehalfOf?.PrincipalScopeOrgUnitId is not Guid root)
            return true;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),
            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, root.ToString("D"))
        ], "delegation-principal", ClaimTypes.Name, ClaimTypes.Role));
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(principal, units);
        return !scope.IsDenied && (scope.IsUnrestricted || orgUnitIds.All(unit => scope.CanAccess(unit)));
    }

    /// <summary>The approval record names the principal on behalf of whom it was given.</summary>
    public static void Record(ApprovalActing acting, TransferApproval approval)
    {
        if (acting.OnBehalfOf is not { } delegation)
            return;
        approval.OnBehalfOfUserId = delegation.PrincipalUserId;
        approval.OnBehalfOfName = Bounded(delegation.PrincipalName);
    }

    /// <summary>The checklist's approval step names the principal on behalf of whom it was completed.</summary>
    public static void Record(ApprovalActing acting, MovementChecklist? approvedStep)
    {
        if (approvedStep is null || acting.OnBehalfOf is not { } delegation)
            return;
        approvedStep.OnBehalfOfUserId = delegation.PrincipalUserId;
        approvedStep.OnBehalfOfName = Bounded(delegation.PrincipalName);
    }

    private static string? Bounded(string? name) => name is { Length: > 200 } ? name[..200] : name;
}
