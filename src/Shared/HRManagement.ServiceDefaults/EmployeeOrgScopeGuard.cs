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
}
