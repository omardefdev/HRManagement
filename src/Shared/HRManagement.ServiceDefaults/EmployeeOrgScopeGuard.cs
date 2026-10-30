using System.Security.Claims;
using HRManagement.SharedKernel.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>BOLA guard for employee-scoped resources in satellite modules (IAM-002).</summary>
public static class EmployeeOrgScopeGuard
{
    public static async Task<IActionResult?> ForbidIfEmployeeOutOfScopeAsync(
        this ControllerBase controller,
        ClaimsPrincipal user,
        Guid employeeId,
        IQueryable<EmployeeOrgUnitSnapshot> employees,
        IQueryable<OrgUnitSnapshot> orgUnits,
        CancellationToken cancellationToken = default)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(user, orgUnits, ct: cancellationToken);
        if (controller.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;

        if (!scope.MustFilter)
            return null;

        var primaryOrgUnitId = await employees.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => e.PrimaryOrgUnitId)
            .FirstOrDefaultAsync(cancellationToken);

        return controller.ForbidIfOrgUnitOutOfScope(
            scope,
            primaryOrgUnitId,
            resourceType: "Employee",
            resourceId: employeeId.ToString("D"));
    }

    /// <summary>
    /// QA-034: ids of the employees whose primary org unit is inside a restricted <paramref name="scope"/>, for list
    /// filters (<c>Where(x =&gt; inScope.Contains(x.EmployeeId))</c>). An employee without a snapshot or without a
    /// primary org unit is outside the scope (fail-closed); a denied scope yields no ids. Call it only when
    /// <see cref="OrgAccessScope.MustFilter"/> is true: an unrestricted user is not filtered at all.
    /// </summary>
    public static IQueryable<Guid> EmployeeIdsInScope(
        this IQueryable<EmployeeOrgUnitSnapshot> employees,
        OrgAccessScope scope)
    {
        if (!scope.MustFilter)
            throw new InvalidOperationException("EmployeeIdsInScope is for restricted scopes; do not filter unrestricted users.");

        var allowed = scope.IsDenied ? Array.Empty<Guid>() : scope.AllowedOrgUnitIds.ToArray();
        return employees.AsNoTracking()
            .Where(e => e.PrimaryOrgUnitId.HasValue && allowed.Contains(e.PrimaryOrgUnitId.Value))
            .Select(e => e.EmployeeId);
    }
}
