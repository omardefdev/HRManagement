using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: one row of the public results list (exam number and outcome only).</summary>
public sealed record PublicResultRow(string ExamNumber, CompetitionResultOutcome Outcome);

/// <summary>HCM-031: the public results page of a competition.</summary>
public sealed record PublicResultsView(
    RecruitmentCompetition Competition,
    CompetitionResultVersion Version,
    IReadOnlyList<PublicResultRow> Rows,
    int Total,
    int Page,
    int PageSize,
    DateTime? ObjectionDeadlineUtc);

/// <summary>
/// HCM-031 (PLAN-31, P4, AC-HCM-31d/31e): competition results — compute a draft version from the closed exam marks and
/// the non-exam assessments (PLAN-26), HR tie decisions, approval through <c>RECRUITMENT-RESULTS-APPROVAL</c> (a committee
/// role step then the HR director, distinct people, never the person who computed the draft), publication (the previous
/// version is superseded, the objection window starts at the first publication), and the public list.
/// </summary>
public sealed class CompetitionResultsService(HRCoreDbContext db, IWorkflowStarter workflowStarter)
{
    public const string ApprovalWorkflowCode = "RECRUITMENT-RESULTS-APPROVAL";
    public const string EntityType = nameof(CompetitionResultVersion);
    public const int PublicPageSize = 100;

    /// <summary>
    /// Computes a new draft version (or recomputes the open draft). First version: Examining → ResultsDraft. A later
    /// version (upheld objection) carries <paramref name="reason"/> and the rank/outcome diff against the published one.
    /// </summary>
    public async Task<(CompetitionResultVersion? Version, string? Error)> ComputeDraftAsync(
        Guid competitionId, string? reason, Guid? actorUserId, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (null, CompetitionLifecycleService.NotFoundMessage);
        if (competition.Status is not (RecruitmentCompetitionStatus.Examining or RecruitmentCompetitionStatus.ResultsDraft
            or RecruitmentCompetitionStatus.ResultsPublished))
            return (null, "تُحتسب النتائج بعد انتهاء الامتحانات وقبل إنهاء المسابقة.");
        if (await db.CompetitionResultVersions.AnyAsync(v => v.CompetitionId == competitionId
                && (v.Status == CompetitionResultVersionStatus.PendingApproval || v.Status == CompetitionResultVersionStatus.Approved), ct))
            return (null, "توجد نسخة نتائج قيد الاعتماد أو معتمدة لم تُنشر بعد.");

        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.CompetitionId == competitionId).ToListAsync(ct);
        if (sessions.Count == 0 || sessions.Any(s => s.Status != ExamSessionStatus.MarkingClosed))
            return (null, "يجب إغلاق التصحيح في كل جلسات الامتحان قبل احتساب النتائج.");

        var referenceLine = competition.Lines.OrderBy(l => l.LineNo).FirstOrDefault(l => l.CriteriaVersionId != null);
        if (referenceLine is null)
            return (null, "لا توجد معايير تقييم مثبتة لأسطر المسابقة.");
        var criteria = await db.RecruitmentAssessmentCriteria.AsNoTracking().FirstAsync(c => c.Id == referenceLine.CriteriaVersionId, ct);
        var items = AssessmentCriteriaSerializer.Deserialize(criteria.CriteriaJson);
        var examWeight = items.Where(i => i.Stage == AssessmentStage.Exam).Sum(i => i.Weight);
        var nonExamItems = items.Where(i => i.Stage != AssessmentStage.Exam).ToList();
        var interviewItems = items.Where(i => i.Stage == AssessmentStage.Interview).ToList();

