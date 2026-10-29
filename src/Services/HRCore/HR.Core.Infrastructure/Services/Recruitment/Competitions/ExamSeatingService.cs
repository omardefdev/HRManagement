using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: result of a seating run.</summary>
public sealed record ExamSeatingResult(bool Success, string? Error, int Seated);

/// <summary>
/// HCM-031 (PLAN-31, P3, AC-HCM-31b): exam sessions, marker assignment, seating with random exam numbers, admission cards
/// and attendance. Seating is set-based (one save, no per-row tracking cost) so 10,000 applicants seat in seconds.
/// </summary>
public sealed class ExamSeatingService(HRCoreDbContext db, ExamAdmissionCardDocumentService cards)
{
    public const string CardsIssuedMessage = "صدرت بطاقات الامتحان؛ لا يمكن إعادة التوزيع.";
    public const string NotExaminingMessage = "يُتاح التوزيع على القاعات بعد إغلاق التقديم وقبل احتساب النتائج.";
    public const string HrReadAccessWarning = "يملك هذا المصحح صلاحية الاطلاع على هوية المتقدمين";

    public async Task<(ExamSession? Session, string? Error)> CreateSessionAsync(
        Guid competitionId, ExamSessionInput input, Guid? actorUserId, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (null, CompetitionLifecycleService.NotFoundMessage);
        if (competition.Status is not (RecruitmentCompetitionStatus.Announced or RecruitmentCompetitionStatus.IntakeClosed
            or RecruitmentCompetitionStatus.Examining))
            return (null, "تُضاف جلسات الامتحان بعد إعلان المسابقة وحتى احتساب النتائج.");
        var error = ExamMarkingRules.ValidateSession(input, competition.ReconcileThreshold);
        if (error is not null)
            return (null, error);

        var session = new ExamSession
        {
            TenantId = competition.TenantId,
            CompetitionId = competitionId,
            Type = input.Type,
            Title = input.Title!.Trim(),
            ScheduledAt = DateTime.SpecifyKind(input.ScheduledAt!.Value, DateTimeKind.Utc),
            DurationMinutes = input.DurationMinutes,
            Venue = input.Venue!.Trim(),
            Capacity = input.Capacity,
            MaxScore = input.MaxScore
        };
        db.ExamSessions.Add(session);
        Audit(competition.TenantId, competitionId, "CreateExamSession", actorUserId, new { session.Type, session.Capacity, session.MaxScore });
        await db.SaveChangesAsync(ct);
        return (session, null);
    }

