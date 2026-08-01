using System.Security.Claims;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.ServiceDefaults;

/// <summary>يحدد إن كان يجب تقييد استعلام الموظفين بشريحة كادر إشرافي/تنفيذي حسب أدوار Keycloak.</summary>
public static class HrCadreVisibility
{
    /// <returns>قيمة الشريحة عند التقييد، أو null لعرض الجميع.</returns>
    public static EmployeeCadre? GetRestrictedCadre(ClaimsPrincipal user)
    {
        if (user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.HrCore))
            return null;

        var sup = user.IsInRole(HrManagementRoles.HrCoreSupervisory);
        var exe = user.IsInRole(HrManagementRoles.HrCoreExecutive);
        if (sup && exe)
            return null;
        if (sup)
            return EmployeeCadre.Supervisory;
        if (exe)
            return EmployeeCadre.Executive;
        return null;
    }
}
