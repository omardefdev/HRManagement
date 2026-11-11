using System.Security.Claims;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Services.Delegation;

/// <summary>
/// PLAN-50 (§5, P-8): HR.Core's own lookup, straight from <c>ApprovalDelegations</c> with no cache, so a revocation acts at once. It reads
/// the current tenant only: the EF tenant filter, and under forced row security (production connects as <c>hr_core_app</c>) the
/// session's tenant. A delegate matches by employee, or by user for a row older than PLAN-50 that names no employee
/// (<see cref="ApprovalDelegationMatcher"/>); a role that is no longer delegable (<c>Delegation:DelegableRoles</c>) acts no more.
/// </summary>
public sealed class DbApprovalDelegationLookup(HRCoreDbContext db, IConfiguration configuration) : IApprovalDelegationLookup
{
    public Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        GetActiveForAsync(DelegationIdentity.UserId(user), DelegationIdentity.EmployeeId(user), ct);

    public async Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(Guid? userId, Guid? employeeId, CancellationToken ct = default)
    {
        if (userId is null && employeeId is null)
            return [];

        var now = DateTime.UtcNow;
        var rows = await db.ApprovalDelegations.AsNoTracking()
            .Where(d => !d.IsRevoked
                        && d.RoleScope != null
                        && d.ActiveFrom <= now
                        && d.ActiveUntil >= now
                        && ((employeeId != null && d.DelegateEmployeeId == employeeId)
                            || (d.DelegateEmployeeId == null && userId != null && d.DelegateId == userId)))
            .ToListAsync(ct);

        var delegable = DelegationOptions.DelegableRoles(configuration);
        return ApprovalDelegationMatcher.ActiveFor(rows, userId, employeeId, now)
            .Where(d => delegable.Contains(d.Role, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
