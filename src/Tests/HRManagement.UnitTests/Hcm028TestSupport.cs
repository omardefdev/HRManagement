using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-028: SQLite fixtures for the committee service, approval and ranking-gate tests.</summary>
internal static class Hcm028TestSupport
{
    /// <summary>HR secretary: creates and submits the session.</summary>
    public static readonly Guid SecretaryId = Guid.Parse("28280000-0000-4000-8000-000000000001");
    public static readonly Guid Step1ApproverId = Guid.Parse("28280000-0000-4000-8000-00000000000a");
    public static readonly Guid Step2ApproverId = Guid.Parse("28280000-0000-4000-8000-00000000000b");
    public static readonly Guid DirectorId = Guid.Parse("28280000-0000-4000-8000-00000000000c");

    private static int _employeeCounter;

    public static WorkflowActionActor Actor(Guid userId, string role = "hr-core") => new(userId, $"user-{userId:N}"[..12], role);

    public static SelectionCommitteeLifecycleService CreateLifecycle(HRCoreDbContext db) =>
        new(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            Hcm027TestSupport.CreateStarter(db),
            Hcm027TestSupport.Config(),
            new SelectionCommitteeService(db),
            Hcm027TestSupport.CreateActionService(db));

    public static async Task<RecruitmentRequisition> SeedRequisitionAsync(
        HRCoreDbContext db,
        bool requiresCommitteeApproval = true,
        string suffix = "C")
    {
        await WorkflowPackSeeder.SeedAsync(db);
        var req = await Hcm027TestSupport.SeedRequisitionAsync(db, $"H28-{suffix}");
        req.RequiresCommitteeApproval = requiresCommitteeApproval;
        await db.SaveChangesAsync();
        return req;
    }

    /// <summary>Assessed applications; the first gets the highest score, applied earliest first.</summary>
    public static async Task<List<RecruitmentApplication>> SeedCandidatesAsync(
        HRCoreDbContext db,
        RecruitmentRequisition req,
        params decimal[] scores)
    {
        var list = new List<RecruitmentApplication>();
        for (var i = 0; i < scores.Length; i++)
        {
            var app = new RecruitmentApplication
            {
                ApplicantFullName = $"مرشح {i + 1}",
                RequisitionId = req.Id,
                AppliedPosition = req.JobTitle,
                AppliedAt = DateTime.UtcNow.AddDays(-30 + i),
                Status = RecruitmentApplicationStatus.Assessed,
                AssessmentScore = scores[i],
                ConflictOfInterestDeclared = true
            };
            db.RecruitmentApplications.Add(app);
            list.Add(app);
        }

        await db.SaveChangesAsync();
        return list;
    }

    public static async Task<Employee> SeedEmployeeAsync(HRCoreDbContext db, string? name = null)
    {
        var n = Interlocked.Increment(ref _employeeCounter);
        var employee = new Employee
        {
            FullName = name ?? $"عضو لجنة {n}",
            NationalId = $"H28{n:0000000000}"
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    public static async Task<SelectionCommitteeSession> SeedSessionAsync(
        HRCoreDbContext db,
        RecruitmentRequisition req,
        CommitteePurpose purpose = CommitteePurpose.Ranking)
    {
        var (session, error) = await new SelectionCommitteeService(db)
            .CreateSessionAsync(req.Id, DateTime.UtcNow, null, null, purpose);
        Assert.Null(error);
        session!.CreatedBy = SecretaryId;
        await db.SaveChangesAsync();
        return session;
    }

    /// <summary>Adds members (the first is the chair) and returns them in order.</summary>
    public static async Task<List<SelectionCommitteeMember>> AddMembersAsync(
        HRCoreDbContext db,
        SelectionCommitteeSession session,
        int count)
    {
        var service = new SelectionCommitteeService(db);
        for (var i = 0; i < count; i++)
        {
            var employee = await SeedEmployeeAsync(db);
            var (ok, error) = await service.AddMemberAsync(
                session.Id,
                employee.Id,
                i == 0 ? SelectionCommitteeMember.ChairRole : SelectionCommitteeMember.MemberRole,
                hasConflictOfInterest: false,
                SecretaryId);
            Assert.True(ok, error);
        }

        return await db.SelectionCommitteeMembers.AsNoTracking()
            .Where(m => m.SessionId == session.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// A session ready to submit: 3 members + chair, every member agrees on every candidate, and every
    /// candidate is recommended at its suggested rank (the last one as reserve when there are several).
    /// </summary>
    public static async Task<(SelectionCommitteeSession Session, List<SelectionCommitteeMember> Members)> SeedReadySessionAsync(
        HRCoreDbContext db,
        RecruitmentRequisition req,
        IReadOnlyList<RecruitmentApplication> candidates,
        CommitteePurpose purpose = CommitteePurpose.Ranking)
    {
        var session = await SeedSessionAsync(db, req, purpose);
        var members = await AddMembersAsync(db, session, 3);
        var service = new SelectionCommitteeService(db);
        var ranks = SelectionCommitteeRules.SuggestedRanks(candidates);

        foreach (var app in candidates)
        {
            foreach (var member in members)
            {
                var (voted, voteError) = await service.RecordVoteAsync(
                    session.Id, member.Id, app.Id, CommitteeVoteValue.Agree, null, SecretaryId);
                Assert.True(voted, voteError);
            }

            var recommendation = candidates.Count > 1 && ranks[app.Id] == candidates.Count
                ? CommitteeRecommendation.Reserve
                : CommitteeRecommendation.Recommend;
            var (decided, decisionError) = await service.UpsertDecisionAsync(
                session.Id, app.Id, recommendation, ranks[app.Id], "توصية اللجنة", SecretaryId);
            Assert.True(decided, decisionError);
        }

        return (session, members);
    }

    /// <summary>Submits and walks the 3 committee steps with distinct, non-member approvers.</summary>
    public static async Task ApproveAllStepsAsync(HRCoreDbContext db, Guid sessionId)
    {
        var lifecycle = CreateLifecycle(db);
        var (submitted, submitError) = await lifecycle.SubmitForApprovalAsync(sessionId, SecretaryId, "secretary");
        Assert.True(submitted, submitError);

        foreach (var (approver, role) in new[]
                 {
                     (Step1ApproverId, "hr-core"),
                     (Step2ApproverId, "hr-core"),
                     (DirectorId, "hr-management-approver")
                 })
        {
            var (ok, error) = await CreateLifecycle(db).ApproveStepAsync(sessionId, Actor(approver, role), Guid.NewGuid(), null);
            Assert.True(ok, error);
        }

        db.ChangeTracker.Clear();
    }
}
