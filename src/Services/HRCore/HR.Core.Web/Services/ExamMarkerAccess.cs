using System.Security.Claims;
using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Services;

/// <summary>HCM-031 (PLAN-31): how the exam marker sees HR.Core — the marking screens only.</summary>
public static class ExamMarkerAccess
{
    public static bool IsMarker(ClaimsPrincipal user) => user.IsInRole(HrManagementRoles.HrExamMarker);

    /// <summary>The marker role without any HR.Core read role (or the compliance reviewer role): the marking menu only.</summary>
    public static bool IsExamMarkerOnly(ClaimsPrincipal user) =>
        IsMarker(user)
        && !user.IsInRole(HrManagementRoles.Admin)
        && !user.IsInRole(HrManagementRoles.HrCore)
        && !user.IsInRole(HrManagementRoles.HrCoreSupervisory)
        && !user.IsInRole(HrManagementRoles.HrCoreExecutive)
        && !user.IsInRole(HrManagementRoles.BranchManager)
        && !user.IsInRole(HrManagementRoles.HrManagementApprover)
        && !user.IsInRole(HrManagementRoles.Compliance);
}
