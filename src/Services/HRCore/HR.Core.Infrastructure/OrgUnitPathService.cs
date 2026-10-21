using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure;

/// <summary>PERF-003: materialized-path maintenance, cycle checks, atomic subtree move.</summary>
public class OrgUnitPathService(HRCoreDbContext db)
{
    public const int MaxDepth = 32;

    /// <summary>Full-table repair only (startup/admin). Prefer AssignPathOnCreate / MoveSubtreeAsync on hot paths.</summary>
    public async Task RecomputeAllAsync(CancellationToken ct = default)
    {
        var units = await db.OrgUnits
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.OrganizationMaintenance)
            .ToListAsync(ct);
        var byId = units.ToDictionary(u => u.Id);

        foreach (var unit in units)
            unit.MaterializedPath = BuildPath(unit, byId);

        await db.SaveChangesAsync(ct);
    }

    public static int GetDepth(string materializedPath)
    {
        if (string.IsNullOrWhiteSpace(materializedPath)) return 0;
        return materializedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static string SegmentMarker(Guid id) => $"/{id:D}/";

    public static string BuildPath(OrgUnit unit, IReadOnlyDictionary<Guid, OrgUnit> byId)
    {
        var segments = new Stack<Guid>();
        var current = unit;
        var guard = 0;

        while (guard++ < 64)
        {
            segments.Push(current.Id);
            if (current.ParentId is null || !byId.TryGetValue(current.ParentId.Value, out var parent))
                break;
            current = parent;
        }

        return "/" + string.Join('/', segments) + "/";
    }

    /// <summary>Reject self-parent and moving under own descendant.</summary>
    public async Task<(bool Ok, string? Error)> ValidateNoCycleAsync(
        Guid unitId,
        Guid? newParentId,
        CancellationToken ct = default)
    {
        if (newParentId is null)
            return (true, null);

        if (newParentId.Value == unitId)
            return (false, "لا يمكن جعل الوحدة أباً لنفسها.");

        var parent = await db.OrgUnits.AsNoTracking()
            .Where(o => o.Id == newParentId.Value)
            .Select(o => new { o.Id, o.MaterializedPath })
            .FirstOrDefaultAsync(ct);

        if (parent is null)
            return (false, "الوحدة الأب غير موجودة.");

        var marker = SegmentMarker(unitId);
        if (!string.IsNullOrEmpty(parent.MaterializedPath)
            && parent.MaterializedPath.Contains(marker, StringComparison.OrdinalIgnoreCase))
            return (false, "لا يمكن نقل الوحدة تحت أحد فروعها (منع الدورات).");

        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> ValidateDepthAsync(
        Guid? parentId,
        CancellationToken ct = default)
    {
        if (parentId is null)
            return (true, null);

        var parentPath = await db.OrgUnits.AsNoTracking()
            .Where(o => o.Id == parentId.Value)
            .Select(o => o.MaterializedPath)
            .FirstOrDefaultAsync(ct);

        if (parentPath is null)
            return (false, "الوحدة الأب غير موجودة.");

        if (GetDepth(parentPath) >= MaxDepth)
            return (false, $"تجاوز الحد الأقصى لعمق الشجرة ({MaxDepth}).");

        return (true, null);
    }

    /// <summary>Set MaterializedPath for a newly inserted unit (Id already assigned).</summary>
    public async Task AssignPathOnCreateAsync(OrgUnit unit, CancellationToken ct = default)
    {
        if (unit.ParentId is null)
        {
            unit.MaterializedPath = $"/{unit.Id:D}/";
        }
        else
        {
            var parentPath = await db.OrgUnits.AsNoTracking()
                .Where(o => o.Id == unit.ParentId.Value)
                .Select(o => o.MaterializedPath)
                .FirstAsync(ct);

            if (string.IsNullOrEmpty(parentPath))
                parentPath = $"/{unit.ParentId.Value:D}/";

            unit.MaterializedPath = parentPath.TrimEnd('/') + $"/{unit.Id:D}/";
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Atomic subtree move: update ParentId and rewrite MaterializedPath for the node and all descendants.
    /// Returns affected units (tracked, with new paths) for event publishing.
    /// </summary>
    public async Task<(bool Ok, string? Error, IReadOnlyList<OrgUnit> Affected)> MoveSubtreeAsync(
        Guid unitId,
        Guid? newParentId,
        CancellationToken ct = default)
    {
        var (cycleOk, cycleError) = await ValidateNoCycleAsync(unitId, newParentId, ct);
        if (!cycleOk)
            return (false, cycleError, Array.Empty<OrgUnit>());

        var (depthOk, depthError) = await ValidateDepthAsync(newParentId, ct);
        if (!depthOk)
            return (false, depthError, Array.Empty<OrgUnit>());

        var useTx = db.Database.IsRelational();
        await using var tx = useTx ? await db.Database.BeginTransactionAsync(ct) : null;

        var unit = await db.OrgUnits.FirstOrDefaultAsync(o => o.Id == unitId, ct);
        if (unit is null)
            return (false, "الوحدة غير موجودة.", Array.Empty<OrgUnit>());

        var oldPath = unit.MaterializedPath;
        if (string.IsNullOrEmpty(oldPath))
            oldPath = $"/{unit.Id:D}/";

        string newPath;
        if (newParentId is null)
        {
            newPath = $"/{unit.Id:D}/";
        }
        else
        {
            var parentPath = await db.OrgUnits.AsNoTracking()
                .Where(o => o.Id == newParentId.Value)
                .Select(o => o.MaterializedPath)
                .FirstAsync(ct);
            if (string.IsNullOrEmpty(parentPath))
                parentPath = $"/{newParentId.Value:D}/";
            newPath = parentPath.TrimEnd('/') + $"/{unit.Id:D}/";
        }

        if (GetDepth(newPath) > MaxDepth)
            return (false, $"تجاوز الحد الأقصى لعمق الشجرة ({MaxDepth}).", Array.Empty<OrgUnit>());

        unit.ParentId = newParentId;

        var subtree = await db.OrgUnits
            .Where(o => o.MaterializedPath.StartsWith(oldPath) || o.Id == unitId)
            .ToListAsync(ct);

        foreach (var node in subtree)
        {
            if (node.Id == unitId)
            {
                node.MaterializedPath = newPath;
                continue;
            }

            if (node.MaterializedPath.StartsWith(oldPath, StringComparison.Ordinal))
                node.MaterializedPath = newPath + node.MaterializedPath[oldPath.Length..];
        }

        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        return (true, null, subtree);
    }
}
