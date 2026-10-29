using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Web.ModelBinding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P8): regressions for the independent review findings — finalize ignores candidates who left after
/// publication (R1), decimals bind under ar-SY (R2), an upheld correction newer than the published version blocks finalize
/// (R3), consecutive recalls take distinct reserves (R4), results SoD covers the submitter and the tie resolver (R5), manual
/// transitions after seating and the recall after HR rejection (R6), retention spares live competition applications (R7),
/// no publication after cancellation (R8), no anonymous results list for internal-only competitions (R9).
/// </summary>
public sealed class Hcm031ReviewFixTests
{
    private const string ObjectionReason = "أعترض على احتساب علامة الدفتر لوجود سؤال غير مصحح";
    private const string Rationale = "تبين للمراجع أن السؤال الخامس لم يُصحح وأُضيفت علامته وفق نموذج الإجابة";

    // ------------------------------------------------------------------ R1

    [Fact]
    public async Task R1_finalize_skips_a_passed_candidate_who_left_after_publication()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m, 75m, 70m, 65m, 40m]);
        var (l1, l2) = Lines(fx);
        foreach (var app in fx.Applications)
            await SetPreferencesAsync(db, app, l1.Id, l2.Id);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        // The second by merit withdraws after publication (portal) — still a Passed row in the published version.
        var leaver = await db.RecruitmentApplications.SingleAsync(a => a.Id == fx.Applications[1].Id);
        leaver.Status = RecruitmentApplicationStatus.Withdrawn;
        await db.SaveChangesAsync();

        var result = await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator);

        Assert.True(result.Success, result.Error);
        Assert.Equal(5, result.Allocated);
        Assert.Equal(0, result.Reserve);
        var allocations = await db.CompetitionAllocations.AsNoTracking().ToListAsync();
        Assert.Equal(5, allocations.Select(a => a.AllocatedRequisitionId).Distinct().Count());
        Assert.DoesNotContain(allocations, a => a.RecruitmentApplicationId == leaver.Id);
        Assert.Empty(await db.CompetitionReserveEntries.AsNoTracking().Where(e => e.RecruitmentApplicationId == leaver.Id).ToListAsync());
        Assert.Equal(RecruitmentApplicationStatus.Withdrawn, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == leaver.Id)).Status);
        // 65 now takes the vacancy the leaver would have consumed.
        Assert.Equal(RecruitmentApplicationStatus.Ranked, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == fx.Applications[5].Id)).Status);
    }

    // ------------------------------------------------------------------ R2

    [Theory]
    [InlineData("37.5", 37.5)]
    [InlineData("37٫5", 37.5)]
    [InlineData("٣٧٫٥", 37.5)]
    [InlineData("100", 100)]
    public async Task R2_decimals_from_number_inputs_bind_under_the_ar_SY_request_culture(string posted, double expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SY");
        try
        {
            var context = await BindAsync(new InvariantDecimalModelBinder(), typeof(decimal?), posted);
            Assert.True(context.Result.IsModelSet);
            Assert.Equal((decimal)expected, context.Result.Model);
            Assert.Equal(0, context.ModelState.ErrorCount);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task R2_empty_and_invalid_values_behave_like_the_stock_binder()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SY");
        try
        {
            var empty = await BindAsync(new InvariantDecimalModelBinder(), typeof(decimal?), "");
            Assert.True(empty.Result.IsModelSet);
            Assert.Null(empty.Result.Model);
            Assert.Equal(1, (await BindAsync(new InvariantDecimalModelBinder(), typeof(decimal), "")).ModelState.ErrorCount);
            var invalid = await BindAsync(new InvariantDecimalModelBinder(), typeof(decimal), "abc");
            Assert.False(invalid.Result.IsModelSet);
            Assert.Equal(1, invalid.ModelState.ErrorCount);

            // Root cause: where ar-SY's separator is not «.», the stock binder rejects what a number input posts.
            if (CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator != ".")
            {
                var stock = new DecimalModelBinder(NumberStyles.Float | NumberStyles.AllowThousands, NullLoggerFactory.Instance);
                Assert.Equal(1, (await BindAsync(stock, typeof(decimal?), "37.5")).ModelState.ErrorCount);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ------------------------------------------------------------------ R3

    [Fact]
    public async Task R3_an_upheld_correction_newer_than_the_published_version_blocks_finalize()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [60m, 45m, 40m]);
        await PublishResultsAsync(db, fx.Competition.Id);
        var objections = CreateObjections(db);
        var reviewer = await SeedEmployeeAsync(db, "مراجع مستقل");
        var first = await FileAndAssignAsync(db, objections, fx.Applications[1].Id, reviewer);
        var second = await FileAndAssignAsync(db, objections, fx.Applications[2].Id, reviewer);

        // The first upheld objection opens version 2, which goes to approval.
        Assert.True((await objections.DecideAsync(first, RecruitmentAppealOutcome.ObjectionUpheld, Rationale, 75m,
            Hcm028AppealTestSupport.Reviewer(Guid.NewGuid(), reviewer.Id))).Success);
        var v2 = await db.CompetitionResultVersions.SingleAsync(v => v.VersionNo == 2);
        Assert.True((await CreateResults(db).SubmitForApprovalAsync(v2.Id, Creator, "creator")).Success);
        // The second is upheld while v2 awaits approval: its recompute is refused (notice only).
        var (upheld, _, notice) = await objections.DecideAsync(second, RecruitmentAppealOutcome.ObjectionUpheld, Rationale, 90m,
            Hcm028AppealTestSupport.Reviewer(Guid.NewGuid(), reviewer.Id));
        Assert.True(upheld);
        Assert.NotNull(notice);
        Assert.False(await db.CompetitionResultVersions.AnyAsync(v => v.VersionNo == 3));

        await ApproveAndPublishSubmittedAsync(db, v2.Id);
        await MoveWindowBackAsync(db, fx.Competition.Id);
        var refused = await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator);
        Assert.False(refused.Success);
        Assert.Equal(CompetitionAllocationService.StaleCorrectionMessage, refused.Error);

        // Recompute → approve → publish v3 (it carries the 90), then finalize goes through.
        await PublishResultsAsync(db, fx.Competition.Id);
        var v3 = await db.CompetitionResultVersions.AsNoTracking().SingleAsync(v => v.VersionNo == 3);
        Assert.Equal(1, (await db.CompetitionResults.AsNoTracking()
            .SingleAsync(r => r.ResultVersionId == v3.Id && r.RecruitmentApplicationId == fx.Applications[2].Id)).OverallRank);
        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);
    }

    // ------------------------------------------------------------------ R4 / R6

    [Fact]
    public async Task R4_R6_two_vacancies_of_one_line_recall_two_distinct_reserves_after_HR_rejections()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m, 75m, 70m]);
        var (_, l2) = Lines(fx);
        foreach (var app in fx.Applications)
            await SetPreferencesAsync(db, app, l2.Id); // line 2 has two vacancies
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);
        var allocated = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.Status == RecruitmentApplicationStatus.Ranked).OrderBy(a => a.Rank).ToListAsync();
        Assert.Equal([fx.Applications[0].Id, fx.Applications[1].Id], allocated.Select(a => a.Id));

        var selection = new RecruitmentSelectionService(db);
        var reserve = new CompetitionReserveService(db);
        var recalled = new List<Guid?>();
        foreach (var app in allocated)
        {
            Assert.True((await selection.TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.Rejected, false, "انسحب من التعيين بعد التوزيع")).Success);
            var result = await reserve.RecallForVacancyAsync(app.RequisitionId!.Value, Creator);
            Assert.True(result.Recalled);
            recalled.Add(result.RecalledApplicationId);
        }

        Assert.Equal([fx.Applications[2].Id, fx.Applications[3].Id], recalled);
        foreach (var (app, holder) in allocated.Zip(recalled))
            Assert.Equal(app.RequisitionId, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == holder)).RequisitionId);
        var fromReserve = await db.CompetitionAllocations.AsNoTracking().Where(a => a.FromReserve).ToListAsync();
        Assert.Equal(2, fromReserve.Select(a => a.AllocatedRequisitionId).Distinct().Count());
        Assert.Equal(2, fromReserve.Select(a => a.RecruitmentApplicationId).Distinct().Count());
    }

    [Fact]
    public async Task R6_an_allocated_application_with_a_live_offer_leaves_through_the_offer_and_is_never_recalled_around()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m]);
        var (_, l2) = Lines(fx);
        foreach (var app in fx.Applications)
            await SetPreferencesAsync(db, app, l2.Id);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);
        var holder = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == fx.Applications[0].Id);
        var (offer, draftError) = await CreateOfferServiceWithRecall(db).CreateDraftAsync(holder.Id, 600_000m, null, DateTime.UtcNow.Date.AddDays(20), null, Creator);
        Assert.True(offer is not null, draftError);
        var tracked = await db.OfferLetters.SingleAsync(o => o.Id == offer!.Id);
        tracked.Status = OfferStatus.Issued;
        tracked.IssuedDate = DateTime.UtcNow;
        tracked.ExpiryDate = DateTime.UtcNow.AddDays(14);
        await db.SaveChangesAsync();

        // HR cannot reject around the live offer…
        var refused = await new RecruitmentSelectionService(db).TransitionStatusAsync(holder.Id, RecruitmentApplicationStatus.Rejected, false, "رفض بعد إصدار العرض");
        Assert.False(refused.Success);
        Assert.Equal(RecruitmentSelectionService.CompetitionActiveOfferMessage, refused.Error);
        // …and even if the application left by another path, the recall waits for the offer's settlement.
        var leaving = await db.RecruitmentApplications.SingleAsync(a => a.Id == holder.Id);
        leaving.Status = RecruitmentApplicationStatus.Withdrawn;
        await db.SaveChangesAsync();
        Assert.False((await new CompetitionReserveService(db).RecallForVacancyAsync(holder.RequisitionId!.Value, Creator)).Recalled);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed,
            (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == fx.Applications[2].Id)).Status);
    }

    [Fact]
    public async Task R6_after_seating_HR_may_only_reject_or_record_a_withdrawal()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 60m]);
        var selection = new RecruitmentSelectionService(db);

        var moved = await selection.TransitionStatusAsync(fx.Applications[0].Id, RecruitmentApplicationStatus.InterviewScheduled, false);

        Assert.False(moved.Success);
        Assert.Equal(RecruitmentSelectionService.CompetitionManualTransitionMessage, moved.Error);
        Assert.True((await selection.TransitionStatusAsync(fx.Applications[1].Id, RecruitmentApplicationStatus.Rejected, false, "غياب موثق عن الامتحان")).Success);
        var (version, error) = await CreateResults(db).ComputeDraftAsync(fx.Competition.Id, null, Creator);
        Assert.True(version is not null, error);
    }

    // ------------------------------------------------------------------ R5

    [Fact]
    public async Task R5_the_person_who_submitted_the_results_cannot_approve_any_step()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 40m]);
        var results = CreateResults(db);
        var (version, _) = await results.ComputeDraftAsync(fx.Competition.Id, null, Creator);
        Assert.True((await results.SubmitForApprovalAsync(version!.Id, Reviewer, "reviewer")).Success);
        var instanceId = (await db.CompetitionResultVersions.AsNoTracking().SingleAsync()).WorkflowInstanceId!.Value;
        var actions = CreateActionService(db);

        var self = await actions.ApproveAsync(instanceId, Actor(Reviewer), null, null);

        Assert.False(self.Success);
        Assert.Contains("قدّم", self.Error, StringComparison.Ordinal);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Guid.NewGuid()), null, null)).Success);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Approver, "hr-management-approver"), null, null)).Success);
    }

    [Fact]
    public async Task R5_the_person_who_decided_a_tie_cannot_approve_the_results()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 70m, 90m]);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        competition.TieBreakOrder = nameof(CompetitionTieBreak.ExamScore);
        await db.SaveChangesAsync();
        var results = CreateResults(db);
        var (version, _) = await results.ComputeDraftAsync(competition.Id, null, Creator);
        Assert.True((await results.ResolveTieAsync(version!.Id, [fx.Applications[1].Id, fx.Applications[0].Id],
            "قرار اللجنة بتقديم صاحب الخبرة الأطول", Reviewer)).Success);
        Assert.True((await results.SubmitForApprovalAsync(version.Id, Creator, "creator")).Success);
        var instanceId = (await db.CompetitionResultVersions.AsNoTracking().SingleAsync()).WorkflowInstanceId!.Value;

        var refused = await CreateActionService(db).ApproveAsync(instanceId, Actor(Reviewer), null, null);

        Assert.False(refused.Success);
        Assert.Contains("التعادل", refused.Error, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ R7

    [Fact]
    public void R7_a_competition_application_that_still_holds_a_line_never_triggers_retention()
    {
        var finalizedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var competition = new RecruitmentCompetition { Status = RecruitmentCompetitionStatus.Finalized, FinalizedAt = finalizedAt };
        RecruitmentApplication App(RecruitmentApplicationStatus status) =>
            new() { CompetitionId = Guid.NewGuid(), Competition = competition, Status = status };

        foreach (var live in CandidateApplicationLegalLifecycleService.CompetitionLiveStatuses)
            Assert.Null(CandidateApplicationLegalLifecycleService.RetentionTrigger(App(live), null));
        Assert.Equal(finalizedAt, CandidateApplicationLegalLifecycleService.RetentionTrigger(App(RecruitmentApplicationStatus.Rejected), null));
        Assert.Equal(finalizedAt, CandidateApplicationLegalLifecycleService.RetentionTrigger(App(RecruitmentApplicationStatus.OfferDeclined), null));
    }

    [Fact]
    public async Task R7_retention_counts_only_competition_applications_that_left()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [90m, 40m]);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        Assert.True((await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator)).Success);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        competition.FinalizedAt = DateTime.UtcNow.AddYears(-5);
        await db.SaveChangesAsync();
        var retention = Hcm030RetentionJobTests.Service(db);

        // The allocated (Ranked) candidate keeps its data; only the rejected one is due.
        Assert.Equal(1, (await retention.CountAsync(Guid.Empty, DateTime.UtcNow)).Due);
        var allocated = await db.RecruitmentApplications.SingleAsync(a => a.Id == fx.Applications[0].Id);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, allocated.Status);
        allocated.Status = RecruitmentApplicationStatus.Withdrawn;
        await db.SaveChangesAsync();
        Assert.Equal(2, (await retention.CountAsync(Guid.Empty, DateTime.UtcNow)).Due);
    }

    // ------------------------------------------------------------------ R8

    [Fact]
    public async Task R8_results_of_a_cancelled_competition_are_never_submitted_or_published()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 40m]);
        var results = CreateResults(db);
        var (version, _) = await results.ComputeDraftAsync(fx.Competition.Id, null, Creator);
        Assert.True((await results.SubmitForApprovalAsync(version!.Id, Creator, "creator")).Success);
        var instanceId = (await db.CompetitionResultVersions.AsNoTracking().SingleAsync()).WorkflowInstanceId!.Value;
        var actions = CreateActionService(db);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Reviewer), null, null)).Success);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Approver, "hr-management-approver"), null, null)).Success);
        Assert.True((await CreateLifecycle(db).CancelAsync(fx.Competition.Id, "إلغاء المسابقة لأسباب إدارية طارئة", Creator)).Success);

        var (published, error) = await results.PublishAsync(version.Id, Creator);

        Assert.False(published);
        Assert.Equal(CompetitionResultsService.ClosedCompetitionMessage, error);
        var competition = await db.RecruitmentCompetitions.AsNoTracking().SingleAsync();
        Assert.Null(competition.ResultsFirstPublishedAt);
        Assert.Null(competition.CurrentResultVersionId);
        Assert.False(await db.RecruitmentApplicationEvents.AnyAsync(e => e.EventType == RecruitmentApplicationEventTypes.ResultsPublished));
    }

    // ------------------------------------------------------------------ R9

    [Theory]
    [InlineData(RecruitmentPublishingChannel.InternalOnly, false)]
    [InlineData(RecruitmentPublishingChannel.PublicOnly, true)]
    [InlineData(RecruitmentPublishingChannel.Both, true)]
    public async Task R9_the_anonymous_results_list_exists_only_on_the_public_channel(RecruitmentPublishingChannel channel, bool visible)
    {
        using var db = Hcm027TestSupport.CreateContext();
        var fx = await SeedMarkedAsync(db, [70m, 40m]);
        var competition = await db.RecruitmentCompetitions.SingleAsync();
        competition.PublishingChannel = channel;
        await db.SaveChangesAsync();
        await PublishResultsAsync(db, fx.Competition.Id);
        var results = CreateResults(db);

        Assert.Equal(visible, await results.GetPublicAsync(fx.Competition.Id, 1, publicChannelOnly: true) is not null);
        Assert.NotNull(await results.GetPublicAsync(fx.Competition.Id, 1)); // HR's PDF
    }

    // ------------------------------------------------------------------ helpers

    private static (CompetitionLine L1, CompetitionLine L2) Lines(MarkedCompetition fx)
    {
        var lines = fx.Competition.Lines.OrderBy(l => l.LineNo).ToList();
        return (lines[0], lines[1]);
    }

    private static CompetitionObjectionService CreateObjections(HRCoreDbContext db) =>
        new(db, Hcm028AppealTestSupport.CreateService(db), CreateResults(db));

    private static async Task<Guid> FileAndAssignAsync(HRCoreDbContext db, CompetitionObjectionService objections, Guid applicationId, Employee reviewer)
    {
        var (appeal, error) = await objections.FileAsync(applicationId, null, ObjectionReason, RecruitmentAppealFiledBy.Candidate, null);
        Assert.True(appeal is not null, error);
        var (assigned, assignError) = await Hcm028AppealTestSupport.CreateService(db).AssignReviewerAsync(appeal!.Id, reviewer.Id, Creator, null, "hr");
        Assert.True(assigned, assignError);
        return appeal.Id;
    }

    /// <summary>Approves (Reviewer, then Approver) and publishes a version already submitted by <see cref="Creator"/>.</summary>
    private static async Task ApproveAndPublishSubmittedAsync(HRCoreDbContext db, Guid versionId)
    {
        var instanceId = await db.CompetitionResultVersions.Where(v => v.Id == versionId).Select(v => v.WorkflowInstanceId!.Value).SingleAsync();
        var actions = CreateActionService(db);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Reviewer), null, null)).Success);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Approver, "hr-management-approver"), null, null)).Success);
        var (published, error) = await CreateResults(db).PublishAsync(versionId, Creator);
        Assert.True(published, error);
    }

    private static async Task MoveWindowBackAsync(HRCoreDbContext db, Guid competitionId)
    {
        var competition = await db.RecruitmentCompetitions.SingleAsync(c => c.Id == competitionId);
        competition.ResultsFirstPublishedAt = DateTime.UtcNow.AddDays(-(competition.ObjectionWindowDays + 1));
        await db.SaveChangesAsync();
    }

    private static async Task<ModelBindingContext> BindAsync(IModelBinder binder, Type type, string posted)
    {
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(type);
        var form = new FormCollection(new Dictionary<string, StringValues> { ["value"] = posted });
        var context = new DefaultModelBindingContext
        {
            ModelMetadata = metadata,
            ModelName = "value",
            FieldName = "value",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new FormValueProvider(BindingSource.Form, form, CultureInfo.CurrentCulture),
            BindingSource = BindingSource.Form
        };
        await binder.BindModelAsync(context);
        return context;
    }
}
