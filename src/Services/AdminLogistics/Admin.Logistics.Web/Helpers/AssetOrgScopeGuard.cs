using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Web.Helpers;

/// <summary>IAM-002 / AC-32: org-scope guard for asset custody anchored on open holder.</summary>
public static class AssetOrgScopeGuard
{
    public static async Task<Guid?> ResolveAnchorEmployeeIdAsync(
        AdminLogisticsDbContext db,
        Guid assetId,
        CancellationToken ct = default) =>
        await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == assetId
                        && a.ReturnedAt == null
                        && (a.Status == AssetAssignmentStatus.Active
                            || a.Status == AssetAssignmentStatus.PendingApproval))
            .Select(a => (Guid?)a.EmployeeId)
            .FirstOrDefaultAsync(ct);

    public static async Task<IActionResult?> ForbidIfAssetOutOfScopeAsync(
        this ControllerBase controller,
        AdminLogisticsDbContext db,
        Guid assetId,
        CancellationToken ct = default)
    {
        var anchor = await ResolveAnchorEmployeeIdAsync(db, assetId, ct);
        if (anchor is Guid employeeId)
        {
            return await controller.ForbidIfEmployeeOutOfScopeAsync(
                controller.User,
                employeeId,
                db.EmployeeOrgUnitSnapshots,
                db.OrgUnitSnapshots,
                ct);
        }

        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(
            controller.User,
            db.OrgUnitSnapshots,
            ct: ct);
        if (controller.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;

        if (!scope.MustFilter)
            return null;

        OrgScopeDenialContext.Record(
            controller.HttpContext,
            controller.User,
            reason: "org-scope-denied",
            resourceType: "Asset",
            resourceId: assetId.ToString("D"));
        return controller.Forbid();
    }

    public static Task<IActionResult?> ForbidIfEmployeeOutOfScopeForCustodyAsync(
        this ControllerBase controller,
        AdminLogisticsDbContext db,
        Guid employeeId,
        CancellationToken ct = default) =>
        controller.ForbidIfEmployeeOutOfScopeAsync(
            controller.User,
            employeeId,
            db.EmployeeOrgUnitSnapshots,
            db.OrgUnitSnapshots,
            ct);
}
