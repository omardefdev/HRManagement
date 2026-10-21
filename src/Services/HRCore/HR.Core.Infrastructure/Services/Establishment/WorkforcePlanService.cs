using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public interface IWorkforcePlanService
{
    Task<WorkforcePlan?> GetWithLinesAsync(Guid id, CancellationToken ct = default);

    Task<WorkforcePlanLine> AddLineAsync(
        Guid planId,
        string? gradeCode,
        Guid? jobCatalogEntryId,
        int plannedCount,
        decimal plannedFte,
        string? notes = null,
        CancellationToken ct = default);

    Task RemoveLineAsync(Guid lineId, CancellationToken ct = default);

    Task<(bool Success, string? Error)> ApproveAsync(
        Guid planId,
        string? legalSourceId,
        Guid? approvedBy,
        CancellationToken ct = default);
}

public sealed class WorkforcePlanService(HRCoreDbContext db) : IWorkforcePlanService
{
    public Task<WorkforcePlan?> GetWithLinesAsync(Guid id, CancellationToken ct = default) =>
        db.WorkforcePlans
            .Include(p => p.Lines)
            .Include(p => p.OrgUnit)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<WorkforcePlanLine> AddLineAsync(
        Guid planId,
        string? gradeCode,
        Guid? jobCatalogEntryId,
        int plannedCount,
        decimal plannedFte,
        string? notes = null,
        CancellationToken ct = default)
    {
        var plan = await db.WorkforcePlans.FirstOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new InvalidOperationException("Workforce plan not found.");
        if (plan.Status != "draft")
            throw new InvalidOperationException("Cannot edit a non-draft plan.");

        var line = new WorkforcePlanLine
        {
            WorkforcePlanId = planId,
            GradeCode = gradeCode,
            JobCatalogEntryId = jobCatalogEntryId,
            PlannedCount = plannedCount,
            PlannedFTE = plannedFte,
            Notes = notes
        };
        db.WorkforcePlanLines.Add(line);
        await db.SaveChangesAsync(ct);
        return line;
    }

    public async Task RemoveLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await db.WorkforcePlanLines.Include(l => l.WorkforcePlan)
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null) return;
        if (line.WorkforcePlan?.Status != "draft")
            throw new InvalidOperationException("Cannot edit a non-draft plan.");

        db.WorkforcePlanLines.Remove(line);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(bool Success, string? Error)> ApproveAsync(
        Guid planId,
        string? legalSourceId,
        Guid? approvedBy,
        CancellationToken ct = default)
    {
        var plan = await db.WorkforcePlans.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null)
            return (false, "الخطة غير موجودة.");
        if (plan.Status != "draft")
            return (false, "الخطة ليست مسودة.");
        if (plan.OrgUnitId is null)
            return (false, "يجب تحديد الوحدة التنظيمية.");
        if (plan.Lines.Count == 0)
            return (false, "يجب إضافة سطر هدف واحد على الأقل.");

        plan.Status = "approved";
        plan.ApprovedAt = DateTime.UtcNow;
        plan.ApprovedBy = approvedBy;
        plan.LegalSourceId = legalSourceId;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }
}
