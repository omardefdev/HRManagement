using System.Security.Claims;
using HRManagement.SharedKernel.SeparationOfDuties;

namespace HRManagement.ServiceDefaults.Delegation;

/// <summary>
/// PLAN-50 (§5): the delegations in force whose delegate is this user (by the <c>employee_id</c> claim, or by <c>sub</c> for rows older
/// than PLAN-50), in the user's tenant, with a delegable role. HR.Core reads its database (<c>DbApprovalDelegationLookup</c>); other
/// services ask HR.Core (<see cref="HttpApprovalDelegationLookup"/>). A service without one registered decides by roles only.
/// </summary>
public interface IApprovalDelegationLookup
{
    Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(ClaimsPrincipal user, CancellationToken ct = default);
}

/// <summary>
/// PLAN-50 (§7): the entity type an approval action decides, so a delegation scoped to another type does not act there. On a controller it
/// covers every action of the controller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApprovalEntityTypeAttribute(string entityType) : Attribute
{
    public string EntityType { get; } = entityType;
}

/// <summary>PLAN-50: the resource a view passes to a role-or-delegation policy, to ask about one entity type.</summary>
public sealed record ApprovalEntityTypeResource(string EntityType);

/// <summary>The identity in a token, as the lookups and the matcher use it.</summary>
public static class DelegationIdentity
{
    public static Guid? UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id) && id != Guid.Empty
            ? id
            : null;

    public static Guid? EmployeeId(ClaimsPrincipal user) =>
        HrOrgScopeVisibility.GetEmployeeId(user) is Guid id && id != Guid.Empty ? id : null;
}
