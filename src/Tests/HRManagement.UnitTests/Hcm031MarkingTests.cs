using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31, P3, AC-HCM-31c): anonymous blind double marking with reconciliation, locking and absentees.</summary>
public sealed class Hcm031MarkingTests
{
    private sealed record Fixture(
        RecruitmentCompetition Competition,
        ExamSession Session,
        IReadOnlyList<ExamCandidate> Present,
        ExamCandidate Absent,
        ExamMarkerActor First,
        ExamMarkerActor Second,
        ExamMarkerActor Reconciler);

    private static async Task<Fixture> SeedHeldAsync(HRCoreDbContext db, int applicants = 4)
    {
        var (competition, sessions) = await SeedExamReadyAsync(db, applicants, applicants);
        var seating = CreateSeating(db);
        Assert.True((await seating.SeatAsync(competition.Id, Creator)).Success);
        var session = sessions[0];
        var candidates = await db.ExamCandidates.Where(c => c.ExamSessionId == session.Id).OrderBy(c => c.SeatNumber).ToListAsync();
        var absent = candidates[^1];
        var attendance = candidates.ToDictionary(c => c.Id, c => c.Id == absent.Id ? ExamAttendance.Absent : ExamAttendance.Present);
        Assert.True((await seating.SetAttendanceAsync(session.Id, attendance, Creator)).Success);
        Assert.True((await seating.MarkHeldAsync(session.Id, Creator)).Success);

        var actors = new List<ExamMarkerActor>();
        foreach (var role in new[] { ExamMarkerRole.First, ExamMarkerRole.Second, ExamMarkerRole.Reconciler })
        {
            var employee = await SeedEmployeeAsync(db, $"مصحح {role}");
            var (ok, error) = await seating.AssignMarkerAsync(session.Id, employee.Id, role, Creator);
            Assert.True(ok, error);
            actors.Add(new ExamMarkerActor(Guid.NewGuid(), employee.Id, HoldsHrReadAccess: false));
        }
        return new Fixture(competition, session, candidates.Where(c => c.Id != absent.Id).ToList(), absent, actors[0], actors[1], actors[2]);
    }

    [Fact]
    public async Task The_second_marker_never_sees_the_first_mark_and_the_reconciler_sees_both_after_both_AC_HCM_31c()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var marking = new ExamMarkingService(db);
        var paper = fx.Present[0];

        Assert.True((await marking.SubmitAsync(paper.Id, 80m, null, fx.First)).Success);
        var seenBySecond = await marking.GetPaperAsync(paper.Id, fx.Second);
        Assert.NotNull(seenBySecond);
        Assert.Null(seenBySecond.FirstMark);
        Assert.Null(seenBySecond.SecondMark);
        Assert.Null(seenBySecond.OwnMark);

        // Before the second mark, the reconciler has no paper.
        Assert.Empty((await marking.GetPapersAsync(fx.Session.Id, fx.Reconciler)).Papers);

        Assert.True((await marking.SubmitAsync(paper.Id, 60m, null, fx.Second)).Success);
        var seenByFirst = await marking.GetPaperAsync(paper.Id, fx.First);
        Assert.Null(seenByFirst!.SecondMark);
        Assert.Equal(80m, seenByFirst.OwnMark);

