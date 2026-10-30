using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;

namespace Payroll.Web.Services;

/// <summary>
/// QA-034: employee org scope for Payroll lists. Payroll keeps no employee org snapshots; an employee's org unit is
/// the <see cref="EmployeePayProfile.OrgUnitId"/> of the active pay profile (one per employee,
/// IX_EmployeePayProfiles_OneActive), as in <c>EmployeePayProfilesController</c>. An employee without an active
/// profile or without an org unit on it is outside a restricted scope (fail-closed).
/// </summary>
public static class PayrollEmployeeScope
{
    /// <summary>403 when <paramref name="employeeId"/> is outside a restricted scope; null to continue.</summary>
    public static async Task<IActionResult?> ForbidIfEmployeeOutOfScopeAsync(
        this ControllerBase controller,
        OrgAccessScope scope,
        Guid employeeId,
        IQueryable<EmployeePayProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        if (controller.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;
        if (!scope.MustFilter)
            return null;

        var orgUnitId = await profiles.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.IsActive)
            .OrderByDescending(p => p.EffectiveFrom)
            .Select(p => p.OrgUnitId)
            .FirstOrDefaultAsync(cancellationToken);

        return controller.ForbidIfOrgUnitOutOfScope(
            scope,
            orgUnitId,
            resourceType: "Employee",
            resourceId: employeeId.ToString("D"));
    }

    /// <summary>
    /// Ids of the employees whose active pay profile is in a restricted <paramref name="scope"/>. Call it only when
    /// <see cref="OrgAccessScope.MustFilter"/> is true; a denied scope yields no ids.
    /// </summary>
    public static IQueryable<Guid> EmployeeIdsInScope(this IQueryable<EmployeePayProfile> profiles, OrgAccessScope scope)
    {
        if (!scope.MustFilter)
            throw new InvalidOperationException("EmployeeIdsInScope is for restricted scopes; do not filter unrestricted users.");

        var allowed = scope.IsDenied ? Array.Empty<Guid>() : scope.AllowedOrgUnitIds.ToArray();
        return profiles.AsNoTracking()
            .Where(p => p.IsActive && p.OrgUnitId.HasValue && allowed.Contains(p.OrgUnitId.Value))
            .Select(p => p.EmployeeId);
    }
}
