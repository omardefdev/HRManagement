using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-026 (G-03): versioned, immutable assessment criteria per requisition.
/// One active version per requisition; a version is locked by the first submitted score.
/// </summary>
public sealed class RecruitmentAssessmentCriteriaService(HRCoreDbContext db)
{
    public const int MinChangeReasonLength = 10;

    private static readonly RequisitionStatus[] EditableRequisitionStatuses =
    [
        RequisitionStatus.Draft,
        RequisitionStatus.PendingApproval,
        RequisitionStatus.Approved,
        RequisitionStatus.Published
    ];

    public async Task<(RecruitmentAssessmentCriteria? Criteria, string? Error)> CreateVersionAsync(
        Guid requisitionId,
        string? title,
        IReadOnlyList<AssessmentCriteriaItem> items,
        string? changeReason,
        Guid? actorUserId,
        bool actorCanApprove,
        CancellationToken ct = default)
    {
        var normalized = (items ?? [])
            .Select(i => i with
            {
                Key = (i.Key ?? string.Empty).Trim(),
                NameAr = (i.NameAr ?? string.Empty).Trim(),
                Description = string.IsNullOrWhiteSpace(i.Description) ? null : i.Description.Trim()
            })
            .ToList();

        var validation = AssessmentCriteriaRules.Validate(normalized);
        if (validation is not null)
            return (null, validation);

        var requisition = await db.RecruitmentRequisitions.FirstOrDefaultAsync(r => r.Id == requisitionId, ct);
        if (requisition is null)
            return (null, "طلب الشاغر غير موجود.");

        if (!EditableRequisitionStatuses.Contains(requisition.Status))
            return (null, "لا يمكن تعديل معايير التقييم لطلب شاغر مغلق أو ملغى.");

        var trimmedReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim();
        if (trimmedReason is { Length: > 500 })
            return (null, "سبب التغيير يجب ألا يتجاوز 500 حرف.");

        var trimmedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (trimmedTitle is { Length: > 200 })
            return (null, "عنوان الإصدار يجب ألا يتجاوز 200 حرف.");

        var previous = await db.RecruitmentAssessmentCriteria
            .FirstOrDefaultAsync(c => c.RequisitionId == requisitionId && c.IsActive, ct);

        var replacesLocked = previous?.LockedAt is not null;
        if (replacesLocked)
        {
            if (trimmedReason is null || trimmedReason.Length < MinChangeReasonLength)
                return (null, $"تغيير معايير بدأ التقييم عليها يتطلب سبباً مكتوباً ({MinChangeReasonLength} أحرف على الأقل).");
            if (!actorCanApprove)
                return (null, "تغيير معايير بدأ التقييم عليها يتطلب صلاحية الاعتماد.");
        }

        var maxVersion = await db.RecruitmentAssessmentCriteria
            .Where(c => c.RequisitionId == requisitionId)
            .MaxAsync(c => (int?)c.VersionNumber, ct) ?? 0;
        var versionNumber = maxVersion + 1;

        var criteria = new RecruitmentAssessmentCriteria
        {
            TenantId = requisition.TenantId,
            RequisitionId = requisitionId,
            VersionNumber = versionNumber,
            Title = trimmedTitle ?? $"معايير التقييم v{versionNumber}",
            CriteriaJson = AssessmentCriteriaSerializer.Serialize(normalized),
            EffectiveFrom = DateTime.UtcNow,
            IsActive = true,
            ChangeReason = trimmedReason,
            ApprovedBy = actorCanApprove ? actorUserId : null
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Deactivate first in its own statement so the filtered unique index never sees two active rows.
        if (previous is not null)
        {
            previous.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        db.RecruitmentAssessmentCriteria.Add(criteria);
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentAssessmentCriteria),
            EntityId = criteria.Id,
            Action = replacesLocked ? "ReplaceLockedCriteriaVersion" : "CreateCriteriaVersion",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new
            {
                requisitionId,
                versionNumber,
                previousVersionId = previous?.Id,
                previousLocked = replacesLocked,
                changeReason = trimmedReason
            })
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (criteria, null);
    }

    public Task<RecruitmentAssessmentCriteria?> GetActiveAsync(Guid requisitionId, CancellationToken ct = default) =>
        db.RecruitmentAssessmentCriteria
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RequisitionId == requisitionId && c.IsActive, ct);

    public async Task<IReadOnlyList<RecruitmentAssessmentCriteria>> GetVersionsAsync(
        Guid requisitionId,
        CancellationToken ct = default) =>
        await db.RecruitmentAssessmentCriteria
            .AsNoTracking()
            .Where(c => c.RequisitionId == requisitionId)
            .OrderByDescending(c => c.VersionNumber)
            .ToListAsync(ct);

    public Task<bool> HasActiveAsync(Guid requisitionId, CancellationToken ct = default) =>
        db.RecruitmentAssessmentCriteria.AnyAsync(c => c.RequisitionId == requisitionId && c.IsActive, ct);

    public async Task<(bool Success, string? Error)> LockAsync(Guid criteriaId, CancellationToken ct = default)
    {
        var criteria = await db.RecruitmentAssessmentCriteria.FirstOrDefaultAsync(c => c.Id == criteriaId, ct);
        if (criteria is null)
            return (false, "إصدار المعايير غير موجود.");

        if (criteria.LockedAt is null)
        {
            criteria.LockedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return (true, null);
    }
}
