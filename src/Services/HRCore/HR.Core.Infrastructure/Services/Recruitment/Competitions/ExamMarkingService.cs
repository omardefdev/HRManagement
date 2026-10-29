using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: who is marking — the token subject and the employee claim (the assignment is by employee).</summary>
public sealed record ExamMarkerActor(Guid UserId, Guid? EmployeeId, bool HoldsHrReadAccess);

/// <summary>HCM-031: a session as the marker sees it (no competition applicant data).</summary>
public sealed record MarkerSessionView(Guid AssignmentId, Guid SessionId, string Title, ExamSessionType Type, DateTime ScheduledAt,
    ExamMarkerRole Role, ExamSessionStatus Status, decimal MaxScore, int Papers, int Done);

/// <summary>HCM-031: a paper as the marker sees it — the exam number and marks only (the id is the exam candidate id).</summary>
public sealed record MarkerPaperView(Guid ExamCandidateId, string ExamNumber, decimal MaxScore, decimal? OwnMark,
    decimal? FirstMark, decimal? SecondMark, bool Locked);

/// <summary>
/// HCM-031 (PLAN-31, P3, AC-HCM-31c): anonymous, blind double marking. A marker sees the papers of the sessions assigned
/// to him by exam number only. First and second rounds never see another mark; when both are in and differ by more than
/// the competition's threshold the paper goes to the reconciler, who then sees both. Marks are locked once marking is
/// closed. Every submission is audited (marker, round, time, exam candidate id — never the applicant).
/// </summary>
public sealed class ExamMarkingService(HRCoreDbContext db)
{
    public const string AuditAction = "SubmitExamMark";

    /// <summary>
    /// The marker's assignments (by employee claim). Binds the token subject on first visit; a token that also carries
    /// HR.Core read roles flags the assignment («يملك هذا المصحح صلاحية الاطلاع على هوية المتقدمين») and is audited.
    /// </summary>
    public async Task<IReadOnlyList<MarkerSessionView>> GetSessionsAsync(ExamMarkerActor actor, CancellationToken ct = default)
    {
        var assignments = await MyAssignments(actor).Include(a => a.ExamSession).ToListAsync(ct);
        await BindAsync(assignments, actor, ct);

        var views = new List<MarkerSessionView>();
        foreach (var a in assignments.Where(a => a.ExamSession!.Status is ExamSessionStatus.Held or ExamSessionStatus.MarkingClosed))
        {
            var papers = await PapersFor(a).CountAsync(ct);
            var round = ExamMarkingRules.RoundFor(a.Role);
            var done = await db.ExamScores.CountAsync(s => s.ExamCandidate!.ExamSessionId == a.ExamSessionId && s.Round == round, ct);
            var s = a.ExamSession!;
            views.Add(new(a.Id, s.Id, s.Title, s.Type, s.ScheduledAt, a.Role, s.Status, s.MaxScore, papers, done));
        }
        return views.OrderBy(v => v.ScheduledAt).ToList();
    }

    /// <summary>The papers of one session for the marker's role (reconciler: only the papers that need reconciliation).</summary>
    public async Task<(IReadOnlyList<MarkerPaperView> Papers, ExamMarkerAssignment? Assignment)> GetPapersAsync(
        Guid sessionId, ExamMarkerActor actor, CancellationToken ct = default)
    {
        var assignment = await MyAssignments(actor).Include(a => a.ExamSession).ThenInclude(s => s!.Competition)
            .FirstOrDefaultAsync(a => a.ExamSessionId == sessionId, ct);
        if (assignment is null || assignment.ExamSession!.Status is not (ExamSessionStatus.Held or ExamSessionStatus.MarkingClosed))
            return ([], null);
        await BindAsync([assignment], actor, ct);

        var rows = await PapersFor(assignment)
            .OrderBy(c => c.ExamNumber)
            .Select(c => new
            {
                c.Id,
                c.ExamNumber,
                Marks = c.Scores.Select(s => new { s.Round, s.Score }).ToList()
            })
            .ToListAsync(ct);
        var session = assignment.ExamSession!;
        var threshold = session.Competition!.ReconcileThreshold;
        var own = ExamMarkingRules.RoundFor(assignment.Role);
        var locked = session.Status == ExamSessionStatus.MarkingClosed;
        var papers = new List<MarkerPaperView>();
        foreach (var row in rows)
        {
            var marks = MarksOf(row.Marks.Select(m => (m.Round, m.Score)));
            if (assignment.Role == ExamMarkerRole.Reconciler
                && !(marks.First is decimal a && marks.Second is decimal b && ExamMarkingRules.NeedsReconciliation(a, b, threshold)))
                continue;
            var visible = ExamMarkingRules.VisibleMarks(assignment.Role, marks);
            papers.Add(new(row.Id, row.ExamNumber, session.MaxScore,
                row.Marks.Where(m => m.Round == own).Select(m => (decimal?)m.Score).FirstOrDefault(),
                visible.First, visible.Second, locked));
        }
        return (papers, assignment);
    }

