using System.Security.Claims;
using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Services.Recruitment.Analytics;

/// <summary>
/// HCM-032 (PLAN-32, decision 7): candidate names in recruitment drill lists and exports are for <c>HrCoreWrite</c> users
/// only. The controllers ask the policy; the export-job download (no policy service there) re-checks with this mirror of
/// the policy's interactive roles (Admin, HrCore) — service accounts never download report files.
/// <c>Hcm032ExportTests</c> pins the mirror to the policy for every role.
/// </summary>
public static class RecruitmentReportAccess
{
    public static bool CanSeeCandidateNames(ClaimsPrincipal user) =>
        user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.HrCore);
}