    public async Task<(bool Success, string? Error)> RemoveSessionAsync(Guid sessionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return (false, "جلسة الامتحان غير موجودة.");
        if (session.Status != ExamSessionStatus.Planned || await db.ExamCandidates.AnyAsync(c => c.ExamSessionId == sessionId, ct))
            return (false, "لا يمكن حذف جلسة وُزّع عليها متقدمون.");
        db.ExamSessions.Remove(session);
        Audit(session.TenantId, session.CompetitionId, "RemoveExamSession", actorUserId, new { sessionId });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Assigns an employee as a marker of a session (distinct people per role). The token is bound on first visit.</summary>
    public async Task<(bool Success, string? Error)> AssignMarkerAsync(
        Guid sessionId, Guid employeeId, ExamMarkerRole role, Guid? actorUserId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.Include(s => s.Markers).FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return (false, "جلسة الامتحان غير موجودة.");
        if (session.Status == ExamSessionStatus.MarkingClosed)
            return (false, ExamMarkingRules.SessionLockedMessage);
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            return (false, "الموظف غير موجود.");

        var error = ExamMarkingRules.ValidateAssignment(
            session.Markers.Where(m => m.MarkerEmployeeId != null).Select(m => (m.MarkerEmployeeId!.Value, m.Role)).ToList(),
            employeeId, role);
        if (error is not null)
            return (false, error);

        db.ExamMarkerAssignments.Add(new ExamMarkerAssignment
        {
            TenantId = session.TenantId,
            ExamSessionId = sessionId,
            MarkerEmployeeId = employeeId,
            Role = role
        });
        Audit(session.TenantId, session.CompetitionId, "AssignExamMarker", actorUserId, new { sessionId, employeeId, role = role.ToString() });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveMarkerAsync(Guid assignmentId, Guid? actorUserId, CancellationToken ct = default)
    {
        var assignment = await db.ExamMarkerAssignments.Include(a => a.ExamSession).FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment?.ExamSession is null)
            return (false, "التكليف غير موجود.");
        var round = ExamMarkingRules.RoundFor(assignment.Role);
        if (await db.ExamScores.AnyAsync(s => s.ExamCandidate!.ExamSessionId == assignment.ExamSessionId && s.Round == round, ct))
            return (false, "رُصدت علامات في هذا الدور؛ لا يمكن إلغاء التكليف.");
        db.ExamMarkerAssignments.Remove(assignment);
        Audit(assignment.TenantId, assignment.ExamSession.CompetitionId, "RemoveExamMarker", actorUserId, new { assignmentId });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Seats every admitted application (<c>Shortlisted</c>) of the competition in the sessions of each exam type, by
    /// capacity and in exam-number order. Exam numbers are random, fixed length and unique per competition; an application
    /// keeps its number when re-seated and across exam types. Re-seating is allowed until admission cards are issued.
    /// IntakeClosed → Examining on the first seating.
    /// </summary>
    public async Task<ExamSeatingResult> SeatAsync(Guid competitionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return new(false, CompetitionLifecycleService.NotFoundMessage, 0);
        if (competition.Status is not (RecruitmentCompetitionStatus.IntakeClosed or RecruitmentCompetitionStatus.Examining))
            return new(false, NotExaminingMessage, 0);
        if (await db.ExamCandidates.AnyAsync(c => c.CompetitionId == competitionId && c.AdmissionCardIssuedAt != null, ct))
            return new(false, CardsIssuedMessage, 0);

        var sessions = await db.ExamSessions
            .Where(s => s.CompetitionId == competitionId)
            .OrderBy(s => s.ScheduledAt).ThenBy(s => s.Id)
            .ToListAsync(ct);
        if (sessions.Count == 0)
            return new(false, "أضف جلسة امتحان واحدة على الأقل قبل التوزيع.", 0);
        if (sessions.Any(s => s.Status is ExamSessionStatus.Held or ExamSessionStatus.MarkingClosed))
            return new(false, "عُقدت إحدى الجلسات؛ لا يمكن إعادة التوزيع.", 0);

        var admitted = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.CompetitionId == competitionId && a.Status == RecruitmentApplicationStatus.Shortlisted)
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (admitted.Count == 0)
            return new(false, "لا يوجد متقدمون مقبولون للاختبار (الحالة «في القائمة المختصرة»).", 0);

        // Keep every application's exam number (re-seating, several exam types); new ones are random and unique.
        var existing = await db.ExamCandidates.AsNoTracking()
            .Where(c => c.CompetitionId == competitionId)
            .Select(c => new { c.RecruitmentApplicationId, c.ExamNumber })
            .ToListAsync(ct);
        var numbers = existing
            .GroupBy(e => e.RecruitmentApplicationId)
            .ToDictionary(g => g.Key, g => g.First().ExamNumber);
        var used = new HashSet<string>(numbers.Values, StringComparer.Ordinal);
        foreach (var applicationId in admitted.Where(id => !numbers.ContainsKey(id)))
        {
            string number;
            do number = ExamMarkingRules.NewExamNumber();
            while (!used.Add(number));
            numbers[applicationId] = number;
        }

        var plans = new List<(ExamSession Session, ExamSeat Seat)>();
        foreach (var group in sessions.GroupBy(s => s.Type))
        {
            var ordered = group.ToList();
            var (seats, error) = ExamMarkingRules.PlanSeating(
                ordered.Select(s => (s.Id, s.Capacity)).ToList(),
                admitted.Select(id => (id, numbers[id])).ToList(),
                ExamAdmissionCardDocumentService.ExamTypeLabel(group.Key));
            if (error is not null)
                return new(false, error, 0);
            var byId = ordered.ToDictionary(s => s.Id);
            plans.AddRange(seats.Select(seat => (byId[seat.SessionId], seat)));
        }

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.ExamCandidates.Where(c => c.CompetitionId == competitionId).ExecuteDeleteAsync(ct);

        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var batch in plans.Chunk(1000))
            {
                db.ExamCandidates.AddRange(batch.Select(p => new ExamCandidate
                {
                    TenantId = competition.TenantId,
                    ExamSessionId = p.Session.Id,
                    CompetitionId = competitionId,
                    ExamType = p.Session.Type,
                    RecruitmentApplicationId = p.Seat.ApplicationId,
                    ExamNumber = numbers[p.Seat.ApplicationId],
                    SeatNumber = p.Seat.SeatNumber
                }));
                db.ChangeTracker.DetectChanges();
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                db.Attach(competition);
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        await db.ExamSessions.Where(s => s.CompetitionId == competitionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, ExamSessionStatus.Seated), ct);
        if (competition.Status == RecruitmentCompetitionStatus.IntakeClosed)
            competition.Status = RecruitmentCompetitionStatus.Examining;
        Audit(competition.TenantId, competitionId, "SeatExamCandidates", actorUserId, new { applicants = admitted.Count, seats = plans.Count });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return new(true, null, admitted.Count);
    }

