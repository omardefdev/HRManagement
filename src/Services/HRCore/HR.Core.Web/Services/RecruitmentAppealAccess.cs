using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Services;

/// <summary>
/// HCM-028 (F, PLAN-25 slice): how the compliance reviewer sees HR.Core — only appeals assigned to their employee
/// (<c>employee_id</c> claim), in the appeals pages and the work inbox.
/// </summary>
public static class RecruitmentAppealAccess
{
    /// <summary>The compliance role (or Admin) may decide an appeal assigned to the actor.</summary>
    public static bool HasReviewerRole(ClaimsPrincipal user) =>
        user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.Compliance);

    /// <summary>Compliance without any HR.Core read role: limited to appeals assigned to them.</summary>
    public static bool IsComplianceOnly(ClaimsPrincipal user) =>
        user.IsInRole(HrManagementRoles.Compliance)
        && !user.IsInRole(HrManagementRoles.Admin)
        && !user.IsInRole(HrManagementRoles.HrCore)
        && !user.IsInRole(HrManagementRoles.HrCoreSupervisory)
        && !user.IsInRole(HrManagementRoles.HrCoreExecutive)
        && !user.IsInRole(HrManagementRoles.BranchManager)
        && !user.IsInRole(HrManagementRoles.HrManagementApprover);

    /// <summary>The appeal is assigned to the user — by employee, or for legacy rows by Keycloak user id.</summary>
    public static bool IsAssignedTo(RecruitmentAppeal appeal, ClaimsPrincipal user)
    {
        if (appeal.IndependentReviewerEmployeeId is Guid reviewerEmployeeId)
            return HrOrgScopeVisibility.GetEmployeeId(user) == reviewerEmployeeId;
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        return userId.HasValue && appeal.IndependentReviewerId == userId;
    }

    /// <summary>Same rule as <see cref="HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealService.DecideAsync"/>.</summary>
    public static bool CanDecide(RecruitmentAppeal appeal, ClaimsPrincipal user) =>
        RecruitmentAppealWorkflowRules.IsAssignedReviewer(
            appeal, HasReviewerRole(user), WorkflowPanelBuilder.ParseUserId(user), HrOrgScopeVisibility.GetEmployeeId(user));
}
