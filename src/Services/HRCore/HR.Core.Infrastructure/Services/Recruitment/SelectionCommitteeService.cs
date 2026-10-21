using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class SelectionCommitteeService(HRCoreDbContext db)
{
    public async Task<(SelectionCommitteeSession? Session, string? Error)> CreateSessionAsync(
        Guid requisitionId,
        DateTime committeeDate,
        string? findings,
        string? recommendation,
        CancellationToken ct = default)
    {
        if (!await db.RecruitmentRequisitions.AnyAsync(r => r.Id == requisitionId, ct))
            return (null, "طلب الشاغر غير موجود.");

        var session = new SelectionCommitteeSession
        {
            RequisitionId = requisitionId,
            CommitteeDate = committeeDate,
            Findings = findings,
            Recommendation = recommendation
        };
        db.SelectionCommitteeSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return (session, null);
    }

    public async Task<(bool Success, string? Error)> AddMemberAsync(
        Guid sessionId,
        Guid employeeId,
        string role,
        bool hasConflictOfInterest,
        CancellationToken ct = default)
    {
        var session = await db.SelectionCommitteeSessions.FindAsync([sessionId], ct);
        if (session is null) return (false, "جلسة اللجنة غير موجودة.");

        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            return (false, "الموظف غير موجود.");

        db.SelectionCommitteeMembers.Add(new SelectionCommitteeMember
        {
            SessionId = sessionId,
            EmployeeId = employeeId,
            Role = role,
            HasConflictOfInterest = hasConflictOfInterest,
            Recused = hasConflictOfInterest
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<SelectionCommitteeSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.SelectionCommitteeSessions
            .Include(s => s.Members)
                .ThenInclude(m => m.Employee)
            .Include(s => s.Requisition)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task<SelectionCommitteeSession?> GetLatestForRequisitionAsync(
        Guid requisitionId,
        CancellationToken ct = default) =>
        await db.SelectionCommitteeSessions
            .Include(s => s.Members)
                .ThenInclude(m => m.Employee)
            .Where(s => s.RequisitionId == requisitionId)
            .OrderByDescending(s => s.CommitteeDate)
            .FirstOrDefaultAsync(ct);
}