    /// <summary>One paper for the marker (null when it is not one of his papers).</summary>
    public async Task<MarkerPaperView?> GetPaperAsync(Guid examCandidateId, ExamMarkerActor actor, CancellationToken ct = default)
    {
        var sessionId = await db.ExamCandidates.Where(c => c.Id == examCandidateId).Select(c => (Guid?)c.ExamSessionId).FirstOrDefaultAsync(ct);
        if (sessionId is null)
            return null;
        var (papers, _) = await GetPapersAsync(sessionId.Value, actor, ct);
        return papers.FirstOrDefault(p => p.ExamCandidateId == examCandidateId);
    }

    /// <summary>Submits (or, before marking closes, corrects) the marker's mark of a paper in the round of his role.</summary>
    public async Task<(bool Success, string? Error)> SubmitAsync(
        Guid examCandidateId, decimal score, string? note, ExamMarkerActor actor, CancellationToken ct = default)
    {
        var candidate = await db.ExamCandidates
            .Include(c => c.ExamSession).ThenInclude(s => s!.Competition)
            .Include(c => c.Scores)
            .FirstOrDefaultAsync(c => c.Id == examCandidateId, ct);
        if (candidate?.ExamSession is null)
            return (false, ExamMarkingRules.NotAssignedMessage);
        var session = candidate.ExamSession;
        var assignment = await MyAssignments(actor).FirstOrDefaultAsync(a => a.ExamSessionId == session.Id, ct);
        if (assignment is null)
            return (false, ExamMarkingRules.NotAssignedMessage);
        if (session.Status == ExamSessionStatus.MarkingClosed)
            return (false, ExamMarkingRules.SessionLockedMessage);
        if (session.Status != ExamSessionStatus.Held)
            return (false, "يبدأ التصحيح بعد اعتماد انعقاد الجلسة.");
        if (candidate.Attendance != ExamAttendance.Present)
            return (false, ExamMarkingRules.AbsentMessage);
        var scoreError = ExamMarkingRules.ValidateScore(score, session.MaxScore);
        if (scoreError is not null)
            return (false, scoreError);
        if (note is { Length: > ExamMarkingRules.MaxNoteLength })
            return (false, $"الملاحظة يجب ألا تتجاوز {ExamMarkingRules.MaxNoteLength} حرف.");

        var round = ExamMarkingRules.RoundFor(assignment.Role);
        if (candidate.Scores.Any(s => s.Round != round && s.Round != ExamScoreRound.ObjectionCorrection && s.MarkerUserId == actor.UserId))
            return (false, ExamMarkingRules.SamePersonMessage);
        var marks = MarksOf(candidate.Scores.Select(s => (s.Round, s.Score)));
        if (round == ExamScoreRound.Reconciler
            && !(marks.First is decimal a && marks.Second is decimal b
                 && ExamMarkingRules.NeedsReconciliation(a, b, session.Competition!.ReconcileThreshold)))
            return (false, ExamMarkingRules.ReconcilerNotNeededMessage);

        var now = DateTime.UtcNow;
        var existing = candidate.Scores.FirstOrDefault(s => s.Round == round);
        if (existing is null)
        {
            db.ExamScores.Add(new ExamScore
            {
                TenantId = candidate.TenantId,
                ExamCandidateId = candidate.Id,
                MarkerUserId = actor.UserId,
                Round = round,
                Score = score,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                SubmittedAt = now
            });
        }
        else
        {
            if (existing.MarkerUserId != actor.UserId)
                return (false, ExamMarkingRules.NotAssignedMessage);
            existing.Score = score;
            existing.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            existing.SubmittedAt = now;
        }

        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = candidate.TenantId,
            EntityType = nameof(ExamCandidate),
            EntityId = candidate.Id,
            Action = AuditAction,
            UserId = actor.UserId,
            ChangedFields = JsonSerializer.Serialize(new { round = round.ToString(), sessionId = session.Id, at = now, corrected = existing is not null })
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent submission of the same round (unique (ExamCandidateId, Round)).
            return (false, "رُصدت علامة لهذا الدفتر في الجولة نفسها للتو؛ أعد فتح الدفتر.");
        }
        return (true, null);
    }

    /// <summary>
    /// HR closes marking of a held session: every present paper must be complete. Stores each paper's exam mark
    /// (<see cref="ExamMarkingRules.ComputeMark"/>) and locks the marks.
    /// </summary>
    public async Task<(bool Success, string? Error)> CloseMarkingAsync(Guid sessionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.Include(s => s.Competition).FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return (false, "جلسة الامتحان غير موجودة.");
        if (session.Status != ExamSessionStatus.Held)
            return (false, "يمكن إغلاق التصحيح لجلسة منعقدة فقط.");
        var competition = session.Competition!;

        var rows = await db.ExamCandidates
            .Where(c => c.ExamSessionId == sessionId && c.Attendance == ExamAttendance.Present)
            .Select(c => new { c.Id, c.ExamNumber, Marks = c.Scores.Select(s => new { s.Round, s.Score }).ToList() })
            .ToListAsync(ct);
        var finals = new Dictionary<Guid, decimal>();
        var pending = new List<string>();
        foreach (var row in rows)
        {
            var mark = ExamMarkingRules.ComputeMark(MarksOf(row.Marks.Select(m => (m.Round, m.Score))), competition.ReconcileThreshold, competition.ReconcileRule);
            if (mark is decimal m)
                finals[row.Id] = m;
            else
                pending.Add(row.ExamNumber);
        }
        if (pending.Count > 0)
            return (false, $"لم يكتمل تصحيح {pending.Count} دفتر (مثل {string.Join("، ", pending.Take(5))}).");

        foreach (var batch in finals.Chunk(1000))
        {
            var ids = batch.Select(b => b.Key).ToList();
            var candidates = await db.ExamCandidates.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
            foreach (var candidate in candidates)
                candidate.FinalMark = finals[candidate.Id];
            await db.SaveChangesAsync(ct);
        }
        session.Status = ExamSessionStatus.MarkingClosed;
        session.MarkingClosedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = session.TenantId,
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = competition.Id,
            Action = "CloseExamMarking",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new { sessionId, papers = finals.Count })
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Marking progress of a session for HR (counts only).</summary>
    public async Task<(int Present, int Complete, int AwaitingReconciler)> ProgressAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.AsNoTracking().Include(s => s.Competition).FirstAsync(s => s.Id == sessionId, ct);
        var rows = await db.ExamCandidates.AsNoTracking()
            .Where(c => c.ExamSessionId == sessionId && c.Attendance == ExamAttendance.Present)
            .Select(c => c.Scores.Select(s => new { s.Round, s.Score }).ToList())
            .ToListAsync(ct);
        var states = rows.Select(r => ExamMarkingRules.State(MarksOf(r.Select(m => (m.Round, m.Score))), session.Competition!.ReconcileThreshold)).ToList();
        return (rows.Count, states.Count(s => s == PaperMarkingState.Complete), states.Count(s => s == PaperMarkingState.AwaitingReconciler));
    }

    public static PaperMarks MarksOf(IEnumerable<(ExamScoreRound Round, decimal Score)> scores)
    {
        decimal? first = null, second = null, reconciler = null, correction = null;
        foreach (var (round, score) in scores)
        {
            switch (round)
            {
                case ExamScoreRound.First: first = score; break;
                case ExamScoreRound.Second: second = score; break;
                case ExamScoreRound.Reconciler: reconciler = score; break;
                case ExamScoreRound.ObjectionCorrection: correction = score; break;
            }
        }
        return new PaperMarks(first, second, reconciler, correction);
    }

    private IQueryable<ExamMarkerAssignment> MyAssignments(ExamMarkerActor actor) =>
        db.ExamMarkerAssignments.Where(a =>
            (actor.EmployeeId != null && a.MarkerEmployeeId == actor.EmployeeId)
            && (a.MarkerUserId == null || a.MarkerUserId == actor.UserId));

    private IQueryable<ExamCandidate> PapersFor(ExamMarkerAssignment assignment) =>
        db.ExamCandidates.Where(c => c.ExamSessionId == assignment.ExamSessionId && c.Attendance == ExamAttendance.Present);

    private async Task BindAsync(IReadOnlyList<ExamMarkerAssignment> assignments, ExamMarkerActor actor, CancellationToken ct)
    {
        var changed = false;
        foreach (var assignment in assignments)
        {
            if (assignment.MarkerUserId is null)
            {
                assignment.MarkerUserId = actor.UserId;
                changed = true;
            }
            if (actor.HoldsHrReadAccess && !assignment.HoldsHrReadAccess)
            {
                assignment.HoldsHrReadAccess = true;
                db.AuditLogs.Add(new EntityAuditLog
                {
                    TenantId = assignment.TenantId,
                    EntityType = nameof(ExamMarkerAssignment),
                    EntityId = assignment.Id,
                    Action = "ExamMarkerHoldsHrReadAccess",
                    UserId = actor.UserId,
                    ChangedFields = JsonSerializer.Serialize(new { assignment.ExamSessionId, role = assignment.Role.ToString() })
                });
                changed = true;
            }
        }
        if (changed)
            await db.SaveChangesAsync(ct);
    }
}
