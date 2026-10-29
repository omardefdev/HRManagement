using System.Globalization;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: one objection on the HR objections tab.</summary>
public sealed record CompetitionObjectionRow(
    Guid AppealId,
    Guid ApplicationId,
    string ApplicantFullName,
    string? ExamNumber,
    RecruitmentAppealStatus Status,
    RecruitmentAppealOutcome? Outcome,
    decimal? CorrectedScore,
    string? FiledBy,
    DateTime FiledAt,
    bool HasReviewer);

/// <summary>
/// HCM-031 (PLAN-31, P4, AC-HCM-31e, decision 5): result objections reuse <see cref="RecruitmentAppeal"/> (kind
/// <c>ResultObjection</c>) so the PLAN-28 independent reviewer and <c>RECRUITMENT-APPEAL-REVIEW</c> apply. Filed by the
/// candidate (portal) or HR on behalf while <c>now ≤ ResultsFirstPublishedAt + ObjectionWindowDays</c> — a later version
/// never reopens the window. One pending objection per application. Upheld: the reviewer's corrected mark replaces the
/// paper's mark and the results are recomputed into a new draft version (with a diff log), which goes through the
/// results approval again; dismissed: no change.
/// </summary>
public sealed class CompetitionObjectionService(HRCoreDbContext db, RecruitmentAppealService appeals, CompetitionResultsService results)
{
    public const string PendingExistsMessage = "يوجد اعتراض قيد المراجعة على هذه النتيجة.";
    public const string NoPublishedResultMessage = "لم تُنشر نتيجة لهذا الطلب بعد.";

    public static string WindowClosedMessage(DateTime deadlineUtc) =>
        $"انتهت مدة الاعتراض في {deadlineUtc.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}.";

    /// <summary>Null when the application can be objected now, else the reason (Arabic).</summary>
    public async Task<string?> GetFilingErrorAsync(RecruitmentApplication app, DateTime utcNow, CancellationToken ct = default)
    {
        if (app.CompetitionId is not Guid competitionId)
            return "الاعتراض على النتائج متاح لطلبات المسابقات فقط.";
        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstAsync(c => c.Id == competitionId, ct);
        if (competition.Status is RecruitmentCompetitionStatus.Finalized or RecruitmentCompetitionStatus.Cancelled
            || competition.CurrentResultVersionId is not Guid versionId)
            return NoPublishedResultMessage;
        var deadline = CompetitionWorkflowRules.ObjectionDeadlineUtc(competition.ResultsFirstPublishedAt, competition.ObjectionWindowDays)!.Value;
        if (utcNow > deadline)
            return WindowClosedMessage(deadline);
        if (!await db.CompetitionResults.AnyAsync(r => r.ResultVersionId == versionId && r.RecruitmentApplicationId == app.Id, ct))
            return NoPublishedResultMessage;
        if (await db.RecruitmentAppeals.AnyAsync(a => a.RecruitmentApplicationId == app.Id && a.Status == RecruitmentAppealStatus.Pending, ct))
            return PendingExistsMessage;
        return null;
    }

    public async Task<(RecruitmentAppeal? Appeal, string? Error)> FileAsync(
        Guid applicationId,
        Guid? examCandidateId,
        string? reason,
        string filedBy,
        Guid? actorUserId,
        Guid? actorEmployeeId = null,
        CancellationToken ct = default)
    {
        if (!RecruitmentAppealFiledBy.IsValid(filedBy))
            return (null, "جهة تقديم الاعتراض غير صالحة.");
        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < RecruitmentAppealWorkflowRules.MinReasonLength)
            return (null, $"سبب الاعتراض مطلوب ({RecruitmentAppealWorkflowRules.MinReasonLength} حرفاً على الأقل).");
        if (trimmed.Length > 2000)
            return (null, "سبب الاعتراض يجب ألا يتجاوز 2000 حرف.");