    /// <summary>
    /// Issues the admission cards of every seated candidate not issued yet: stores the card hash and records an
    /// <c>AdmissionCardReady</c> event per application (for the PLAN-30 notifications). Re-seating is refused afterwards.
    /// </summary>
    public async Task<(bool Success, string? Error, int Issued)> IssueAdmissionCardsAsync(
        Guid competitionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
            return (false, CompetitionLifecycleService.NotFoundMessage, 0);
        if (competition.Status != RecruitmentCompetitionStatus.Examining)
            return (false, "تصدر البطاقات بعد التوزيع على القاعات.", 0);

        var sessions = await db.ExamSessions.AsNoTracking().Where(s => s.CompetitionId == competitionId).ToDictionaryAsync(s => s.Id, ct);
        var now = DateTime.UtcNow;
        var issued = 0;
        while (true)
        {
            var batch = await db.ExamCandidates
                .Where(c => c.CompetitionId == competitionId && c.AdmissionCardIssuedAt == null)
                .OrderBy(c => c.ExamNumber)
                .Take(1000)
                .Select(c => new { Candidate = c, Name = c.RecruitmentApplication!.ApplicantFullName })
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;
            foreach (var row in batch)
            {
                var data = ExamAdmissionCardDocumentService.BuildData(
                    competition, sessions[row.Candidate.ExamSessionId], row.Candidate, row.Name, cards.Instructions);
                row.Candidate.AdmissionCardSha256 = ExamAdmissionCardDocumentService.ComputeSha256(data);
                row.Candidate.AdmissionCardIssuedAt = now;
                db.RecruitmentApplicationEvents.Add(CompetitionEvents.New(
                    competition.TenantId, row.Candidate.RecruitmentApplicationId,
                    RecruitmentApplicationEventTypes.AdmissionCardReady, row.Candidate.Id, now));
            }
            issued += batch.Count;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        Audit(competition.TenantId, competitionId, "IssueAdmissionCards", actorUserId, new { issued });
        await db.SaveChangesAsync(ct);
        return (true, null, issued);
    }

    /// <summary>Records attendance (Present/Absent) of seated candidates of a session; not after marking is closed.</summary>
    public async Task<(bool Success, string? Error)> SetAttendanceAsync(
        Guid sessionId, IReadOnlyDictionary<Guid, ExamAttendance> attendance, Guid? actorUserId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return (false, "جلسة الامتحان غير موجودة.");
        if (session.Status is not (ExamSessionStatus.Seated or ExamSessionStatus.Held))
            return (false, "يُسجَّل الحضور بعد التوزيع وقبل إغلاق التصحيح.");
        if (attendance.Values.Any(a => a == ExamAttendance.Unknown || !Enum.IsDefined(a)))
            return (false, "الحضور إما حاضر أو غائب.");

        var ids = attendance.Keys.ToList();
        var candidates = await db.ExamCandidates.Where(c => c.ExamSessionId == sessionId && ids.Contains(c.Id)).ToListAsync(ct);
        if (candidates.Count != ids.Count)
            return (false, "أحد المتقدمين لا يتبع هذه الجلسة.");
        var marked = await db.ExamScores.Where(s => ids.Contains(s.ExamCandidateId)).Select(s => s.ExamCandidateId).Distinct().ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            var value = attendance[candidate.Id];
            if (value == ExamAttendance.Absent && marked.Contains(candidate.Id))
                return (false, $"رُصدت علامة للرقم {candidate.ExamNumber}؛ لا يمكن تسجيله غائباً.");
            candidate.Attendance = value;
        }
        Audit(session.TenantId, session.CompetitionId, "RecordExamAttendance", actorUserId,
            new { sessionId, present = attendance.Count(a => a.Value == ExamAttendance.Present), absent = attendance.Count(a => a.Value == ExamAttendance.Absent) });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Seated → Held once every seated candidate is marked Present or Absent; marking opens.</summary>
    public async Task<(bool Success, string? Error)> MarkHeldAsync(Guid sessionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var session = await db.ExamSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return (false, "جلسة الامتحان غير موجودة.");
        if (session.Status != ExamSessionStatus.Seated)
            return (false, "يمكن تسجيل انعقاد جلسة موزّعة فقط.");
        if (await db.ExamCandidates.AnyAsync(c => c.ExamSessionId == sessionId && c.Attendance == ExamAttendance.Unknown, ct))
            return (false, "سجّل حضور أو غياب كل المتقدمين قبل اعتماد انعقاد الجلسة.");
        session.Status = ExamSessionStatus.Held;
        Audit(session.TenantId, session.CompetitionId, "ExamSessionHeld", actorUserId, new { sessionId });
        await db.SaveChangesAsync(ct);
        return (true, null);
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

/// <summary>HCM-031: explicit application events of the competition services (PLAN-30 event log, no PII).</summary>
public static class CompetitionEvents
{
    public static RecruitmentApplicationEvent New(Guid tenantId, Guid applicationId, string eventType, Guid? relatedId, DateTime nowUtc) =>
        new()
        {
            TenantId = tenantId,
            RecruitmentApplicationId = applicationId,
            EventType = eventType,
            RelatedEntityId = relatedId,
            OccurredAt = nowUtc
        };
}
