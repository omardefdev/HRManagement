using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Services;

/// <summary>IAM-002 org-scope gate for workflow inbox and approve panels.</summary>
public static class WorkflowInboxOrgScope
{
    public const string CentralOnlyMessage = HR.Core.Infrastructure.Services.Workflow.Gates.WorkflowGateOrgScope.CentralOnlyMessage;
    public const string OutOfBranchMessage = HR.Core.Infrastructure.Services.Workflow.Gates.WorkflowGateOrgScope.OutOfBranchMessage;
    public const string DeniedMessage = HR.Core.Infrastructure.Services.Workflow.Gates.WorkflowGateOrgScope.DeniedMessage;

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