        var app = await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null)
            return (null, "الطلب غير موجود.");
        var error = await GetFilingErrorAsync(app, DateTime.UtcNow, ct);
        if (error is not null)
            return (null, error);

        var papers = await db.ExamCandidates.Where(c => c.RecruitmentApplicationId == app.Id).Select(c => c.Id).ToListAsync(ct);
        var paperId = examCandidateId ?? (papers.Count == 1 ? papers[0] : (Guid?)null);
        if (paperId is not Guid paper || !papers.Contains(paper))
            return (null, "اختر دفتر الامتحان موضوع الاعتراض.");

        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstAsync(c => c.Id == app.CompetitionId, ct);
        var appeal = new RecruitmentAppeal
        {
            TenantId = app.TenantId,
            RecruitmentApplicationId = app.Id,
            Kind = RecruitmentAppealKind.ResultObjection,
            CompetitionResultVersionId = competition.CurrentResultVersionId,
            ObjectionExamCandidateId = paper,
            Reason = trimmed,
            Status = RecruitmentAppealStatus.Pending,
            FiledBy = filedBy,
            FiledByEmployeeId = actorEmployeeId,
            CreatedBy = actorUserId
        };
        db.RecruitmentAppeals.Add(appeal);
        app.AppealStatus = RecruitmentAppealStatus.Pending;
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = app.TenantId,
            EntityType = nameof(RecruitmentAppeal),
            EntityId = appeal.Id,
            Action = "ResultObjectionFiled",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new { applicationId = app.Id, filedBy, versionId = appeal.CompetitionResultVersionId })
        });
        await db.SaveChangesAsync(ct);
        return (appeal, null);
    }

    /// <summary>
    /// The independent reviewer decides (outcome <c>ObjectionUpheld</c> with <paramref name="correctedScore"/>, or
    /// <c>ObjectionDismissed</c>) through <see cref="RecruitmentAppealService.DecideAsync"/>. After an upheld decision the
    /// results are recomputed into a new draft version (reason «اعتراض مقبول»). Returns a notice when the recomputation
    /// could not run (HR can recompute from the results tab).
    /// </summary>
    public async Task<(bool Success, string? Error, string? Notice)> DecideAsync(
        Guid appealId,
        RecruitmentAppealOutcome outcome,
        string? rationale,
        decimal? correctedScore,
        AppealReviewActor actor,
        CancellationToken ct = default)
    {
        var appeal = await db.RecruitmentAppeals.FirstOrDefaultAsync(a => a.Id == appealId, ct);
        if (appeal is null || appeal.Kind != RecruitmentAppealKind.ResultObjection)
            return (false, RecruitmentAppealService.NotFoundMessage, null);
        var outcomeError = RecruitmentAppealWorkflowRules.ValidateObjectionOutcome(outcome, correctedScore);
        if (outcome == RecruitmentAppealOutcome.ObjectionUpheld && outcomeError is null)
        {
            var max = await db.ExamCandidates.Where(c => c.Id == appeal.ObjectionExamCandidateId)
                .Select(c => (decimal?)c.ExamSession!.MaxScore).FirstOrDefaultAsync(ct);
            outcomeError = max is null ? "دفتر الاعتراض غير موجود." : ExamMarkingRules.ValidateScore(correctedScore!.Value, max.Value);
        }
        if (outcomeError is not null)
            return (false, outcomeError, null);

        appeal.ObjectionCorrectedScore = outcome == RecruitmentAppealOutcome.ObjectionUpheld ? correctedScore : null;
        var (ok, error) = await appeals.DecideAsync(appealId, outcome, rationale, correctedRank: null, actor, ct);
        if (!ok)
            return (false, error, null);

        var decided = await db.RecruitmentAppeals.AsNoTracking().FirstAsync(a => a.Id == appealId, ct);
        if (decided.Status != RecruitmentAppealStatus.Decided || decided.Outcome != RecruitmentAppealOutcome.ObjectionUpheld)
            return (true, null, null);

        var competitionId = await db.RecruitmentApplications.Where(a => a.Id == decided.RecruitmentApplicationId)
            .Select(a => a.CompetitionId!.Value).FirstAsync(ct);
        var examNumber = await db.ExamCandidates.Where(c => c.Id == decided.ObjectionExamCandidateId).Select(c => c.ExamNumber).FirstAsync(ct);
        var (_, recomputeError) = await results.ComputeDraftAsync(
            competitionId, $"اعتراض مقبول على الدفتر {examNumber}", actor.Workflow.UserId, ct);
        return (true, null, recomputeError is null ? null : $"قُبل الاعتراض، لكن تعذّر احتساب نسخة النتائج الجديدة: {recomputeError}");
    }

    public Task<bool> HasPendingAsync(Guid competitionId, CancellationToken ct = default) =>
        db.RecruitmentAppeals.AnyAsync(a => a.Kind == RecruitmentAppealKind.ResultObjection
            && a.Status == RecruitmentAppealStatus.Pending
            && a.RecruitmentApplication!.CompetitionId == competitionId, ct);

    public async Task<IReadOnlyList<CompetitionObjectionRow>> ListAsync(Guid competitionId, CancellationToken ct = default) =>
        await db.RecruitmentAppeals.AsNoTracking()
            .Where(a => a.Kind == RecruitmentAppealKind.ResultObjection && a.RecruitmentApplication!.CompetitionId == competitionId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new CompetitionObjectionRow(
                a.Id,
                a.RecruitmentApplicationId,
                a.RecruitmentApplication!.ApplicantFullName,
                db.ExamCandidates.Where(c => c.Id == a.ObjectionExamCandidateId).Select(c => c.ExamNumber).FirstOrDefault(),
                a.Status,
                a.Outcome,
                a.ObjectionCorrectedScore,
                a.FiledBy,
                a.CreatedAt,
                a.IndependentReviewerEmployeeId != null || a.IndependentReviewerId != null))
            .ToListAsync(ct);
}
