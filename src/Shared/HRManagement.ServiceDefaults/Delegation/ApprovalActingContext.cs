using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Delegation;

/// <summary>
/// PLAN-50 (§5): who acts. <see cref="OnBehalfOf"/> is null when the user holds a direct role (the direct role wins); otherwise it is the
/// delegation through which he acts, or null when there is none (the caller refuses).
/// </summary>
public sealed record ApprovalActing(Guid? UserId, Guid? EmployeeId, bool HoldsDirectRole, ActiveDelegation? OnBehalfOf)
{
    /// <summary>He may act: by a direct role, or through a delegation in force.</summary>
    public bool MayAct => HoldsDirectRole || OnBehalfOf is not null;
}

public interface IApprovalActingContext
{
    /// <summary>
    /// The actor of the current request for an approval of <paramref name="entityType"/>: a direct holder of one of
    /// <paramref name="directRoles"/> acts in his own name; anyone else acts through a delegation of <paramref name="delegatedRole"/> that
    /// covers the type, or not at all. A lookup that fails gives no delegation.
    /// </summary>
    Task<ApprovalActing> GetAsync(
        IReadOnlyCollection<string> directRoles,
        string delegatedRole,
        string entityType,
        CancellationToken ct = default);
}

public sealed class ApprovalActingContext(
    IHttpContextAccessor accessor,
    ILogger<ApprovalActingContext> logger,
    IApprovalDelegationLookup? lookup = null) : IApprovalActingContext
{
    /// <summary>The <c>HttpContext.Items</c> key under which <see cref="RoleOrDelegationHandler"/> leaves the delegation it matched.</summary>
    public const string ItemKey = "PLAN50.ApprovalDelegation";

    public async Task<ApprovalActing> GetAsync(
        IReadOnlyCollection<string> directRoles,
        string delegatedRole,
        string entityType,
        CancellationToken ct = default)
    {
        var http = accessor.HttpContext;
        var user = http?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return new ApprovalActing(null, null, false, null);

        var userId = DelegationIdentity.UserId(user);
        var employeeId = DelegationIdentity.EmployeeId(user);
        if (directRoles.Any(user.IsInRole))
            return new ApprovalActing(userId, employeeId, true, null);

        if (lookup is null || ServiceCallerPrincipal.IsServiceAccount(user))
            return new ApprovalActing(userId, employeeId, false, null);

        // The policy already matched one for this request; the decision re-checks it against the type and the clock.
        var now = DateTime.UtcNow;
        if (http!.Items.TryGetValue(ItemKey, out var item) && item is ActiveDelegation matched
            && ApprovalDelegationMatcher.Match([matched], delegatedRole, entityType, now) is not null)
            return new ApprovalActing(userId, employeeId, false, matched);

        try
        {
            var active = await lookup.GetActiveForAsync(user, ct);
            return new ApprovalActing(userId, employeeId, false, ApprovalDelegationMatcher.Match(active, delegatedRole, entityType, now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "approval delegation lookup failed; the actor acts through no delegation");
            return new ApprovalActing(userId, employeeId, false, null);
        }
    }
}
