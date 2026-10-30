using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Web.Controllers;
using HR.Core.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P2): competition intake — ordered line preferences, duplicates, closed intake, per-line eligibility,
/// the intake-close job, the public status of competition applications, and the RequisitionId-null fixes (legacy filter,
/// legacy hire, retention trigger, DSAR).
/// </summary>
public sealed class Hcm031IntakeTests
{
    private static CompetitionIntakeService CreateIntake(HRCoreDbContext db) =>
        new(db,
            new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new RecruitmentSelectionService(db),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

    private static CompetitionApplyViewModel ApplyModel(RecruitmentCompetition competition, string nationalId, params Guid?[] preferences)
    {
        var vm = new CompetitionApplyViewModel
        {
            CompetitionId = competition.Id,
            PublicJobPostingId = competition.PublicJobPostingId!.Value,
            FullName = "متقدم للمسابقة",
            NationalId = nationalId,
            Phone = "0999000000",
            Email = "c@test.sy",
            PrivacyConsent = true,
            Preferences = preferences.ToList()
        };
        return vm;
    }

    // ---- preferences --------------------------------------------------------------------------------------------------

    [Fact]
    public void Preferences_need_a_first_choice_distinct_lines_of_the_competition_and_at_most_the_maximum()
    {
        var competition = new RecruitmentCompetition { MaxLinesPerCandidate = 2 };
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var lines = new[] { a, b, c };

        Assert.Equal(CompetitionIntakeService.PreferencesRequiredMessage,
            CompetitionIntakeService.ValidatePreferences(competition, lines, [null, null], out _));
        Assert.NotNull(CompetitionIntakeService.ValidatePreferences(competition, lines, [a, b, c], out _));
        Assert.NotNull(CompetitionIntakeService.ValidatePreferences(competition, lines, [a, a], out _));
        Assert.NotNull(CompetitionIntakeService.ValidatePreferences(competition, lines, [Guid.NewGuid()], out _));
        Assert.Null(CompetitionIntakeService.ValidatePreferences(competition, lines, [null, b, a], out var ordered));
        Assert.Equal([b, a], ordered);
    }

    // ---- public apply -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Public_apply_creates_one_competition_application_with_ordered_preferences_and_no_requisition()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 3, 2);
        var lines = competition.Lines.OrderBy(l => l.LineNo).ToList();
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        var result = await controller.ApplyCompetition(ApplyModel(competition, "0102030405", lines[1].Id, lines[0].Id));

        Assert.Equal(nameof(PublicCareersController.Submitted), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var application = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(competition.Id, application.CompetitionId);
        Assert.Null(application.RequisitionId);
        Assert.Equal(RecruitmentIntakeChannel.Public, application.IntakeChannel);
        Assert.Equal(RecruitmentApplicationStatus.Submitted, application.Status);
        var preferences = await db.CompetitionPreferences.AsNoTracking().OrderBy(p => p.Order).ToListAsync();
        Assert.Equal([lines[1].Id, lines[0].Id], preferences.Select(p => p.CompetitionLineId));
        Assert.Equal([1, 2], preferences.Select(p => p.Order));
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        Assert.Null(citizen.RequisitionId);
        Assert.Equal(application.Id, citizen.PromotedRecruitmentApplicationId);
        Assert.Contains(await db.RecruitmentApplicationEvents.AsNoTracking().ToListAsync(),
            e => e.RecruitmentApplicationId == application.Id && e.EventType == RecruitmentApplicationEventTypes.Received);
    }

    [Fact]
    public async Task A_second_application_of_the_same_person_gets_the_Arabic_duplicate_message_without_an_exception()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        var line = competition.Lines.Single();
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);
        await controller.ApplyCompetition(ApplyModel(competition, "0102030405", line.Id));

        var second = await controller.ApplyCompetition(ApplyModel(competition, "0102030405", line.Id));

        Assert.Equal(nameof(PublicCareersController.Competition), Assert.IsType<RedirectToActionResult>(second).ActionName);
        Assert.Equal(CompetitionIntakeService.DuplicateMessage, controller.TempData["Error"]);
        Assert.Single(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task Apply_is_refused_after_the_intake_closed()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        Assert.True((await CreateLifecycle(db).CloseIntakeAsync(competition.Id, Creator)).Success);
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        await controller.ApplyCompetition(ApplyModel(competition, "0102030405", competition.Lines.Single().Id));

        Assert.Equal(CompetitionIntakeService.IntakeClosedMessage, controller.TempData["Error"]);
        Assert.Empty(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task Missing_preferences_redisplay_the_form_with_an_error()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        var result = await controller.ApplyCompetition(ApplyModel(competition, "0102030405"));

        Assert.Equal("Competition", Assert.IsType<ViewResult>(result).ViewName);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await db.RecruitmentApplications.ToListAsync());
    }

