using System.Security.Claims;
using HR.Core.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;

namespace HR.Core.Web.Services;

/// <summary>AC-26: role-based full vs masked national ID display.</summary>
public static class EmployeeNationalIdPresentation
{
    public static bool CanViewFullNationalId(ClaimsPrincipal user)
    {
        if (user.IsInRole(HrManagementRoles.Admin))
            return true;

        if (user.IsInRole(HrManagementRoles.BranchManager))
            return false;

        return user.IsInRole(HrManagementRoles.HrCore)
               || user.IsInRole(HrManagementRoles.HrCoreSupervisory)
               || user.IsInRole(HrManagementRoles.HrCoreExecutive);
    }

    public static string ResolveDisplayNationalId(Employee employee, ClaimsPrincipal user)
    {
        var raw = employee.NationalId ?? string.Empty;
        // Never expose a protected payload if a materialization path bypassed the interceptor.
        if (raw.StartsWith("CfDJ8", StringComparison.Ordinal))
            return "محمي";
        return CanViewFullNationalId(user) ? raw : PiiMask.NationalId(raw);
    }
}
