using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Hcm026InterviewLifecycleTests
{
    private static DateTime Future => DateTime.UtcNow.AddDays(3);

    [Fact]
    public async Task Past_time_rejected()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;

        var (slot, error) = await CreateService(db).ScheduleAsync(
            app.Id, DateTime.UtcNow.AddMinutes(-5), InterviewMode.InPerson, "قاعة 1", [panel.Id], null);

        Assert.Null(slot);
        Assert.NotNull(error);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.Shortlisted, (await db.RecruitmentApplications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Schedule_moves_shortlisted_to_interview_scheduled_with_panel()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var member = await Hcm026ScoringServiceTests.AddEmployeeAsync(db, "E-PANEL-2");

        var (slot, error) = await CreateService(db).ScheduleAsync(
            app.Id, Future, InterviewMode.Remote, "https://meet.example/x", [panel.Id, member.Id, panel.Id], "ملاحظة");

        Assert.Null(error);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);
        var members = await db.RecruitmentInterviewPanelMembers.Where(m => m.SlotId == slot!.Id).ToListAsync();
        Assert.Equal(2, members.Count);
        Assert.Equal(RecruitmentInterviewPanelMember.ChairRole, members.Single(m => m.EmployeeId == panel.Id).Role);
        Assert.Equal(InterviewMode.Remote, (await db.RecruitmentInterviewSlots.SingleAsync()).Mode);
    }

    [Fact]
    public async Task Empty_or_unknown_panel_rejected()
    {
        var (db, app, _) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var service = CreateService(db);

        Assert.NotNull((await service.ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [], null)).Error);
        Assert.NotNull((await service.ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [Guid.NewGuid()], null)).Error);
        Assert.False(await db.RecruitmentInterviewSlots.AnyAsync());
    }

    [Theory]
    [InlineData(RecruitmentApplicationStatus.Ranked)]
    [InlineData(RecruitmentApplicationStatus.Assessed)]
    [InlineData(RecruitmentApplicationStatus.UnderReview)]
    public async Task Scheduling_outside_allowed_statuses_rejected_and_status_unchanged(RecruitmentApplicationStatus status)
    {
        var (db, app, panel) = await SeedAsync(status);
        await using var _db = db;

        var (slot, error) = await CreateService(db).ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [panel.Id], null);

        Assert.Null(slot);
        Assert.NotNull(error);
        db.ChangeTracker.Clear();
        Assert.Equal(status, (await db.RecruitmentApplications.SingleAsync()).Status);
        Assert.False(await db.RecruitmentInterviewSlots.AnyAsync());
    }

    [Fact]
    public async Task Second_concurrent_scheduled_slot_rejected()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var service = CreateService(db);

        Assert.NotNull((await service.ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [panel.Id], null)).Slot);
        var (second, error) = await service.ScheduleAsync(app.Id, Future.AddDays(1), InterviewMode.InPerson, null, [panel.Id], null);

        Assert.Null(second);
        Assert.NotNull(error);
        Assert.Equal(1, await db.RecruitmentInterviewSlots.CountAsync());
    }

    [Fact]
    public async Task Applicant_cannot_sit_on_panel()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var vacancy = new InternalVacancy { Title = "داخلي", RequisitionId = app.RequisitionId };
        db.InternalVacancies.Add(vacancy);
        var internalApp = new InternalApplication
        {
            InternalVacancyId = vacancy.Id,
            EmployeeId = panel.Id,
            RequisitionId = app.RequisitionId,
            AppliedAt = DateTime.UtcNow
        };
        db.InternalApplications.Add(internalApp);
        app.SourceInternalApplicationId = internalApp.Id;
        await db.SaveChangesAsync();

        var (slot, error) = await CreateService(db).ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [panel.Id], null);

        Assert.Null(slot);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Reschedule_creates_chain_and_respects_max()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var service = CreateService(db, maxReschedules: 1);
        var (first, _) = await service.ScheduleAsync(app.Id, Future, InterviewMode.InPerson, "قاعة", [panel.Id], null);

        Assert.NotNull((await service.RescheduleAsync(first!.Id, Future.AddDays(1), "قصر")).Error);

        var (second, error) = await service.RescheduleAsync(first.Id, Future.AddDays(1), "تعارض في جدول اللجنة");
        Assert.Null(error);
        Assert.Equal(first.Id, second!.RescheduledFromSlotId);

        db.ChangeTracker.Clear();
        var original = await db.RecruitmentInterviewSlots.SingleAsync(s => s.Id == first.Id);
        Assert.Equal(InterviewSlotStatus.Rescheduled, original.Status);
        Assert.Equal("تعارض في جدول اللجنة", original.CancelledReason);
        Assert.Equal(InterviewSlotStatus.Scheduled, (await db.RecruitmentInterviewSlots.SingleAsync(s => s.Id == second.Id)).Status);
        Assert.Single(await db.RecruitmentInterviewPanelMembers.Where(m => m.SlotId == second.Id).ToListAsync());

        var (third, maxError) = await service.RescheduleAsync(second.Id, Future.AddDays(2), "طلب المرشح تأجيلاً");
        Assert.Null(third);
        Assert.Contains("الحد الأقصى", maxError);
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_keeps_application_status()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var service = CreateService(db);
        var (slot, _) = await service.ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [panel.Id], null);

        Assert.False((await service.CancelAsync(slot!.Id, " ")).Success);
        var (success, error) = await service.CancelAsync(slot.Id, "اعتذار اللجنة");

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        Assert.Equal(InterviewSlotStatus.Cancelled, (await db.RecruitmentInterviewSlots.SingleAsync()).Status);
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);
        Assert.False((await service.CancelAsync(slot.Id, "مرة ثانية")).Success);
    }

    [Fact]
    public async Task Completing_future_slot_rejected()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var service = CreateService(db);
        var (slot, _) = await service.ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [panel.Id], null);

        var (success, error) = await service.CompleteAsync(slot!.Id, InterviewOutcome.Attended, null);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.False(await service.HasAttendedInterviewAsync(app.Id));
    }

    [Fact]
    public async Task Attended_completion_is_recorded()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var service = CreateService(db);
        var slot = await ScheduleThenMoveToPastAsync(db, service, app.Id, panel.Id);

        var (success, error) = await service.CompleteAsync(slot.Id, InterviewOutcome.Attended, "أداء جيد");

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentInterviewSlots.SingleAsync();
        Assert.Equal(InterviewSlotStatus.Completed, saved.Status);
        Assert.Equal(InterviewOutcome.Attended, saved.Outcome);
        Assert.NotNull(saved.CompletedAt);
        Assert.Equal("أداء جيد", saved.OutcomeNotes);
        Assert.True(await service.HasAttendedInterviewAsync(app.Id));
    }

    [Fact]
    public async Task NoShow_keeps_application_status()
    {
        var (db, app, panel) = await SeedAsync(RecruitmentApplicationStatus.Shortlisted);
        await using var _db = db;
        var service = CreateService(db);
        var slot = await ScheduleThenMoveToPastAsync(db, service, app.Id, panel.Id);

        var (success, _) = await service.CompleteAsync(slot.Id, InterviewOutcome.NoShow, null);

        Assert.True(success);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);
        Assert.Equal(InterviewOutcome.NoShow, (await db.RecruitmentInterviewSlots.SingleAsync()).Outcome);
        Assert.False(await service.HasAttendedInterviewAsync(app.Id));
    }

    [Fact]
    public async Task Interview_stage_scores_require_attended_interview()
    {
        AssessmentCriteriaItem[] items =
        [
            new("interview", "المقابلة", 50m, 100m, AssessmentStage.Interview),
            new("exp", "الخبرة", 50m, 100m, AssessmentStage.Experience)
        ];
        var (db, app, _) = await Hcm026ScoringServiceTests.SeedAsync(RecruitmentApplicationStatus.Shortlisted, items);
        await using var _db = db;
        var panel = await Hcm026ScoringServiceTests.AddEmployeeAsync(db, "E-GATE");
        var interviews = CreateService(db);
        var scoring = Hcm026ScoringServiceTests.CreateService(db);
        var assessor = Guid.NewGuid();
        var slot = await ScheduleThenMoveToPastAsync(db, interviews, app.Id, panel.Id);

        var rejected = await scoring.SubmitScoresAsync(app.Id, assessor, null,
            Hcm026ScoringServiceTests.Scores(("interview", 70m), ("exp", 90m)), null, true);
        Assert.False(rejected.Success);
        Assert.Equal(RecruitmentScoringService.InterviewGateMessage, rejected.Error);

        // Non-interview items may be scored before the interview, but the assessor stays incomplete.
        Assert.True((await scoring.SubmitScoresAsync(app.Id, assessor, null,
            Hcm026ScoringServiceTests.Scores(("exp", 90m)), null, true)).Success);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);

        Assert.True((await interviews.CompleteAsync(slot.Id, InterviewOutcome.Attended, null)).Success);
        var accepted = await scoring.SubmitScoresAsync(app.Id, assessor, null,
            Hcm026ScoringServiceTests.Scores(("interview", 70m)), null, true);
        Assert.True(accepted.Success, accepted.Error);

        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(RecruitmentApplicationStatus.Assessed, saved.Status);
        Assert.Equal(80.00m, saved.AssessmentScore);
    }

    [Fact]
    public async Task Full_path_shortlist_to_ranked_without_seed_scores()
    {
        AssessmentCriteriaItem[] items =
        [
            new("interview", "المقابلة", 40m, 100m, AssessmentStage.Interview),
            new("exam", "الاختبار", 60m, 50m, AssessmentStage.Exam)
        ];
        var (db, app, _) = await Hcm026ScoringServiceTests.SeedAsync(RecruitmentApplicationStatus.Shortlisted, items);
        await using var _db = db;
        var panel = await Hcm026ScoringServiceTests.AddEmployeeAsync(db, "E-FULL");
        var interviews = CreateService(db);
        var slot = await ScheduleThenMoveToPastAsync(db, interviews, app.Id, panel.Id);
        Assert.True((await interviews.CompleteAsync(slot.Id, InterviewOutcome.Attended, null)).Success);

        var scoring = Hcm026ScoringServiceTests.CreateService(db);
        Assert.True((await scoring.SubmitScoresAsync(app.Id, Guid.NewGuid(), panel.Id,
            Hcm026ScoringServiceTests.Scores(("interview", 80m), ("exam", 40m)), null, true)).Success);

        var (ranked, error) = await new RecruitmentSelectionService(db).AssignRankAsync(app.Id, 1);
        Assert.True(ranked, error);
        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(RecruitmentApplicationStatus.Ranked, saved.Status);
        Assert.Equal(80.00m, saved.AssessmentScore);

        // Interviews never pull a ranked application back.
        Assert.NotNull((await interviews.ScheduleAsync(app.Id, Future, InterviewMode.InPerson, null, [panel.Id], null)).Error);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.Ranked, (await db.RecruitmentApplications.SingleAsync()).Status);
    }

    private static async Task<RecruitmentInterviewSlot> ScheduleThenMoveToPastAsync(
        HRCoreDbContext db,
        RecruitmentInterviewService service,
        Guid applicationId,
        Guid panelEmployeeId)
    {
        var (slot, error) = await service.ScheduleAsync(applicationId, Future, InterviewMode.InPerson, null, [panelEmployeeId], null);
        Assert.True(slot is not null, error);
        var tracked = await db.RecruitmentInterviewSlots.SingleAsync(s => s.Id == slot.Id);
        tracked.ScheduledAt = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();
        return tracked;
    }

    private static RecruitmentInterviewService CreateService(HRCoreDbContext db, int maxReschedules = 1) =>
        new(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions { MaxInterviewReschedules = maxReschedules }));

    private static async Task<(HRCoreDbContext Db, RecruitmentApplication App, Employee Panel)> SeedAsync(
        RecruitmentApplicationStatus status)
    {
        var (db, app, _) = await Hcm026ScoringServiceTests.SeedAsync(status);
        var panel = await Hcm026ScoringServiceTests.AddEmployeeAsync(db, $"E-{status}");
        return (db, app, panel);
    }
}