    [Fact]
    public async Task The_competition_posting_redirects_to_the_competition_page_and_never_takes_a_legacy_application()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        var details = await controller.Details(competition.PublicJobPostingId!.Value);
        Assert.Equal(nameof(PublicCareersController.Competition), Assert.IsType<RedirectToActionResult>(details).ActionName);

        var legacyApply = await controller.Apply(Hcm030TestSupport.ApplyModel(competition.PublicJobPostingId.Value));
        Assert.Equal(nameof(PublicCareersController.Competition), Assert.IsType<RedirectToActionResult>(legacyApply).ActionName);
        Assert.Empty(await db.CitizenApplications.ToListAsync());
    }

    [Fact]
    public async Task Eligibility_flags_are_evaluated_per_preferred_line_and_never_reject()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1, 1);
        var lines = competition.Lines.OrderBy(l => l.LineNo).ToList();
        var strict = await db.RecruitmentRequisitions.FirstAsync(r => r.CompetitionLineId == lines[1].Id);
        strict.MinYearsOfExperience = 10m;
        await db.SaveChangesAsync();
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);
        var vm = ApplyModel(competition, "0102030405", lines[0].Id, lines[1].Id);
        vm.YearsOfExperience = 2m;

        await controller.ApplyCompetition(vm);

        var application = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentApplicationStatus.Submitted, application.Status);
        Assert.Contains(RecruitmentEligibilityRules.BelowMinExperience, RecruitmentEligibilityRules.Parse(application.EligibilityFlags));
        var preferences = await db.CompetitionPreferences.AsNoTracking().OrderBy(p => p.Order).ToListAsync();
        Assert.DoesNotContain(RecruitmentEligibilityRules.BelowMinExperience, RecruitmentEligibilityRules.Parse(preferences[0].EligibilityFlags));
        Assert.Contains(RecruitmentEligibilityRules.BelowMinExperience, RecruitmentEligibilityRules.Parse(preferences[1].EligibilityFlags));
    }

    // ---- internal apply -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Internal_apply_creates_a_competition_application_once_per_employee()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 1);
        competition.PublishingChannel = RecruitmentPublishingChannel.Both;
        await db.SaveChangesAsync();
        await ApproveAsync(db, lifecycle, competition.Id);
        Assert.True((await lifecycle.AnnounceAsync(competition.Id, Creator)).Success);
        var line = await db.CompetitionLines.SingleAsync();
        var employee = new Employee
        {
            FullName = "موظف مرشح",
            NationalId = "09080706050",
            NationalIdHash = HRManagement.SharedKernel.PiiMask.Sha256Hex("09080706050"),
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active,
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var controller = Hcm030TestSupport.CreateEssController(db, employee.Id);

        await controller.ApplyCompetition(competition.Id, [line.Id], "خبرة مناسبة");
        await controller.ApplyCompetition(competition.Id, [line.Id], "مرة ثانية");

        var application = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentIntakeChannel.Internal, application.IntakeChannel);
        Assert.Equal(competition.Id, application.CompetitionId);
        Assert.Null(application.RequisitionId);
        var internalApp = await db.InternalApplications.AsNoTracking().SingleAsync();
        Assert.Equal(application.Id, internalApp.PromotedRecruitmentApplicationId);
        Assert.Equal(CompetitionIntakeService.DuplicateMessage, controller.TempData["Error"]);
    }

    // ---- intake close job -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_intake_close_job_closes_a_competition_after_its_closing_date()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 2);
        var closer = new RecruitmentIntakeCloser(db, NullLogger<RecruitmentIntakeCloser>.Instance);

        Assert.Equal(0, await closer.CloseExpiredAsync(competition.ApplicationClosingDate, default));
        var closed = await closer.CloseExpiredAsync(competition.ApplicationClosingDate.AddDays(1), default);

        Assert.Equal(1, closed);
        var saved = await db.RecruitmentCompetitions.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentCompetitionStatus.IntakeClosed, saved.Status);
        Assert.False((await db.PublicJobPostings.AsNoTracking().SingleAsync()).IsPublished);
        Assert.All(await db.RecruitmentRequisitions.AsNoTracking().Where(r => r.CompetitionLineId != null).ToListAsync(),
            r => Assert.NotNull(r.IntakeClosedAt));
        Assert.Equal(0, await closer.CloseExpiredAsync(competition.ApplicationClosingDate.AddDays(2), default));
    }

    // ---- HR bulk screening ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Bulk_admission_to_the_exam_needs_the_COI_declaration_and_a_reason_for_flagged_applications()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        var line = competition.Lines.Single();
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);
        await controller.ApplyCompetition(ApplyModel(competition, "0101010101", line.Id));
        var flaggedVm = ApplyModel(competition, "0202020202", line.Id);
        flaggedVm.BirthDate = null;
        await controller.ApplyCompetition(flaggedVm);
        var flagged = await db.RecruitmentApplications.SingleAsync(a => a.NationalIdHash == HRManagement.SharedKernel.PiiMask.Sha256Hex("0202020202"));
        flagged.EligibilityFlags = RecruitmentEligibilityRules.BelowMinExperience;
        await db.SaveChangesAsync();
        var ids = await db.RecruitmentApplications.Select(a => a.Id).ToListAsync();
        var intake = CreateIntake(db);

        var noCoi = await intake.BulkTransitionAsync(competition.Id, ids, RecruitmentApplicationStatus.Shortlisted, false, null, null, Creator);
        Assert.All(noCoi, r => Assert.False(r.Success));

        var withCoi = await intake.BulkTransitionAsync(competition.Id, ids, RecruitmentApplicationStatus.Shortlisted, true, null, null, Creator);
        Assert.Single(withCoi, r => r.Success);
        Assert.Equal(RecruitmentEligibilityRules.OverrideReasonRequiredMessage, Assert.Single(withCoi, r => !r.Success).Error);

        var withReason = await intake.BulkTransitionAsync(
            competition.Id, [flagged.Id], RecruitmentApplicationStatus.Shortlisted, true, "الخبرة الفعلية موثقة بشهادات معتمدة", null, Creator);
        Assert.True(Assert.Single(withReason).Success);
        Assert.All(await db.RecruitmentApplications.AsNoTracking().ToListAsync(), a => Assert.Equal(RecruitmentApplicationStatus.Shortlisted, a.Status));
    }

    // ---- public status projection ------------------------------------------------------------------------------------------

    [Fact]
    public void Competition_applications_show_exam_and_results_states_instead_of_interview_states()
    {
        var now = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        CandidatePublicView Project(RecruitmentApplicationStatus status, CandidateCompetitionInput? competition) =>
            CandidatePublicStatusProjection.Project(new CandidatePublicStatusInput
            {
                Status = status,
                AppliedAtUtc = now.AddDays(-20),
                NowUtc = now,
                Competition = competition
            });

        Assert.Equal("قيد المراجعة", Project(RecruitmentApplicationStatus.Submitted, new()).LabelAr);
        Assert.Equal("مقبول للاختبار", Project(RecruitmentApplicationStatus.Shortlisted, new()).LabelAr);
        Assert.Equal("مقبول للاختبار — 2026/11/10",
            Project(RecruitmentApplicationStatus.Shortlisted, new(ExamAtUtc: new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc))).LabelAr);
        Assert.Equal("بانتظار النتائج", Project(RecruitmentApplicationStatus.Shortlisted, new(ExamHeld: true)).LabelAr);
        Assert.Equal("النتائج منشورة — راجع نتيجتك", Project(RecruitmentApplicationStatus.Shortlisted, new(ExamHeld: true, ResultsPublished: true)).LabelAr);
        Assert.Equal("اعتراضك قيد المراجعة", Project(RecruitmentApplicationStatus.Shortlisted, new(ResultsPublished: true, ObjectionPending: true)).LabelAr);
        Assert.Equal("مرشح للتعيين على محاسب أول", Project(RecruitmentApplicationStatus.Ranked, new(AllocatedLineTitle: "محاسب أول")).LabelAr);
        Assert.Equal("غير ناجح", Project(RecruitmentApplicationStatus.Rejected, new(NotSuccessful: true)).LabelAr);

        var timeline = Project(RecruitmentApplicationStatus.Shortlisted, new()).Timeline;
        Assert.Contains(timeline, s => s.LabelAr == "الامتحان");
        Assert.DoesNotContain(timeline, s => s.LabelAr == "مدعو للمقابلة");

        // Not a competition: unchanged.
        Assert.Equal("مدعو للمقابلة", Project(RecruitmentApplicationStatus.Shortlisted, null).LabelAr);
    }

    // ---- RequisitionId-null fixes --------------------------------------------------------------------------------------------

    [Fact]
    public void A_shortlisted_competition_application_is_never_a_legacy_hire_without_offer()
    {
        var legacy = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Shortlisted };
        var competition = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Shortlisted, CompetitionId = Guid.NewGuid() };

        Assert.True(RecruitmentWorkflowRules.CanHire(legacy, legacyHireWithoutOffer: true));
        Assert.False(RecruitmentWorkflowRules.CanHire(competition, legacyHireWithoutOffer: true));
        Assert.False(SelectionWorkflowRules.CanTransition(competition, RecruitmentApplicationStatus.Hired, legacyHireWithoutOffer: true));
        Assert.DoesNotContain(RecruitmentApplicationStatus.Hired, SelectionWorkflowRules.GetAllowedTargets(competition, legacyHireWithoutOffer: true));
    }

    [Fact]
    public void The_retention_trigger_of_a_competition_application_is_the_competition_end()
    {
        var finalizedAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var running = new RecruitmentApplication
        {
            AppliedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CompetitionId = Guid.NewGuid(),
            Competition = new RecruitmentCompetition { Status = RecruitmentCompetitionStatus.ResultsPublished }
        };
        Assert.Null(CandidateApplicationLegalLifecycleService.RetentionTrigger(running, null));
        Assert.Equal(CandidateRetentionVerdict.OpenRequisition,
            CandidateApplicationLegalLifecycleService.EvaluatePair(running, null, null, false, DateTime.UtcNow));

        running.Competition.Status = RecruitmentCompetitionStatus.Finalized;
        running.Competition.FinalizedAt = finalizedAt;
        Assert.Equal(finalizedAt, CandidateApplicationLegalLifecycleService.RetentionTrigger(running, null));

        running.Competition = null; // not loaded: never due
        Assert.Null(CandidateApplicationLegalLifecycleService.RetentionTrigger(running, null));
    }

    [Fact]
    public async Task Retention_never_counts_an_application_of_a_running_competition_as_due()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        db.RecruitmentApplications.Add(new RecruitmentApplication
        {
            ApplicantFullName = "متقدم قديم",
            AppliedPosition = competition.Title,
            CompetitionId = competition.Id,
            NationalIdHash = new string('c', 64),
            AppliedAt = DateTime.UtcNow.AddYears(-10),
            Status = RecruitmentApplicationStatus.Submitted
        });
        await db.SaveChangesAsync();
        var retention = new CandidateRetentionService(db, new HRManagement.ServiceDefaults.EfFileReferenceOutbox(db),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()), NullLogger<CandidateRetentionService>.Instance);

        var counts = await retention.CountAsync(Guid.Empty, DateTime.UtcNow);
        Assert.Equal(0, counts.Due);

        competition.Status = RecruitmentCompetitionStatus.Finalized;
        competition.FinalizedAt = DateTime.UtcNow.AddYears(-5);
        await db.SaveChangesAsync();
        Assert.Equal(1, (await retention.CountAsync(Guid.Empty, DateTime.UtcNow)).Due);
    }

    [Fact]
    public async Task The_DSAR_export_carries_the_competition_and_the_ordered_preferences()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1, 1);
        var lines = competition.Lines.OrderBy(l => l.LineNo).ToList();
        await Hcm030TestSupport.CreatePublicCareersController(db).ApplyCompetition(ApplyModel(competition, "0102030405", lines[1].Id, lines[0].Id));
        var application = await db.RecruitmentApplications.AsNoTracking().SingleAsync();

        var result = await new DsarInternalController(db).GetCandidateByApplication(application.Id, default);
        var json = JsonSerializer.Serialize(((OkObjectResult)result.Result!).Value);
        using var doc = JsonDocument.Parse(json);
        var competitionRecord = doc.RootElement.GetProperty("competition");

        Assert.Equal(competition.Number, competitionRecord.GetProperty("number").GetString());
        var preferences = competitionRecord.GetProperty("preferences").EnumerateArray().ToList();
        Assert.Equal([2, 1], preferences.Select(p => p.GetProperty("lineNo").GetInt32()));
    }

    [Fact]
    public async Task The_legacy_filter_of_the_HR_index_excludes_competition_applications()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        db.RecruitmentApplications.AddRange(
            new RecruitmentApplication { ApplicantFullName = "قديم", AppliedPosition = "x", AppliedAt = DateTime.UtcNow, Status = RecruitmentApplicationStatus.Submitted },
            new RecruitmentApplication
            {
                ApplicantFullName = "مسابقة",
                AppliedPosition = "y",
                AppliedAt = DateTime.UtcNow,
                Status = RecruitmentApplicationStatus.Submitted,
                CompetitionId = competition.Id,
                NationalIdHash = new string('d', 64)
            });
        await db.SaveChangesAsync();

        var controller = Hcm029TestSupport.CreateHireController(db);
        var legacy = Assert.IsType<RecruitmentApplicationsIndexViewModel>(Assert.IsType<ViewResult>(
            await controller.Index(new RecruitmentApplicationsFilter { LegacyOnly = true })).Model);
        Assert.Equal(["قديم"], legacy.Applications.Select(a => a.ApplicantFullName));

        var byCompetition = Assert.IsType<RecruitmentApplicationsIndexViewModel>(Assert.IsType<ViewResult>(
            await controller.Index(new RecruitmentApplicationsFilter { CompetitionId = competition.Id })).Model);
        Assert.Equal(["مسابقة"], byCompetition.Applications.Select(a => a.ApplicantFullName));
    }
}
