using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentSelectionService(HRCoreDbContext db)
{
    public async Task<(bool Success, string? Error)> DeclareCoiAsync(
        RecruitmentApplication application,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var app = await LoadApplicationAsync(application.Id, ct);
        if (app is null) return (false, "الطلب غير موجود.");

        app.ConflictOfInterestDeclared = true;
        app.ConflictOfInterestDeclaredAt = DateTime.UtcNow;

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentApplication),
            EntityId = app.Id,
            Action = "DeclareCoi",
            UserId = actorUserId,
            ChangedFields = """{"conflictOfInterestDeclared":true}"""
        });

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Legacy — not exposed; PLAN-26. Free-form single score kept only for data migration tooling.
    /// Use <see cref="RecruitmentScoringService.SubmitScoresAsync"/> for structured multi-assessor scoring.
    /// </summary>
    internal async Task<(bool Success, string? Error)> RecordAssessmentAsync(
        Guid applicationId,
        Guid criteriaVersionId,
        decimal score,
        CancellationToken ct = default)
    {
        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");

        var criteria = await db.RecruitmentAssessmentCriteria
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == criteriaVersionId && c.RequisitionId == app.RequisitionId, ct);
        if (criteria is null)
            return (false, "إصدار المعايير غير صالح لهذا الطلب.");

        app.AssessmentScore = score;
        app.CriteriaVersionId = criteriaVersionId;

        if (app.Status == RecruitmentApplicationStatus.InterviewScheduled)
        {
            var gate = SelectionWorkflowRules.ValidateTransitionGate(app, RecruitmentApplicationStatus.Assessed);
            if (gate is null)
            {
                app.Status = RecruitmentApplicationStatus.Assessed;
                app.ReviewedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AssignRankAsync(
        Guid applicationId,
        int rank,
        CancellationToken ct = default)
    {
        if (rank < 1)
            return (false, "الترتيب يجب أن يكون موجباً.");

        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");
        if (!RecruitmentAppealWorkflowRules.CanRank(app))
            return (false, "لا يمكن تعيين الترتيب أثناء استئناف معلق.");
        if (app.RequisitionId is null)
            return (false, "الترتيب يتطلب طلب شاغر.");

        var duplicate = await db.RecruitmentApplications.AnyAsync(
            a => a.RequisitionId == app.RequisitionId && a.Id != app.Id && a.Rank == rank, ct);
        if (duplicate)
            return (false, "الترتيب مستخدم لمرشح آخر على نفس طلب الشاغر.");

        app.Rank = rank;
        app.RankedAt = DateTime.UtcNow;

        if (app.Status == RecruitmentApplicationStatus.Assessed)
        {
            var gate = SelectionWorkflowRules.ValidateTransitionGate(app, RecruitmentApplicationStatus.Ranked);
            if (gate is null)
            {
                app.Status = RecruitmentApplicationStatus.Ranked;
                app.ReviewedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<IReadOnlyList<RecruitmentApplication>> GetRankedCandidatesAsync(
        Guid requisitionId,
        CancellationToken ct = default) =>
        await db.RecruitmentApplications
            .AsNoTracking()
            .Where(a => a.RequisitionId == requisitionId && a.Rank != null)
            .OrderBy(a => a.Rank)
            .ThenBy(a => a.ApplicantFullName)
            .ToListAsync(ct);

    public async Task<(bool Success, string? Error)> TransitionStatusAsync(
        Guid applicationId,
        RecruitmentApplicationStatus to,
        bool legacyHireWithoutOffer,
        CancellationToken ct = default)
    {
        if (SelectionWorkflowRules.SystemOnlyTargets.Contains(to))
            return (false, SelectionWorkflowRules.AssessedIsAutomaticMessage);

        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");

        if (!SelectionWorkflowRules.CanTransition(app, to, legacyHireWithoutOffer))
        {
            var gate = SelectionWorkflowRules.ValidateTransitionGate(app, to)
                ?? "لا يمكن تنفيذ الانتقال المطلوب للحالة الحالية.";
            return (false, gate);
        }

        app.Status = to;
        app.ReviewedAt = DateTime.UtcNow;
        if (to == RecruitmentApplicationStatus.UnderReview)
            app.ScreenedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ReopenAppealAsync(
        Guid applicationId,
        string appealNotes,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appealNotes))
            return (false, "ملاحظات الاستئناف مطلوبة.");

        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");

        if (app.Status != RecruitmentApplicationStatus.Rejected)
            return (false, "يمكن إعادة فتح الاستئناف للطلبات المرفوضة فقط.");

        app.AppealNotes = appealNotes.Trim();
        app.Status = RecruitmentApplicationStatus.UnderReview;
        app.ReviewedAt = DateTime.UtcNow;
        app.ScreenedAt = null;

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentApplication),
            EntityId = app.Id,
            Action = "ReopenAppeal",
            UserId = actorUserId,
            ChangedFields = $$"""{"appealNotes":{{JsonSerializer.Serialize(app.AppealNotes)}}}"""
        });

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private Task<RecruitmentApplication?> LoadApplicationAsync(Guid id, CancellationToken ct) =>
        db.RecruitmentApplications
            .Include(a => a.ActiveOfferLetter)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
}
