using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-026: structured multi-assessor scoring. <see cref="RecruitmentApplication.AssessmentScore"/> is computed,
/// never entered, and the move to <see cref="RecruitmentApplicationStatus.Assessed"/> happens only here.
/// </summary>
public sealed class RecruitmentScoringService(
    HRCoreDbContext db,
    RecruitmentInterviewService interviewService,
    IOptions<RecruitmentOptions> options)
{
    public const int MaxNotesLength = 1000;

    public const string InterviewGateMessage =
        "بنود مرحلة المقابلة لا تُقيَّم إلا بعد إكمال المقابلة بنتيجة «حضر».";

    public async Task<(bool Success, string? Error)> SubmitScoresAsync(
        Guid applicationId,
        Guid assessorUserId,
        Guid? assessorEmployeeId,
        IReadOnlyDictionary<string, decimal> scores,
        string? notes,
        bool noConflictDeclared,
        CancellationToken ct = default)
    {
        if (assessorUserId == Guid.Empty)
            return (false, "تعذّر تحديد هوية المقيّم.");

        var app = await db.RecruitmentApplications
            .Include(a => a.SourceInternalApplication)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");
        if (app.RequisitionId is not Guid requisitionId)
            return (false, "التقييم المنظم يتطلب طلب شاغر.");

        var criteria = await db.RecruitmentAssessmentCriteria
            .FirstOrDefaultAsync(c => c.RequisitionId == requisitionId && c.IsActive, ct);
        if (criteria is null)
            return (false, "لا توجد معايير تقييم نشطة لطلب الشاغر.");

        if (app.Status is not (RecruitmentApplicationStatus.InterviewScheduled or RecruitmentApplicationStatus.Assessed))
            return (false, "يمكن التقييم فقط للطلبات في حالة «مقابلة مجدولة» أو «مُقيَّم»؛ الدرجات مُقفلة بعد الترتيب.");

        if (!noConflictDeclared)
            return (false, "يجب الإقرار بعدم وجود تعارض مصالح مع هذا المرشح قبل التقييم.");

        if (assessorEmployeeId is Guid employeeId)
        {
            if (app.SourceInternalApplication?.EmployeeId == employeeId)
                return (false, "لا يجوز للمتقدم تقييم طلبه.");

            var conflicted = await db.SelectionCommitteeMembers.AnyAsync(
                m => m.EmployeeId == employeeId
                    && m.HasConflictOfInterest
                    && m.Session != null
                    && m.Session.RequisitionId == requisitionId,
                ct);
            if (conflicted)
                return (false, "المقيّم مسجّل بتعارض مصالح في لجنة اختيار هذا الطلب.");
        }

        if (scores is null || scores.Count == 0)
            return (false, "يجب إدخال درجة بند واحد على الأقل.");

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is { Length: > MaxNotesLength })
            return (false, $"الملاحظات يجب ألا تتجاوز {MaxNotesLength} حرف.");

        var items = AssessmentCriteriaSerializer.Deserialize(criteria.CriteriaJson);
        var byKey = items.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in scores)
        {
            if (!byKey.TryGetValue(key, out var item))
                return (false, $"البند «{key}» غير موجود في معايير التقييم النشطة.");
            if (value < 0 || value > item.MaxScore)
                return (false, $"درجة البند «{item.NameAr}» يجب أن تكون بين 0 و {item.MaxScore:0.##}.");
        }

        var interviewGateError = await ValidateInterviewGateForScoresAsync(app.Id, scores.Keys, byKey, ct);
        if (interviewGateError is not null)
            return (false, interviewGateError);

        var now = DateTime.UtcNow;
        var existing = await db.RecruitmentAssessmentScores
            .Where(s => s.RecruitmentApplicationId == app.Id && s.AssessorUserId == assessorUserId)
            .ToListAsync(ct);

        foreach (var (rawKey, value) in scores)
        {
            var key = byKey[rawKey].Key;
            var row = existing.FirstOrDefault(s => string.Equals(s.ItemKey, key, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new RecruitmentAssessmentScore
                {
                    TenantId = app.TenantId,
                    RecruitmentApplicationId = app.Id,
                    AssessorUserId = assessorUserId,
                    ItemKey = key
                };
                db.RecruitmentAssessmentScores.Add(row);
                existing.Add(row);
            }

            row.CriteriaVersionId = criteria.Id;
            row.AssessorEmployeeId = assessorEmployeeId;
            row.Score = value;
            row.Notes = trimmedNotes;
            row.NoConflictDeclared = true;
            row.SubmittedAt = now;
        }

        criteria.LockedAt ??= now;
        app.CriteriaVersionId = criteria.Id;
        await db.SaveChangesAsync(ct);

        await ComputeAsync(app.Id, ct);
        return (true, null);
    }

    /// <summary>
    /// Recomputes <see cref="RecruitmentApplication.AssessmentScore"/> from the stored scores and
    /// moves <c>InterviewScheduled → Assessed</c> once enough assessors are complete.
    /// </summary>
    public async Task<AssessmentSummary> ComputeAsync(Guid applicationId, CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications
            .Include(a => a.ActiveOfferLetter)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null)
            return AssessmentSummary.Empty(options.Value.MinAssessors);

        var summary = await BuildSummaryAsync(app, ct);
        if (summary.Criteria is null)
            return summary;

        // Scores lock once the application is Ranked or later; keep the recorded result.
        if (app.Status is RecruitmentApplicationStatus.InterviewScheduled or RecruitmentApplicationStatus.Assessed)
            app.AssessmentScore = summary.FinalScore;

        if (app.Status == RecruitmentApplicationStatus.InterviewScheduled
            && summary.CompleteAssessors >= Math.Max(1, options.Value.MinAssessors)
            && await IsInterviewGateSatisfiedAsync(app.Id, summary.Items, ct)
            && SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Assessed))
        {
            app.Status = RecruitmentApplicationStatus.Assessed;
            app.ReviewedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return summary;
    }

    /// <summary>Read-only scoring summary for the application details page.</summary>
    public async Task<AssessmentSummary> GetSummaryAsync(Guid applicationId, CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        return app is null
            ? AssessmentSummary.Empty(options.Value.MinAssessors)
            : await BuildSummaryAsync(app, ct);
    }

    private async Task<AssessmentSummary> BuildSummaryAsync(RecruitmentApplication app, CancellationToken ct)
    {
        if (app.RequisitionId is not Guid requisitionId)
            return AssessmentSummary.Empty(options.Value.MinAssessors);

        var criteria = app.CriteriaVersionId is Guid pinned
            ? await db.RecruitmentAssessmentCriteria.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pinned, ct)
            : null;
        criteria ??= await db.RecruitmentAssessmentCriteria
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RequisitionId == requisitionId && c.IsActive, ct);
        if (criteria is null)
            return AssessmentSummary.Empty(options.Value.MinAssessors);

        var items = AssessmentCriteriaSerializer.Deserialize(criteria.CriteriaJson);
        var rows = await db.RecruitmentAssessmentScores
            .AsNoTracking()
            .Where(s => s.RecruitmentApplicationId == app.Id && s.CriteriaVersionId == criteria.Id)
            .ToListAsync(ct);

        var assessors = rows
            .GroupBy(r => r.AssessorUserId)
            .Select(g =>
            {
                var scores = g.Select(r => new AssessmentScoringRules.AssessorScore(r.ItemKey, r.Score)).ToList();
                var result = AssessmentScoringRules.ComputeAssessor(g.Key, items, scores);
                return new AssessorSummary(
                    g.Key,
                    g.Select(r => r.AssessorEmployeeId).FirstOrDefault(e => e.HasValue),
                    result.WeightedTotal,
                    result.IsComplete,
                    result.ScoredItems,
                    g.Max(r => r.SubmittedAt),
                    g.ToDictionary(r => r.ItemKey, r => r.Score, StringComparer.OrdinalIgnoreCase),
                    g.Select(r => r.Notes).FirstOrDefault(n => n is not null));
            })
            .OrderBy(a => a.LastSubmittedAt)
            .ToList();

        var final = AssessmentScoringRules.ComputeFinal(
            items,
            rows.GroupBy(r => r.AssessorUserId)
                .Select(g => g.Select(r => new AssessmentScoringRules.AssessorScore(r.ItemKey, r.Score))));

        return new AssessmentSummary(
            criteria,
            items,
            assessors,
            final,
            assessors.Count(a => a.IsComplete),
            options.Value.MinAssessors);
    }

    /// <summary>Interview-stage items can be scored only after an interview completed with outcome Attended.</summary>
    private async Task<string?> ValidateInterviewGateForScoresAsync(
        Guid applicationId,
        IEnumerable<string> submittedKeys,
        IReadOnlyDictionary<string, AssessmentCriteriaItem> byKey,
        CancellationToken ct)
    {
        var touchesInterview = submittedKeys.Any(k => byKey[k].Stage == AssessmentStage.Interview);
        if (!touchesInterview || await interviewService.HasAttendedInterviewAsync(applicationId, ct))
            return null;

        return InterviewGateMessage;
    }

    /// <summary>If the criteria contain Interview-stage items, Assessed requires an attended interview.</summary>
    public async Task<bool> IsInterviewGateSatisfiedAsync(
        Guid applicationId,
        IReadOnlyList<AssessmentCriteriaItem> items,
        CancellationToken ct = default) =>
        !items.Any(i => i.Stage == AssessmentStage.Interview)
        || await interviewService.HasAttendedInterviewAsync(applicationId, ct);
}

public sealed record AssessorSummary(
    Guid AssessorUserId,
    Guid? AssessorEmployeeId,
    decimal WeightedTotal,
    bool IsComplete,
    int ScoredItems,
    DateTime LastSubmittedAt,
    IReadOnlyDictionary<string, decimal> Scores,
    string? Notes);

public sealed record AssessmentSummary(
    RecruitmentAssessmentCriteria? Criteria,
    IReadOnlyList<AssessmentCriteriaItem> Items,
    IReadOnlyList<AssessorSummary> Assessors,
    decimal? FinalScore,
    int CompleteAssessors,
    int MinAssessors)
{
    public static AssessmentSummary Empty(int minAssessors) => new(null, [], [], null, 0, minAssessors);
}