        // |80 − 60| = 20 > threshold 10 → reconciliation queue, with both marks visible to the reconciler only.
        var reconciler = Assert.Single((await marking.GetPapersAsync(fx.Session.Id, fx.Reconciler)).Papers);
        Assert.Equal(paper.Id, reconciler.ExamCandidateId);
        Assert.Equal(80m, reconciler.FirstMark);
        Assert.Equal(60m, reconciler.SecondMark);
        Assert.True((await marking.SubmitAsync(paper.Id, 75m, null, fx.Reconciler)).Success);
    }

    [Fact]
    public async Task Within_the_threshold_no_reconciler_is_needed_and_the_mark_is_the_average()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var marking = new ExamMarkingService(db);
        var paper = fx.Present[0];

        Assert.True((await marking.SubmitAsync(paper.Id, 70m, null, fx.First)).Success);
        Assert.True((await marking.SubmitAsync(paper.Id, 75m, null, fx.Second)).Success);

        Assert.Empty((await marking.GetPapersAsync(fx.Session.Id, fx.Reconciler)).Papers);
        Assert.Equal(ExamMarkingRules.ReconcilerNotNeededMessage, (await marking.SubmitAsync(paper.Id, 72m, null, fx.Reconciler)).Error);
        Assert.Equal(72.5m, ExamMarkingRules.ComputeMark(new PaperMarks(70m, 75m, null), 10m, CompetitionReconcileRule.Average));
    }

    [Theory]
    [InlineData(CompetitionReconcileRule.Average, 71.67)]
    [InlineData(CompetitionReconcileRule.ReconcilerDecides, 75)]
    [InlineData(CompetitionReconcileRule.AverageOfClosestTwo, 77.5)]
    public void Each_reconcile_rule_forms_the_mark_of_a_reconciled_paper(CompetitionReconcileRule rule, double expected)
    {
        var marks = new PaperMarks(80m, 60m, 75m);
        Assert.Equal(PaperMarkingState.Complete, ExamMarkingRules.State(marks, 10m));
        Assert.Equal((decimal)expected, ExamMarkingRules.ComputeMark(marks, 10m, rule));
        Assert.Equal(PaperMarkingState.AwaitingReconciler, ExamMarkingRules.State(marks with { Reconciler = null }, 10m));
        Assert.Null(ExamMarkingRules.ComputeMark(marks with { Reconciler = null }, 10m, rule));
        Assert.Equal(PaperMarkingState.AwaitingMarks, ExamMarkingRules.State(new PaperMarks(80m, null, null), 10m));
        Assert.Equal(55m, ExamMarkingRules.ComputeMark(marks with { ObjectionCorrection = 55m }, 10m, rule));
    }

    [Fact]
    public async Task Markers_on_a_session_are_distinct_people_per_role()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var seating = CreateSeating(db);

        var sameEmployee = await seating.AssignMarkerAsync(fx.Session.Id, fx.First.EmployeeId!.Value, ExamMarkerRole.Reconciler, Creator);
        Assert.False(sameEmployee.Success);
        var other = await SeedEmployeeAsync(db, "مصحح إضافي");
        var sameRole = await seating.AssignMarkerAsync(fx.Session.Id, other.Id, ExamMarkerRole.First, Creator);
        Assert.False(sameRole.Success);
        Assert.Equal(3, await db.ExamMarkerAssignments.CountAsync());
    }

    [Fact]
    public async Task A_marker_only_marks_his_sessions_and_absentees_are_never_marked()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var marking = new ExamMarkingService(db);
        var stranger = new ExamMarkerActor(Guid.NewGuid(), (await SeedEmployeeAsync(db, "غير مكلف")).Id, false);

        Assert.Equal(ExamMarkingRules.NotAssignedMessage, (await marking.SubmitAsync(fx.Present[0].Id, 50m, null, stranger)).Error);
        Assert.Empty(await marking.GetSessionsAsync(stranger));
        Assert.Equal(ExamMarkingRules.AbsentMessage, (await marking.SubmitAsync(fx.Absent.Id, 50m, null, fx.First)).Error);
        Assert.DoesNotContain((await marking.GetPapersAsync(fx.Session.Id, fx.First)).Papers, p => p.ExamCandidateId == fx.Absent.Id);
        Assert.NotNull((await marking.SubmitAsync(fx.Present[0].Id, 101m, null, fx.First)).Error);
    }

    [Fact]
    public async Task The_token_is_bound_on_first_visit_and_another_token_of_the_same_employee_is_refused()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var marking = new ExamMarkingService(db);
        Assert.Single(await marking.GetSessionsAsync(fx.First));

        var impostor = fx.First with { UserId = Guid.NewGuid() };
        Assert.Empty(await marking.GetSessionsAsync(impostor));
        Assert.Equal(ExamMarkingRules.NotAssignedMessage, (await marking.SubmitAsync(fx.Present[0].Id, 50m, null, impostor)).Error);
    }

    [Fact]
    public async Task Closing_marking_needs_every_present_paper_complete_then_locks_the_marks()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var marking = new ExamMarkingService(db);
        foreach (var paper in fx.Present)
        {
            Assert.True((await marking.SubmitAsync(paper.Id, 70m, null, fx.First)).Success);
            Assert.True((await marking.SubmitAsync(paper.Id, 70m, null, fx.Second)).Success);
        }
        var reconciled = fx.Present[0];
        Assert.True((await marking.SubmitAsync(reconciled.Id, 90m, null, fx.Second)).Success); // correction before close
        var (incomplete, error) = await marking.CloseMarkingAsync(fx.Session.Id, Creator);
        Assert.False(incomplete);
        Assert.Contains(reconciled.ExamNumber, error, StringComparison.Ordinal);
        Assert.True((await marking.SubmitAsync(reconciled.Id, 85m, null, fx.Reconciler)).Success);

        Assert.True((await marking.CloseMarkingAsync(fx.Session.Id, Creator)).Success);

        var marks = await db.ExamCandidates.AsNoTracking().Where(c => c.ExamSessionId == fx.Session.Id).ToDictionaryAsync(c => c.Id, c => c.FinalMark);
        Assert.Equal(85m, marks[reconciled.Id]); // ReconcilerDecides (test legal inputs)
        Assert.Equal(70m, marks[fx.Present[1].Id]);
        Assert.Null(marks[fx.Absent.Id]);
        Assert.Equal(ExamMarkingRules.SessionLockedMessage, (await marking.SubmitAsync(fx.Present[1].Id, 10m, null, fx.First)).Error);
        Assert.True((await marking.GetPapersAsync(fx.Session.Id, fx.First)).Papers.All(p => p.Locked));
    }

    [Fact]
    public async Task Every_submission_is_audited_without_the_applicant_identity()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var marking = new ExamMarkingService(db);
        Assert.True((await marking.SubmitAsync(fx.Present[0].Id, 66m, "خط واضح", fx.First)).Success);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == ExamMarkingService.AuditAction);
        Assert.Equal(fx.First.UserId, audit.UserId);
        Assert.Equal(fx.Present[0].Id, audit.EntityId);
        Assert.Contains("\"round\":\"First\"", audit.ChangedFields, StringComparison.Ordinal);
        var application = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == fx.Present[0].RecruitmentApplicationId);
        Assert.DoesNotContain(application.ApplicantFullName, audit.ChangedFields, StringComparison.Ordinal);
        Assert.DoesNotContain(application.Id.ToString(), audit.ChangedFields, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_marker_who_also_holds_HR_read_roles_is_flagged_and_audited()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedHeldAsync(db);
        var marking = new ExamMarkingService(db);

        await marking.GetSessionsAsync(fx.First with { HoldsHrReadAccess = true });

        Assert.True((await db.ExamMarkerAssignments.AsNoTracking().SingleAsync(a => a.MarkerEmployeeId == fx.First.EmployeeId)).HoldsHrReadAccess);
        Assert.Single(await db.AuditLogs.AsNoTracking().Where(a => a.Action == "ExamMarkerHoldsHrReadAccess").ToListAsync());
    }
}