        var sessionMax = sessions.ToDictionary(s => s.Id, s => s.MaxScore);
        var applications = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.CompetitionId == competitionId && a.Status == RecruitmentApplicationStatus.Shortlisted)
            .Select(a => new { a.Id, a.AppliedAt, a.YearsOfExperience })
            .ToListAsync(ct);
        var papers = (await db.ExamCandidates.AsNoTracking()
                .Where(c => c.CompetitionId == competitionId)
                .Select(c => new { c.RecruitmentApplicationId, c.ExamSessionId, c.ExamNumber, c.Attendance, c.FinalMark })
                .ToListAsync(ct))
            .ToLookup(p => p.RecruitmentApplicationId);
        var preferences = (await db.CompetitionPreferences.AsNoTracking()
                .Where(p => p.CompetitionLine!.CompetitionId == competitionId)
                .Select(p => new { p.RecruitmentApplicationId, p.CompetitionLineId })
                .ToListAsync(ct))
            .ToLookup(p => p.RecruitmentApplicationId, p => p.CompetitionLineId);
        var lines = competition.Lines.ToDictionary(l => l.Id);
        var scores = nonExamItems.Count == 0
            ? new Dictionary<Guid, List<(Guid Assessor, string Key, decimal Score)>>()
            : (await db.RecruitmentAssessmentScores.AsNoTracking()
                    .Where(s => s.CriteriaVersionId == criteria.Id && s.RecruitmentApplication!.CompetitionId == competitionId)
                    .Select(s => new { s.RecruitmentApplicationId, s.AssessorUserId, s.ItemKey, s.Score })
                    .ToListAsync(ct))
                .GroupBy(s => s.RecruitmentApplicationId)
                .ToDictionary(g => g.Key, g => g.Select(s => (s.AssessorUserId, s.ItemKey, s.Score)).ToList());

        var inputs = new List<CompetitionScoreInput>(applications.Count);
        var missingPapers = 0;
        var incomplete = 0;
        foreach (var app in applications)
        {
            var appPapers = papers[app.Id].ToList();
            if (appPapers.Count == 0)
            {
                missingPapers++;
                continue;
            }
            var absent = appPapers.Any(p => p.Attendance != ExamAttendance.Present || p.FinalMark is null);
            decimal? nonExam = 0m, interview = 0m;
            if (!absent && nonExamItems.Count > 0)
            {
                var assessorScores = scores.TryGetValue(app.Id, out var list) ? list : [];
                nonExam = Points(nonExamItems, assessorScores);
                interview = interviewItems.Count == 0 ? 0m : Points(interviewItems, assessorScores);
                if (nonExam is null)
                    incomplete++;
            }
            var passMarks = preferences[app.Id].Where(lines.ContainsKey)
                .Select(id => CompetitionWorkflowRules.EffectivePassMark(competition, lines[id])).ToList();
            inputs.Add(new CompetitionScoreInput(
                app.Id,
                appPapers[0].ExamNumber,
                absent,
                absent ? [] : appPapers.Select(p => (p.FinalMark!.Value, sessionMax[p.ExamSessionId])).ToList(),
                nonExam,
                interview,
                app.YearsOfExperience,
                app.AppliedAt,
                passMarks.Count == 0 ? competition.PassMark : passMarks.Min()));
        }
        if (missingPapers > 0)
            return (null, $"لم يُوزَّع {missingPapers} متقدماً مقبولاً على أي جلسة امتحان.");
        if (incomplete > 0)
            return (null, $"لم يكتمل تقييم المراحل غير الامتحانية لـ {incomplete} متقدماً حاضراً.");

        var scored = CompetitionScoringRules.Compute(inputs, examWeight, CompetitionWorkflowRules.ParseTieBreakOrder(competition.TieBreakOrder));

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var draft = await db.CompetitionResultVersions
            .FirstOrDefaultAsync(v => v.CompetitionId == competitionId && v.Status == CompetitionResultVersionStatus.Draft, ct);
        if (draft is not null)
        {
            await db.CompetitionResults.Where(r => r.ResultVersionId == draft.Id).ExecuteDeleteAsync(ct);
        }
        else
        {
            var versionNo = (await db.CompetitionResultVersions.Where(v => v.CompetitionId == competitionId)
                .Select(v => (int?)v.VersionNo).MaxAsync(ct) ?? 0) + 1;
            draft = new CompetitionResultVersion
            {
                TenantId = competition.TenantId,
                CompetitionId = competitionId,
                VersionNo = versionNo
            };
            db.CompetitionResultVersions.Add(draft);
        }
        draft.ComputedAt = DateTime.UtcNow;
        draft.ComputedBy = actorUserId;
        draft.Reason = string.IsNullOrWhiteSpace(reason) ? draft.Reason : reason.Trim()[..Math.Min(1000, reason.Trim().Length)];
        draft.ReturnReason = null;
        await db.SaveChangesAsync(ct);

        var rows = scored.Select(s => new CompetitionResult
        {
            TenantId = competition.TenantId,
            ResultVersionId = draft.Id,
            RecruitmentApplicationId = s.ApplicationId,
            ExamNumber = s.ExamNumber,
            ExamScore = s.ExamScore,
            InterviewScore = s.InterviewScore,
            FinalScore = s.FinalScore,
            OverallRank = s.Rank,
            Outcome = s.Outcome,
            TieUnresolved = s.TieUnresolved
        }).ToList();
        await InsertBatchedAsync(rows, ct);

        draft.Sha256 = CompetitionScoringRules.ComputeSha256(draft.VersionNo, rows);
        if (competition.CurrentResultVersionId is Guid publishedId)
        {
            var published = await db.CompetitionResults.AsNoTracking().Where(r => r.ResultVersionId == publishedId).ToListAsync(ct);
            draft.DiffJson = JsonSerializer.Serialize(CompetitionScoringRules.Diff(published, rows));
        }
        if (competition.Status == RecruitmentCompetitionStatus.Examining)
            competition.Status = RecruitmentCompetitionStatus.ResultsDraft;
        Audit(competition.TenantId, competitionId, "ComputeResultsDraft", actorUserId,
            new { draft.VersionNo, candidates = rows.Count, ties = rows.Count(r => r.TieUnresolved), reason = draft.Reason });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return (draft, null);
    }

    /// <summary>HR orders one unresolved tie group of a draft (reason ≥ 10 characters, audited); the hash is recomputed.</summary>
    public async Task<(bool Success, string? Error)> ResolveTieAsync(
        Guid versionId, IReadOnlyList<Guid> orderedApplicationIds, string? reason, Guid? actorUserId, CancellationToken ct = default)
    {
        var version = await db.CompetitionResultVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
            return (false, "نسخة النتائج غير موجودة.");
        if (version.Status != CompetitionResultVersionStatus.Draft)
            return (false, "يُعدَّل التعادل في مسودة النتائج فقط.");
        var results = await db.CompetitionResults.Where(r => r.ResultVersionId == versionId).ToListAsync(ct);
        var error = CompetitionScoringRules.ValidateTieDecision(
            results.Select(r => (r.RecruitmentApplicationId, r.OverallRank, r.TieUnresolved)).ToList(), orderedApplicationIds, reason);
        if (error is not null)
            return (false, error);

        var group = results.Where(r => orderedApplicationIds.Contains(r.RecruitmentApplicationId)).ToList();
        var startRank = group[0].OverallRank!.Value;
        for (var i = 0; i < orderedApplicationIds.Count; i++)
        {
            var row = group.Single(r => r.RecruitmentApplicationId == orderedApplicationIds[i]);
            row.OverallRank = startRank + i;
            row.TieUnresolved = false;
            row.TieDecisionReason = reason!.Trim()[..Math.Min(500, reason.Trim().Length)];
        }
        version.Sha256 = CompetitionScoringRules.ComputeSha256(version.VersionNo, results);
        Audit(version.TenantId, version.CompetitionId, "ResolveResultsTie", actorUserId,
            new { version.VersionNo, rank = startRank, size = orderedApplicationIds.Count, reason = reason!.Trim() });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Draft → PendingApproval (no unresolved tie); starts <see cref="ApprovalWorkflowCode"/>.</summary>
    public async Task<(bool Success, string? Error)> SubmitForApprovalAsync(
        Guid versionId, Guid? actorUserId, string? actorUserName, CancellationToken ct = default)
    {
        var version = await db.CompetitionResultVersions.Include(v => v.Competition).FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version?.Competition is null)
            return (false, "نسخة النتائج غير موجودة.");
        if (version.Status != CompetitionResultVersionStatus.Draft)
            return (false, "تُقدَّم مسودة النتائج للاعتماد فقط.");
        var ties = await db.CompetitionResults.CountAsync(r => r.ResultVersionId == versionId && r.TieUnresolved, ct);
        if (ties > 0)
            return (false, $"{CompetitionScoringRules.UnresolvedTieMessage}: {ties} متقدماً في حالة تعادل؛ يجب البت فيه قبل الاعتماد.");

        version.Status = CompetitionResultVersionStatus.PendingApproval;
        version.WorkflowInstanceId = await workflowStarter.StartByWorkflowCodeAsync(
            ApprovalWorkflowCode,
            EntityType,
            version.Id,
            version.TenantId,
            actorUserId,
            actorUserName,
            JsonSerializer.Serialize(new { competitionId = version.CompetitionId, version.VersionNo }),
            PolicyDomains.CoreRecruitment,
            ct);
        if (version.WorkflowInstanceId is null)
            return (false, "تعذّر بدء مسار اعتماد النتائج: تعريف المسار غير منشور.");
        if (version.Competition.Status == RecruitmentCompetitionStatus.ResultsDraft)
            version.Competition.Status = RecruitmentCompetitionStatus.ResultsPendingApproval;
        Audit(version.TenantId, version.CompetitionId, "SubmitResults", actorUserId, new { version.VersionNo });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Approved → Published: the previous published version is superseded, the competition points to this one, the
    /// objection window starts at the first publication (a later version never reopens it), and a <c>ResultsPublished</c>
    /// event is recorded per candidate (the message carries no score or rank).
    /// </summary>
    public async Task<(bool Success, string? Error)> PublishAsync(Guid versionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var version = await db.CompetitionResultVersions.Include(v => v.Competition).FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version?.Competition is null)
            return (false, "نسخة النتائج غير موجودة.");
        if (version.Status != CompetitionResultVersionStatus.Approved)
            return (false, "تُنشر نسخة النتائج بعد اعتمادها فقط.");
        var competition = version.Competition;
        var now = DateTime.UtcNow;

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var previous = await db.CompetitionResultVersions
            .Where(v => v.CompetitionId == competition.Id && v.Status == CompetitionResultVersionStatus.Published)
            .ToListAsync(ct);
        foreach (var old in previous)
            old.Status = CompetitionResultVersionStatus.Superseded;
        await db.SaveChangesAsync(ct);

        version.Status = CompetitionResultVersionStatus.Published;
        version.PublishedAt = now;
        competition.CurrentResultVersionId = version.Id;
        competition.ResultsFirstPublishedAt ??= now;
        if (competition.Status is RecruitmentCompetitionStatus.ResultsPendingApproval or RecruitmentCompetitionStatus.ResultsDraft)
            competition.Status = RecruitmentCompetitionStatus.ResultsPublished;

        var applicationIds = await db.CompetitionResults.AsNoTracking()
            .Where(r => r.ResultVersionId == versionId)
            .Select(r => r.RecruitmentApplicationId)
            .ToListAsync(ct);
        foreach (var batch in applicationIds.Chunk(1000))
            db.RecruitmentApplicationEvents.AddRange(batch.Select(id => CompetitionEvents.New(
                competition.TenantId, id, RecruitmentApplicationEventTypes.ResultsPublished, version.Id, now)));
        Audit(competition.TenantId, competition.Id, "PublishResults", actorUserId,
            new { version.VersionNo, superseded = previous.Select(p => p.VersionNo).ToList(), candidates = applicationIds.Count });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return (true, null);
    }

    /// <summary>The public list of the current published version (exam number + outcome only; anonymous portal).</summary>
    public async Task<PublicResultsView?> GetPublicAsync(Guid competitionId, int page, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition?.CurrentResultVersionId is not Guid versionId)
            return null;
        var version = await db.CompetitionResultVersions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .FirstAsync(v => v.TenantId == competition.TenantId && v.Id == versionId, ct);
        var results = db.CompetitionResults
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .Where(r => r.TenantId == competition.TenantId && r.ResultVersionId == versionId);
        var total = await results.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PublicPageSize)));
        var rows = await results
            .OrderBy(r => r.ExamNumber)
            .Skip((page - 1) * PublicPageSize)
            .Take(PublicPageSize)
            .Select(r => new PublicResultRow(r.ExamNumber, r.Outcome))
            .ToListAsync(ct);
        return new PublicResultsView(competition, version, rows, total, page, PublicPageSize,
            CompetitionWorkflowRules.ObjectionDeadlineUtc(competition.ResultsFirstPublishedAt, competition.ObjectionWindowDays));
    }

    /// <summary>All rows of the current published version for the PDF (exam number + outcome).</summary>
    public async Task<IReadOnlyList<PublicResultRow>> GetPublicRowsAsync(Guid tenantId, Guid versionId, CancellationToken ct = default) =>
        await db.CompetitionResults
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ResultVersionId == versionId)
            .OrderBy(r => r.ExamNumber)
            .Select(r => new PublicResultRow(r.ExamNumber, r.Outcome))
            .ToListAsync(ct);

    /// <summary>Weighted points of the given items: the average of complete assessors (PLAN-26 rules); null when none is complete.</summary>
    private static decimal? Points(IReadOnlyList<AssessmentCriteriaItem> items, IReadOnlyList<(Guid Assessor, string Key, decimal Score)> scores)
    {
        var keys = items.Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byAssessor = scores.Where(s => keys.Contains(s.Key))
            .GroupBy(s => s.Assessor)
            .Select(g => g.Select(s => new AssessmentScoringRules.AssessorScore(s.Key, s.Score)))
            .ToList();
        return AssessmentScoringRules.ComputeFinal(items, byAssessor);
    }

    private async Task InsertBatchedAsync(IReadOnlyList<CompetitionResult> rows, CancellationToken ct)
    {
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var batch in rows.Chunk(1000))
            {
                db.CompetitionResults.AddRange(batch);
                db.ChangeTracker.DetectChanges();
                await db.SaveChangesAsync(ct);
                foreach (var row in batch)
                    db.Entry(row).State = EntityState.Detached;
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private void Audit(Guid tenantId, Guid competitionId, string action, Guid? actorUserId, object details) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = tenantId,
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = competitionId,
            Action = action,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(details)
        });
}
