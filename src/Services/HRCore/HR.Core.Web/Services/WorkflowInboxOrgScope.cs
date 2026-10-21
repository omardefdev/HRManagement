using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Services;

/// <summary>IAM-002 org-scope gate for workflow inbox and approve panels.</summary>
public static class WorkflowInboxOrgScope
{
    public const string CentralOnlyMessage = "هذا الاعتماد للموارد البشرية المركزية فقط.";
    public const string OutOfBranchMessage = "هذا الطلب خارج نطاق فرعك.";
    public const string DeniedMessage = "لا يوجد نطاق تنظيمي مسموح.";

    public static bool Allows(OrgAccessScope scope, Guid? entityOrgUnitId)
    {
        if (scope.IsDenied)
            return false;

        if (!scope.MustFilter)
            return true;

        if (entityOrgUnitId is null)
            return false;

        return scope.CanAccess(entityOrgUnitId.Value);
    }

    public static string? GetBlockReason(OrgAccessScope scope, Guid? entityOrgUnitId)
    {
        if (scope.IsDenied)
            return DeniedMessage;

        if (!scope.MustFilter)
            return null;

        if (entityOrgUnitId is null)
            return CentralOnlyMessage;

        return scope.CanAccess(entityOrgUnitId.Value) ? null : OutOfBranchMessage;
    }
}
