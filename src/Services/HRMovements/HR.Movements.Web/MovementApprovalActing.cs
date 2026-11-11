using System.Security.Claims;
using HR.Movements.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.AspNetCore.Http;

namespace HR.Movements.Web;

/// <summary>
/// PLAN-50 (§7, P-7): who approves a movement, and the rules a delegation adds. A holder of the direct role approves in his own name; a
/// delegate of <c>hr-management-approver</c> approves on behalf of his principal, for the delegation's type only, and never a request that
/// he or his principal created, nor one that concerns him or his principal; where the action checks the org scope (transfers, loans), the
/// principal's stored scope applies as well as the delegate's own, for reading as for approving. A failed lookup never stops a holder of the
/// direct role (he never asks) and always stops a delegate.
/// </summary>
public static class MovementApprovalActing
{
    public const string CreatorOrPrincipalMessage = "لا يجوز اعتماد طلب أنشأه المعتمد أو أصيله";

    /// <summary>PLAN-50 (review 3): the subject rule of the engine, for the movements: the employee concerned is never approved on behalf.</summary>
    public const string SubjectMessage = "لا يجوز للمفوَّض اعتماد إجراء يخصه أو يخص أصيله";

    public static readonly IReadOnlyCollection<string> DirectRoles = [HrManagementRoles.Admin, HrManagementRoles.HrManagementApprover];

    /// <summary>
    /// The actor. A controller built without the acting context (unit tests, manual constructions) knows no delegation: the caller acts in
    /// his own name, and only when he holds a direct role (review 4: the fallback never admits a caller without one).
    /// </summary>
    public static Task<ApprovalActing> ResolveAsync(
        IApprovalActingContext? context, ClaimsPrincipal user, string entityType, CancellationToken ct = default) =>
        context is null
            ? Task.FromResult(new ApprovalActing(
                DelegationIdentity.UserId(user), DelegationIdentity.EmployeeId(user), DirectRoles.Any(user.IsInRole), null))
            : context.GetAsync(DirectRoles, HrManagementRoles.HrManagementApprover, entityType, ct);

    /// <summary>On behalf: the request was created by the delegate or by his principal.</summary>
    public static string? CreatorOrPrincipal(ApprovalActing acting, Guid? requestedBy) =>
        acting.OnBehalfOf is { } delegation
        && requestedBy is Guid creator
        && (creator == acting.UserId || creator == delegation.PrincipalUserId)
            ? CreatorOrPrincipalMessage
            : null;

    /// <summary>
    /// On behalf: the movement concerns the delegate or his principal (by employee id). A token without <c>employee_id</c> cannot be matched
    /// on the delegate's side; the principal's stored employee id still is.
    /// </summary>
    public static string? Subject(ApprovalActing acting, Guid employeeId) =>
        acting.OnBehalfOf is { } delegation
        && employeeId != Guid.Empty
        && (employeeId == acting.EmployeeId || employeeId == delegation.PrincipalEmployeeId)
            ? SubjectMessage
            : null;

    /// <summary>
    /// PLAN-50 (review 1): the delegation through which the policy admitted this request, when the caller holds no role of his own that
    /// admits it (the role path never leaves one).
    /// </summary>
    public static ActiveDelegation? AdmittedBy(HttpContext? http) =>
        http is not null && http.Items.TryGetValue(ApprovalActingContext.ItemKey, out var item) ? item as ActiveDelegation : null;

    /// <summary>The stored scope of a principal who was branch-scoped at creation; null when he was central (no limit beyond the caller's).</summary>
    public static async Task<OrgAccessScope?> PrincipalScopeAsync(ActiveDelegation? delegation, IQueryable<OrgUnitSnapshot> units)
    {
        if (delegation?.PrincipalScopeOrgUnitId is not Guid root)
            return null;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),
            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, root.ToString("D"))
        ], "delegation-principal", ClaimTypes.Name, ClaimTypes.Role));
        return await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(principal, units);
    }

    /// <summary>Every org unit lies in the scope; a null scope sets no limit.</summary>
    public static bool Allows(OrgAccessScope? scope, params Guid[] orgUnitIds) =>
        scope is null || (!scope.IsDenied && (scope.IsUnrestricted || orgUnitIds.All(unit => scope.CanAccess(unit))));

    /// <summary>On behalf of a principal who was branch-scoped at creation: every org unit lies in his stored scope.</summary>
    public static async Task<bool> PrincipalScopeAllowsAsync(
        ApprovalActing acting, IQueryable<OrgUnitSnapshot> units, params Guid[] orgUnitIds) =>
        Allows(await PrincipalScopeAsync(acting.OnBehalfOf, units), orgUnitIds);

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
