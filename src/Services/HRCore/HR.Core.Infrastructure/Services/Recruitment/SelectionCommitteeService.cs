using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class SelectionCommitteeService(HRCoreDbContext db)
{
    public const int MaxVoteCommentLength = 1000;
    public const int MaxRationaleLength = 2000;

    public const string SessionNotFoundMessage = "جلسة اللجنة غير موجودة.";
    public const string DuplicateMemberMessage = "الموظف عضو في هذه الجلسة مسبقاً.";
    public const string MemberNotFoundMessage = "العضو غير موجود في هذه الجلسة.";
    public const string NotCandidateMessage = "المرشح ليس من المرشحين المُقيَّمين على طلب الشاغر لهذه الجلسة.";

    public async Task<(SelectionCommitteeSession? Session, string? Error)> CreateSessionAsync(
        Guid requisitionId,
        DateTime committeeDate,
        string? findings,
        string? recommendation,
        CommitteePurpose purpose = CommitteePurpose.Ranking,
        CancellationToken ct = default)
    {
        if (!await db.RecruitmentRequisitions.AnyAsync(r => r.Id == requisitionId, ct))
            return (null, "طلب الشاغر غير موجود.");
        // HCM-031 (J4): a requisition attached to a competition is ranked by the competition results approval.
        if (await db.RecruitmentRequisitions.AnyAsync(r => r.Id == requisitionId && r.CompetitionLineId != null, ct))
            return (null, RecruitmentSelectionService.CompetitionRankingMessage);

        var session = new SelectionCommitteeSession
        {
            RequisitionId = requisitionId,
            CommitteeDate = committeeDate,
            Findings = findings,
            Recommendation = recommendation,
            Purpose = purpose
        };
        db.SelectionCommitteeSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return (session, null);
    }

    /// <summary>HCM-028: an existing employee, once per session, only while the session is unlocked.</summary>
    public async Task<(bool Success, string? Error)> AddMemberAsync(
        Guid sessionId,
        Guid employeeId,
        string? role,
        bool hasConflictOfInterest,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var (session, error) = await LoadUnlockedSessionAsync(sessionId, ct);
        if (session is null) return (false, error);

        if (employeeId == Guid.Empty || !await db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            return (false, "الموظف غير موجود.");

        if (session.Members.Any(m => m.EmployeeId == employeeId))
            return (false, DuplicateMemberMessage);

        var asChair = SelectionCommitteeRules.IsChairRole(role) && !hasConflictOfInterest;
        var member = new SelectionCommitteeMember
        {
            SessionId = sessionId,
            EmployeeId = employeeId,
            Role = SelectionCommitteeMember.MemberRole,
            HasConflictOfInterest = hasConflictOfInterest,
            Recused = hasConflictOfInterest
        };
        db.SelectionCommitteeMembers.Add(member);
        if (asChair)
            AssignChair(session, member);

        Audit(session.Id, "CommitteeMemberAdded", actorUserId, new
        {
            employeeId,
            role = member.Role,
            recused = member.Recused
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RemoveMemberAsync(
        Guid sessionId,
        Guid memberId,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var (session, error) = await LoadUnlockedSessionAsync(sessionId, ct);
        if (session is null) return (false, error);

        var member = session.Members.FirstOrDefault(m => m.Id == memberId);
        if (member is null) return (false, MemberNotFoundMessage);

        if (session.ChairEmployeeId == member.EmployeeId)
            session.ChairEmployeeId = null;

        // Votes and per-candidate declarations cascade with the member.
        db.SelectionCommitteeMembers.Remove(member);
        Audit(session.Id, "CommitteeMemberRemoved", actorUserId, new { employeeId = member.EmployeeId });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SetChairAsync(
        Guid sessionId,
        Guid memberId,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var (session, error) = await LoadUnlockedSessionAsync(sessionId, ct);
        if (session is null) return (false, error);

        var member = session.Members.FirstOrDefault(m => m.Id == memberId);
        if (member is null) return (false, MemberNotFoundMessage);
        if (member.Recused) return (false, "لا يجوز تعيين عضو منسحب رئيساً للجنة.");

        AssignChair(session, member);
        Audit(session.Id, "CommitteeChairSet", actorUserId, new { employeeId = member.EmployeeId });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>HCM-028: per-candidate conflict; a conflicted member's vote on that candidate is withdrawn.</summary>
    public async Task<(bool Success, string? Error)> RecordMemberCoiAsync(
        Guid sessionId,
        Guid memberId,
        Guid applicationId,
        bool hasConflict,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var (session, error) = await LoadUnlockedSessionAsync(sessionId, ct);
        if (session is null) return (false, error);

        var member = session.Members.FirstOrDefault(m => m.Id == memberId);
        if (member is null) return (false, MemberNotFoundMessage);
        if (!await IsCandidateAsync(session.RequisitionId, applicationId, ct))
            return (false, NotCandidateMessage);

        var coi = await db.SelectionCommitteeMemberCois
            .FirstOrDefaultAsync(c => c.MemberId == memberId && c.ApplicationId == applicationId, ct);
        if (coi is null)
        {
            coi = new SelectionCommitteeMemberCoi { MemberId = memberId, ApplicationId = applicationId };
            db.SelectionCommitteeMemberCois.Add(coi);
        }

        coi.HasConflict = hasConflict;
        coi.DeclaredAt = DateTime.UtcNow;
        coi.RecordedBy = actorUserId;

        if (hasConflict)
        {
            var vote = await db.SelectionCommitteeVotes
                .FirstOrDefaultAsync(v => v.MemberId == memberId && v.ApplicationId == applicationId, ct);
            if (vote is not null)
                db.SelectionCommitteeVotes.Remove(vote);
        }

        Audit(session.Id, "CommitteeMemberCoi", actorUserId, new
        {
            employeeId = member.EmployeeId,
            applicationId,
            hasConflict
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>HCM-028: recorded by the HR secretary on behalf of the member (<c>RecordedBy</c> = actor).</summary>
    public async Task<(bool Success, string? Error)> RecordVoteAsync(
        Guid sessionId,
        Guid memberId,
        Guid applicationId,
        CommitteeVoteValue vote,
        string? comment,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var (session, error) = await LoadUnlockedSessionAsync(sessionId, ct);
        if (session is null) return (false, error);

        var member = session.Members.FirstOrDefault(m => m.Id == memberId);
        if (member is null) return (false, MemberNotFoundMessage);
        if (!Enum.IsDefined(vote)) return (false, "قيمة التصويت غير صالحة.");
        if (!await IsCandidateAsync(session.RequisitionId, applicationId, ct))
            return (false, NotCandidateMessage);

        var coi = await db.SelectionCommitteeMemberCois.AsNoTracking()
            .Where(c => c.MemberId == memberId && c.ApplicationId == applicationId)
            .ToListAsync(ct);
        var voterError = SelectionCommitteeRules.ValidateVoter(member, coi, applicationId);
        if (voterError is not null) return (false, voterError);

        var trimmed = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (trimmed is { Length: > MaxVoteCommentLength })
            return (false, $"التعليق يجب ألا يتجاوز {MaxVoteCommentLength} حرف.");

        var row = await db.SelectionCommitteeVotes
            .FirstOrDefaultAsync(v => v.MemberId == memberId && v.ApplicationId == applicationId, ct);
        if (row is null)
        {
            row = new SelectionCommitteeVote { SessionId = sessionId, MemberId = memberId, ApplicationId = applicationId };
            db.SelectionCommitteeVotes.Add(row);
        }

        row.Vote = vote;
        row.Comment = trimmed;
        row.RecordedBy = actorUserId;
        row.RecordedAt = DateTime.UtcNow;

        Audit(session.Id, "CommitteeVote", actorUserId, new
        {
            employeeId = member.EmployeeId,
            applicationId,
            vote = vote.ToString()
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpsertDecisionAsync(
        Guid sessionId,
        Guid applicationId,
        CommitteeRecommendation recommendation,
        int? proposedRank,
        string? rationale,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var (session, error) = await LoadUnlockedSessionAsync(sessionId, ct);
        if (session is null) return (false, error);

        if (!Enum.IsDefined(recommendation)) return (false, "قيمة التوصية غير صالحة.");
        if (!await IsCandidateAsync(session.RequisitionId, applicationId, ct))
            return (false, NotCandidateMessage);
        if (recommendation == CommitteeRecommendation.Reject && proposedRank is not null)
            return (false, "التوصية بالرفض لا تحمل ترتيباً مقترحاً.");
        if (proposedRank is < 1)
            return (false, "الترتيب المقترح يجب أن يكون موجباً.");

        var trimmed = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim();
        if (trimmed is { Length: > MaxRationaleLength })
            return (false, $"التعليل يجب ألا يتجاوز {MaxRationaleLength} حرف.");

        var decision = await db.SelectionCommitteeDecisions
            .FirstOrDefaultAsync(d => d.SessionId == sessionId && d.ApplicationId == applicationId, ct);
        if (decision is null)
        {
            decision = new SelectionCommitteeDecision { SessionId = sessionId, ApplicationId = applicationId };
            db.SelectionCommitteeDecisions.Add(decision);
        }

        decision.Recommendation = recommendation;
        decision.ProposedRank = proposedRank;
        decision.Rationale = trimmed;

        Audit(session.Id, "CommitteeDecision", actorUserId, new
        {
            applicationId,
            recommendation = recommendation.ToString(),
            proposedRank
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<SelectionCommitteeSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.SelectionCommitteeSessions
            .Include(s => s.Members)
                .ThenInclude(m => m.Employee)
            .Include(s => s.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task<SelectionCommitteeSession?> GetLatestForRequisitionAsync(
        Guid requisitionId,
        CancellationToken ct = default) =>
        await db.SelectionCommitteeSessions
            .Include(s => s.Members)
                .ThenInclude(m => m.Employee)
            .Where(s => s.RequisitionId == requisitionId)
            .OrderByDescending(s => s.CommitteeDate)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// HCM-028: everything the committee page, submit and the minutes need. Candidates are the requisition's
    /// Assessed/Ranked applications while unlocked, and the applications the locked decisions cover afterwards.
    /// </summary>
    public async Task<SelectionCommitteeWorkspace?> GetWorkspaceAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.SelectionCommitteeSessions.AsNoTracking()
            .Include(s => s.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;

        var members = await db.SelectionCommitteeMembers.AsNoTracking()
            .Include(m => m.Employee)
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.Role == SelectionCommitteeMember.ChairRole)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync(ct);
        var memberIds = members.Select(m => m.Id).ToList();
        var coi = await db.SelectionCommitteeMemberCois.AsNoTracking()
            .Where(c => memberIds.Contains(c.MemberId))
            .ToListAsync(ct);
        var votes = await db.SelectionCommitteeVotes.AsNoTracking()
            .Where(v => v.SessionId == sessionId)
            .ToListAsync(ct);
        var decisions = await db.SelectionCommitteeDecisions.AsNoTracking()
            .Where(d => d.SessionId == sessionId)
            .ToListAsync(ct);

        List<RecruitmentApplication> candidates;
        if (session.IsLocked)
        {
            var decided = decisions.Select(d => d.ApplicationId).ToList();
            candidates = await db.RecruitmentApplications.AsNoTracking()
                .Where(a => decided.Contains(a.Id))
                .ToListAsync(ct);
        }
        else
        {
            candidates = await GetCandidatesQuery(session.RequisitionId).AsNoTracking().ToListAsync(ct);
        }

        return new SelectionCommitteeWorkspace(
            session,
            members,
            coi,
            votes,
            decisions,
            SelectionCommitteeRules.SuggestRanking(candidates));
    }

    /// <summary>Assessed/Ranked applications on the requisition.</summary>
    public async Task<IReadOnlyList<RecruitmentApplication>> GetCandidatesAsync(
        Guid requisitionId,
        CancellationToken ct = default) =>
        SelectionCommitteeRules.SuggestRanking(
            await GetCandidatesQuery(requisitionId).AsNoTracking().ToListAsync(ct));

    /// <summary>
    /// HCM-028 (session 2): employee ids that sit on any committee session of the requisition (recused members
    /// included) — an independent appeal reviewer must not be one of them.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetMemberEmployeeIdsAsync(Guid requisitionId, CancellationToken ct = default) =>
        await db.SelectionCommitteeMembers.AsNoTracking()
            .Where(m => m.Session != null && m.Session.RequisitionId == requisitionId)
            .Select(m => m.EmployeeId)
            .Distinct()
            .ToListAsync(ct);

    public Task<bool> IsCommitteeMemberAsync(Guid requisitionId, Guid employeeId, CancellationToken ct = default) =>
        db.SelectionCommitteeMembers.AnyAsync(
            m => m.EmployeeId == employeeId && m.Session != null && m.Session.RequisitionId == requisitionId,
            ct);

    private IQueryable<RecruitmentApplication> GetCandidatesQuery(Guid requisitionId) =>
        db.RecruitmentApplications.Where(a =>
            a.RequisitionId == requisitionId
            && (a.Status == RecruitmentApplicationStatus.Assessed || a.Status == RecruitmentApplicationStatus.Ranked));

    private Task<bool> IsCandidateAsync(Guid requisitionId, Guid applicationId, CancellationToken ct) =>
        GetCandidatesQuery(requisitionId).AnyAsync(a => a.Id == applicationId, ct);

    private async Task<(SelectionCommitteeSession? Session, string? Error)> LoadUnlockedSessionAsync(
        Guid sessionId,
        CancellationToken ct)
    {
        var session = await db.SelectionCommitteeSessions
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return (null, SessionNotFoundMessage);
        if (session.IsLocked || session.ApprovedAt is not null)
            return (null, SelectionCommitteeRules.LockedMessage);
        return (session, null);
    }

    private static void AssignChair(SelectionCommitteeSession session, SelectionCommitteeMember chair)
    {
        foreach (var other in session.Members.Where(m => m.Id != chair.Id && SelectionCommitteeRules.IsChairRole(m.Role)))
            other.Role = SelectionCommitteeMember.MemberRole;
        chair.Role = SelectionCommitteeMember.ChairRole;
        session.ChairEmployeeId = chair.EmployeeId;
    }

    private void Audit(Guid sessionId, string action, Guid? actorUserId, object changes) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(SelectionCommitteeSession),
            EntityId = sessionId,
            Action = action,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(changes)
        });
}
