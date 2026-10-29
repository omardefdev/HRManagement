using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.RecruitmentKpi;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm032MetricsFixture;
using F = HR.Core.Domain.Services.RecruitmentKpi.RecruitmentFunnelStage;
using St = HR.Core.Domain.Enums.RecruitmentApplicationStatus;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-032 (PLAN-32, AC-HCM-32a): every recruitment KPI against numbers computed by hand from
/// <see cref="Hcm032MetricsFixture"/>; scope tests for unrestricted, branch, denied and out-of-scope filters (AC-HCM-32b).
/// Period [2026-01-01, 2026-11-01); now = 2026-10-29 12:00 UTC.
/// </summary>
public class Hcm032RecruitmentMetricsTests
{
    private static async Task<(HRCoreDbContext Db, RecruitmentMetricsService Service)> SeedAsync()
    {
        var db = Hcm027TestSupport.CreateContext();
        await Hcm032MetricsFixture.SeedAsync(db);
        db.ChangeTracker.Clear();
        return (db, CreateService(db));
    }

    // Furthest stage per cohort application (27 apps, all applied in the period):
    //   Applied(0):      A4 A6 B6 B8 C3 D5                 = 6
    //   Screened(1):     A3 (ScreenedAt) B4 B5 C2 C4       = 5
    //   Shortlisted(2):  B2 C1 D3 D4                       = 4
    //   Ranked(5):       A2 (RankedAt) B7 (reserve) B9 D1 D2 (reserve) D6 = 6
    //   OfferIssued(6):  A5 (offer IssuedDate) B3 B10      = 3
    //   Hired(8):        A1 (status) B1 C5                 = 3
    // Funnel (reached ≥ stage): 27, 21, 16, 12, 12, 12, 6, 3, 3.
    // Coverage: with events B1–B10, C2, C5, D1–D6 = 18 / 27 = 66.7 %.
    [Fact]
    public async Task Funnel_matches_the_hand_calculation()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var funnel = await service.GetFunnelAsync(Period, Unrestricted);

        Assert.Equal(27, funnel.CohortCount);
        Assert.Equal([27, 21, 16, 12, 12, 12, 6, 3, 3], funnel.Stages.Select(s => s.Count));
        Assert.Equal(FunnelReachRules.Stages, funnel.Stages.Select(s => s.Stage));
        Assert.Equal(18, funnel.CoveredCount);
        Assert.Equal(66.7m, funnel.CoveragePercent);
        // Current status: Submitted B6 C3; UnderReview B4 (upheld appeal) B5 C2; Shortlisted C1; Rejected A2 A3 A4 B2 C4 D3 D4 D5;
        // Hired A1 B1 C5; Ranked B9 D1 D6; Withdrawn A6 B8; OfferDeclined A5 B3 B10; ReserveListed B7 D2.
        Assert.Equal(
            [(St.Submitted, 2), (St.UnderReview, 3), (St.Shortlisted, 1), (St.Rejected, 8), (St.Hired, 3), (St.Ranked, 3),
             (St.Withdrawn, 2), (St.OfferDeclined, 3), (St.ReserveListed, 2)],
            funnel.CurrentStatuses.Select(s => (s.Status, s.Count)));
    }

    // Time to hire (hired in the period):
    //   A1: 2026-01-15 → employee created 2026-02-20 (fallback) = 36
    //   B1: 2026-09-01 → Hired event 2026-10-01               = 30
    //   C5: 2026-10-23 → employee created 2026-10-28 (fallback) = 5
    //   sorted 5, 30, 36 → median 30, P90 = 3rd (ceil 2.7) = 36, n 3, fallback 2, coverage 1/3 = 33.3 %.
    [Fact]
    public async Task Time_to_hire_matches_the_hand_calculation()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetTimeToHireAsync(Period, Unrestricted);

        Assert.Equal(new DurationMeasure(30m, 36m, 3, 2), result);
        Assert.Equal(33.3m, result.CoveragePercent);
    }

    // Time to fill (first hire of the requisition in the period):
    //   R1: approved 2026-01-10 → A1 2026-02-20 (hired fallback)                 = 41
    //   R3: approved 2026-03-04 → B1 2026-10-01                                  = 211
    //   R2: no approval → requested 2026-02-01 (start fallback) → C5 2026-10-28 (hired fallback) = 269
    //   sorted 41, 211, 269 → median 211, P90 269, n 3; start fallback 1, hired fallback 2, rows with any fallback 2.
    [Fact]
    public async Task Time_to_fill_matches_the_hand_calculation_and_counts_fallbacks()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetTimeToFillAsync(Period, Unrestricted);

        Assert.Equal(new DurationMeasure(211m, 269m, 3, 2), result.Days);
        Assert.Equal(1, result.StartFallbackCount);
        Assert.Equal(2, result.HiredFallbackCount);
    }

    // Stage time (days between consecutive status events, event rows only):
    //   Submitted     17 values: 1×7 (B5 B7 B9 B10 C2 C5 D6), 2×7 (B1 B2 B3 B4 B8 D3 D4), 3×2 (D2 D5), 4 (D1) → median 9th = 2
    //   UnderReview   12 values: 1 1 2 3 3 4 5 5 5 5 5 5 (B4 B10 B9 B7 D6 D4 B1 B2 B3 D1 D2 D3) → (4 + 5) / 2 = 4.5
    //   Shortlisted   11 values: 1 2 3 7 8 11 46 46 46 46 46 → 11
    //   Rejected      B4 09-07 → 09-20 (appeal upheld, back to UnderReview) = 13 (other rejections are final)
    //   InterviewScheduled B1 5, B3 5 → 5; Assessed B1 5, B3 5 → 5; Ranked B1 2, B3 7, B7 12, B10 4 → (4 + 7) / 2 = 5.5
    //   OfferExtended B1 3, B3 7, B10 3 → 3; OfferAccepted B1 6 → 6; ReserveListed D6 15 → 15
    [Fact]
    public async Task Stage_time_matches_the_hand_calculation()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetStageTimeAsync(Period, Unrestricted);

        Assert.Equal(27, result.CohortCount);
        Assert.Equal(18, result.CoveredCount);
        Assert.Equal(
            [(St.Submitted, 2m, 17), (St.UnderReview, 4.5m, 12), (St.Shortlisted, 11m, 11), (St.Rejected, 13m, 1), (St.InterviewScheduled, 5m, 2),
             (St.Assessed, 5m, 2), (St.Ranked, 5.5m, 4), (St.OfferExtended, 3m, 3), (St.OfferAccepted, 6m, 1), (St.ReserveListed, 15m, 1)],
            result.Rows.Select(r => (r.Status, r.MedianDays!.Value, r.N)));
    }

    // Offers with a final outcome in the period: Accepted O1 O3 O6 = 3; Declined O2 O7 = 2; Expired O4 = 1; Revoked O5 = 1
    // (excluded). Acceptance = 3 / (3 + 2 + 1) = 50 %.
    [Fact]
    public async Task Offer_acceptance_excludes_revoked_offers()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetOfferOutcomesAsync(Period, Unrestricted);

        Assert.Equal(new OfferOutcomeResult(3, 2, 1, 1), result);
        Assert.Equal(50m, result.AcceptanceRate);
    }

    // Declines: O2 (no category, HR on behalf), O7 (Salary, candidate).
    [Fact]
    public async Task Decline_reasons_group_by_category_and_decliner()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetDeclineReasonsAsync(Period, Unrestricted);

        Assert.Equal(2, result.Total);
        Assert.Equal([("Salary", "الراتب", 1), ("Uncategorized", "غير مصنف", 1)], result.ByCategory.Select(c => (c.Key, c.Label, c.Count)));
        Assert.Equal([("Candidate", 1), ("HrOnBehalf", 1)], result.ByDeclinedBy.Select(c => (c.Key, c.Count)));
    }

    // Channels: HrEntered A1 B4 C2 = 3 (hired A1 → 33.3 %); Public A3 A4 B1 B2 B5 B7 B9 B10 C1 C5 D1 D2 D4 D5 D6 = 15
    // (hired B1 C5 → 13.3 %); Internal A5 A6 B3 B8 C3 D3 = 6 (0 %); Legacy A2 B6 C4 = 3 (0 %).
    [Fact]
    public async Task Channel_mix_matches_the_hand_calculation()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetChannelMixAsync(Period, Unrestricted);

        Assert.Equal(
            [(RecruitmentIntakeChannel.HrEntered, 3, 1, 33.3m), (RecruitmentIntakeChannel.Public, 15, 2, 13.3m),
             (RecruitmentIntakeChannel.Internal, 6, 0, 0m), (RecruitmentIntakeChannel.Legacy, 3, 0, 0m)],
            result.Rows.Select(r => (r.Channel, r.Applied, r.Hired, r.HireRate!.Value)));
    }

    // Appealable rejections in the period (default kinds SelectionDecision, NotSelectedOnHire, OfferRevoked):
    // A2, A3 (no kind → SelectionDecision), B2, B4 (rejection cleared by its upheld appeal; the Rejected event 09-07 counts), C4
    // = 5 (A4 RequisitionClosed and D3 D4 D5 CompetitionResult are not appealable). Appeals filed: AP(B2),
    // AP(B4) = 2 → 40 %. Decided: Upheld 1 (B4), Dismissed 1 (B2). Objections: filed 2 (D3, D4), upheld 1, dismissed 1.
    [Fact]
    public async Task Appeal_rate_and_objections_match_the_hand_calculation()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetAppealsAsync(Period, Unrestricted);

        Assert.Equal(2, result.AppealsFiled);
        Assert.Equal(5, result.AppealableRejections);
        Assert.Equal(40m, result.AppealRate);
        Assert.Equal([("Upheld", 1), ("Dismissed", 1)], result.DecidedOutcomes.Select(o => (o.Key, o.Count)));
        Assert.Equal((2, 1, 1), (result.ObjectionsFiled, result.ObjectionsUpheld, result.ObjectionsDismissed));
    }

    // Offers created in the period: O1..O7 = 7; merit override O4 = 1 (14.3 %); exception O4 (merit) + O5 (grade) = 2 (28.6 %).
    [Fact]
    public async Task Merit_overrides_match_the_hand_calculation()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetMeritOverridesAsync(Period, Unrestricted);

        Assert.Equal(new MeritOverrideResult(7, 1, 2), result);
        Assert.Equal(14.3m, result.MeritOverrideRate);
        Assert.Equal(28.6m, result.ExceptionRate);
    }

    // Vacant without requisition: P7 (R6 cancelled 2026-04-10 → 202 days), P4 (never requested, created 2026-05-01 → 181);
    // P2 P5 P6 have open requisitions, P8 is frozen. Stale published (> 60 days): R2 since 2026-02-03 (268), R4 since
    // 2026-07-20 (101).
    [Fact]
    public async Task Open_vacancies_list_the_oldest_first()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetOpenVacanciesAsync(Period, Unrestricted);

        Assert.Equal(new DateOnly(2026, 10, 29), result.AsOf);
        Assert.Equal(2, result.VacantWithoutRequisitionCount);
        Assert.Equal([(P7, 202), (P4, 181)], result.VacantWithoutRequisition.Select(v => (v.FundedPositionId, v.DaysVacant)));
        Assert.Equal(2, result.StalePublishedCount);
        Assert.Equal([(R2, 268), (R4, 101)], result.StalePublished.Select(s => (s.RequisitionId, s.DaysPublished)));
    }

    // In progress: WI2 (R5), WI5 (O5), WI6 (competition) = 3. Overdue: WI6 676 h > 48 (default), WI2 196 h > 72 (option);
    // WI5 10 h < 24 (step timeout). Completed steps in the period: requisition L1 [24 (WI1), 24 (WI2)], L2 [48]; offer L1
    // [12 (WI3), 12 (WI4)].
    [Fact]
    public async Task Approval_sla_lists_overdue_steps_and_step_durations()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetApprovalSlaAsync(Period, Unrestricted);

        Assert.Equal(3, result.InProgressCount);
        Assert.Equal(2, result.OverdueCount);
        Assert.Equal(
            [("RECRUITMENT-COMPETITION-APPROVAL", "COMP-H32-1", 1, "لجنة المسابقة", "hr-core", 676m, 48, ApprovalSlaSource.Default),
             ("RECRUITMENT-REQUISITION-APPROVAL", "REQ-H32-5", 2, "المدير العام", "hr-management-approver", 196m, 72, ApprovalSlaSource.WorkflowOption)],
            result.Overdue.Select(o => (o.WorkflowCode, o.EntityLabel, o.StepLevel, o.StepName, o.RoleRequired, o.AgeHours, o.SlaHours, o.SlaSource)));
        Assert.Equal(
            [("RECRUITMENT-OFFER-APPROVAL", 1, 12m, 12m, 2), ("RECRUITMENT-REQUISITION-APPROVAL", 1, 24m, 24m, 2), ("RECRUITMENT-REQUISITION-APPROVAL", 2, 48m, 48m, 1)],
            result.CompletedSteps.Select(s => (s.WorkflowCode, s.StepLevel, s.MedianHours!.Value, s.P90Hours!.Value, s.N)));
    }

    // Competition COMP-H32-1: applicants D1–D6 = 6; admitted (Shortlisted) D1–D4 D6 = 5; present D1 D2 D3 D6 = 4 (D4
    // absent); passed D1 D2 D6 = 3; allocated (not from reserve) D1 = 1; reserve entries D2 D6 = 2; recalled D6 = 1;
    // objections filed D3 D4 = 2, upheld D4 = 1.
    [Fact]
    public async Task Competitions_are_counted_for_unrestricted_users()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var result = await service.GetCompetitionsAsync(Period, Unrestricted);

        Assert.True(result.Visible);
        var row = Assert.Single(result.Rows);
        Assert.Equal((6, 5, 4, 3, 1, 2, 1, 2, 1),
            (row.Applicants, row.Admitted, row.Present, row.Passed, row.Allocated, row.Reserve, row.Recalled, row.ObjectionsFiled, row.ObjectionsUpheld));
    }

    // Requisitions active in the period, by approval (else request) date: R1 01-10, R2 02-01, R3 03-04, R6 03-25, R4 07-05,
    // R5 10-20. R3: applicants 10, stages 10 8 6 5 5 5 3 1 1, time-to-fill 211, age 211 (approved → closed).
    [Fact]
    public async Task Requisition_table_shows_ages_stage_counts_and_time_to_fill()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var table = await service.GetRequisitionTableAsync(Period, Unrestricted);

        Assert.Equal(6, table.Total);
        Assert.Equal([R1, R2, R3, R6, R4, R5], table.Rows.Select(r => r.RequisitionId));
        var r3 = table.Rows.Single(r => r.RequisitionId == R3);
        Assert.Equal(10, r3.Applicants);
        Assert.Equal([10, 8, 6, 5, 5, 5, 3, 1, 1], r3.Stages.Select(s => s.Count));
        Assert.Equal(211, r3.TimeToFillDays);
        Assert.Equal(211, r3.AgeDays);
        Assert.Equal((41, 50), (table.Rows[0].TimeToFillDays, table.Rows[0].AgeDays));
        Assert.Equal((269, 270), (table.Rows[1].TimeToFillDays, table.Rows[1].AgeDays));
        Assert.Equal([6, 5, 10, 0, 2, 0], table.Rows.Select(r => r.Applicants));
    }

    // ------------------------------------------------------------------ scope (AC-HCM-32b)

    // Branch (unit 2): R3 apps B1–B10 and R4 apps D1 D6 = 12 (D2–D5 have no requisition). Furthest: B1 8, B2 2, B3 6,
    // B4 1, B5 1, B6 0, B7 5, B8 0, B9 5, B10 6, D1 5, D6 5 → 12, 10, 8, 7, 7, 7, 3, 1, 1; coverage 100 %.
    [Fact]
    public async Task Branch_scope_sees_only_its_unit()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var funnel = await service.GetFunnelAsync(Period, Branch);
        var offers = await service.GetOfferOutcomesAsync(Period, Branch);
        var fill = await service.GetTimeToFillAsync(Period, Branch);
        var vacancies = await service.GetOpenVacanciesAsync(Period, Branch);
        var sla = await service.GetApprovalSlaAsync(Period, Branch);
        var competitions = await service.GetCompetitionsAsync(Period, Branch);
        var table = await service.GetRequisitionTableAsync(Period, Branch);

        Assert.Equal([12, 10, 8, 7, 7, 7, 3, 1, 1], funnel.Stages.Select(s => s.Count));
        Assert.Equal(100m, funnel.CoveragePercent);
        Assert.Equal(new OfferOutcomeResult(1, 1, 1, 1), offers);
        Assert.Equal(new DurationMeasure(211m, 211m, 1, 0), fill.Days);
        Assert.Equal([P4], vacancies.VacantWithoutRequisition.Select(v => v.FundedPositionId));
        Assert.Equal([R4], vacancies.StalePublished.Select(s => s.RequisitionId));
        Assert.Equal((1, 0), (sla.InProgressCount, sla.OverdueCount));
        Assert.Equal([("RECRUITMENT-OFFER-APPROVAL", 1, 1), ("RECRUITMENT-REQUISITION-APPROVAL", 1, 1), ("RECRUITMENT-REQUISITION-APPROVAL", 2, 1)],
            sla.CompletedSteps.Select(s => (s.WorkflowCode, s.StepLevel, s.N)));
        Assert.False(competitions.Visible);
        Assert.Empty(competitions.Rows);
        Assert.Equal([R3, R4], table.Rows.Select(r => r.RequisitionId));
    }

    [Fact]
    public async Task Out_of_scope_org_unit_filter_returns_no_data()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;
        var filter = Period with { OrgUnitId = UnitHq };

        Assert.Equal(0, (await service.GetFunnelAsync(filter, Branch)).CohortCount);
        Assert.Equal(0, (await service.GetTimeToFillAsync(filter, Branch)).Days.N);
        Assert.Equal(0, (await service.GetOfferOutcomesAsync(filter, Branch)).Decided);
        Assert.Equal(0, (await service.GetOpenVacanciesAsync(filter, Branch)).StalePublishedCount);
        Assert.Equal(0, (await service.GetApprovalSlaAsync(filter, Branch)).InstancesExamined);
        Assert.Equal(0, (await service.GetRequisitionTableAsync(filter, Branch)).Total);
        Assert.Null(await service.GetRequisitionDetailAsync(R1, Branch, includeNames: false));
        Assert.Empty(await service.GetApplicationDetailsAsync(filter, Branch, includeNames: true, 100));
    }

    [Fact]
    public async Task Denied_scope_returns_no_data()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;
        var denied = OrgAccessScope.Denied();

        Assert.Equal(0, (await service.GetFunnelAsync(Period, denied)).CohortCount);
        Assert.Equal(0, (await service.GetChannelMixAsync(Period, denied)).CohortCount);
        Assert.Equal(0, (await service.GetAppealsAsync(Period, denied)).AppealableRejections);
        Assert.Equal(0, (await service.GetMeritOverridesAsync(Period, denied)).OffersCreated);
        Assert.Equal(0, (await service.GetTimeToHireAsync(Period, denied)).N);
        Assert.False((await service.GetCompetitionsAsync(Period, denied)).Visible);
    }

    [Fact]
    public async Task Competition_filter_from_a_scoped_user_returns_no_data()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var funnel = await service.GetFunnelAsync(Period with { CompetitionId = Competition }, Branch);
        var unrestricted = await service.GetFunnelAsync(Period with { CompetitionId = Competition }, Unrestricted);

        Assert.Equal(0, funnel.CohortCount);
        Assert.Equal(6, unrestricted.CohortCount);
    }

    // Unrestricted user filtering HQ: R1 A1–A6 and R2 C1–C5 = 11 applications.
    [Fact]
    public async Task Org_unit_filter_of_an_unrestricted_user_restricts_to_that_unit()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var funnel = await service.GetFunnelAsync(Period with { OrgUnitId = UnitHq }, Unrestricted);
        var competitions = await service.GetCompetitionsAsync(Period with { OrgUnitId = UnitHq }, Unrestricted);

        Assert.Equal(11, funnel.CohortCount);
        Assert.True(competitions.Visible);
        Assert.Empty(competitions.Rows);
    }

    [Fact]
    public async Task Channel_and_requisition_filters_narrow_the_cohort()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        Assert.Equal(6, (await service.GetFunnelAsync(Period with { Channel = RecruitmentIntakeChannel.Internal }, Unrestricted)).CohortCount);
        Assert.Equal(10, (await service.GetFunnelAsync(Period with { RequisitionId = R3 }, Unrestricted)).CohortCount);
        Assert.Equal([R3], (await service.GetRequisitionTableAsync(Period with { RequisitionId = R3 }, Unrestricted)).Rows.Select(r => r.RequisitionId));
    }

    // ------------------------------------------------------------------ names and references (decision 7)

    [Fact]
    public async Task Detail_rows_carry_names_only_when_asked_and_never_a_national_id()
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;

        var withNames = await service.GetApplicationDetailsAsync(Period, Unrestricted, includeNames: true, 100);
        var withoutNames = await service.GetApplicationDetailsAsync(Period, Unrestricted, includeNames: false, 100);
        var detail = await service.GetRequisitionDetailAsync(R3, Unrestricted, includeNames: false);

        Assert.Equal(27, withNames.Count);
        Assert.All(withNames, r => Assert.StartsWith(CandidateNamePrefix, r.CandidateName));
        Assert.All(withoutNames, r => Assert.Null(r.CandidateName));
        Assert.All(withoutNames, r => Assert.Matches("^(PUB|INT|HR|LEG)-[0-9A-F]{8}$", r.Reference));
        Assert.DoesNotContain(typeof(ApplicationDetailRow).GetProperties(), p => p.Name.Contains("National", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(detail);
        Assert.All(detail!.Offers, o => Assert.Null(o.CandidateName));
        // Offers of R3 by creation: O5 09-19, O3 09-21, O7 09-22, O4 09-27.
        Assert.Equal(["OFF-H32-O5", "OFF-H32-O3", "OFF-H32-O7", "OFF-H32-O4"], detail.Offers.Select(o => o.OfferNumber));
        Assert.Equal("الراتب", detail.Offers.Single(o => o.OfferNumber == "OFF-H32-O7").DeclineCategory);
        Assert.Equal(10, detail.Funnel.CohortCount);
        // B1 hired 2026-10-01 → time to hire on the detail rows.
        Assert.Equal(Utc(2026, 10, 1), withNames.Single(r => r.CandidateName == CandidateNamePrefix + "B1").HiredAt);
    }

    /// <summary>
    /// Regression (P7, found by the acceptance seed test): the workflow pack is published in the default tenant, so a
    /// tenant-scoped request must still resolve the recruitment definitions of its own (tenant-filtered) instances.
    /// </summary>
    [Fact]
    public async Task Approval_sla_resolves_definitions_of_the_default_tenant_under_a_tenant_context()
    {
        var (db, _) = await SeedAsync();
        await using var __ = db;
        await db.WorkflowDefinitions.ExecuteUpdateAsync(u => u.SetProperty(d => d.TenantId, HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId));
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(db.Database.GetDbConnection()).Options;
        await using var scoped = new HRCoreDbContext(options, new FixedTenant(Guid.Empty));

        var result = await CreateService(scoped).GetApprovalSlaAsync(Period, Unrestricted);

        Assert.Equal((3, 2), (result.InProgressCount, result.OverdueCount));
    }

    private sealed class FixedTenant(Guid tenantId) : HRManagement.SharedKernel.Tenancy.ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    /// <summary>
    /// Review F1: an appeal upheld through the real workflow handler clears the application's rejection; the denominator
    /// still counts that rejection (from its event). Four rejected, four appealed, two upheld → 4 / 4 = 100 % (the cleared
    /// columns alone gave 4 / 2 = 200 %).
    /// </summary>
    [Fact]
    public async Task Appeal_rate_counts_rejections_cleared_by_an_upheld_appeal()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var requisition = await Hcm027TestSupport.SeedRequisitionAsync(db, "F1");
        var apps = new List<HR.Core.Domain.Entities.RecruitmentApplication>();
        for (var i = 0; i < 4; i++)
            apps.Add(await Hcm027TestSupport.SeedApplicationAsync(db, requisition, St.Submitted, rank: null, name: "F1-" + i));
        foreach (var app in apps)
            HR.Core.Domain.Services.ApplicationRejection.Apply(app, RecruitmentRejectionKind.SelectionDecision, "قرار", DateTime.UtcNow);
        await db.SaveChangesAsync();

        var appeals = apps.Select(a => new HR.Core.Domain.Entities.RecruitmentAppeal
        {
            RecruitmentApplicationId = a.Id, Kind = RecruitmentAppealKind.Rejection, Reason = "استئناف للاختبار",
            Status = RecruitmentAppealStatus.Pending, CreatedAt = DateTime.UtcNow.AddSeconds(1)
        }).ToList();
        db.RecruitmentAppeals.AddRange(appeals);
        await db.SaveChangesAsync();
        var handler = new HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealWorkflowHandler(db);
        foreach (var appeal in appeals.Take(2))
        {
            appeal.Outcome = RecruitmentAppealOutcome.Upheld;
            appeal.Decision = "قبول الاستئناف";
            await handler.HandleAsync(new HR.Core.Domain.Entities.Workflow.WorkflowInstance
            {
                EntityType = "RecruitmentAppeal", EntityId = appeal.Id, StepHistoryJson = "[]"
            }, approved: true, reason: null);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.RecruitmentApplications.CountAsync(a => a.RejectedAt == null && a.RequisitionId == requisition.Id));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await CreateService(db).GetAppealsAsync(new RecruitmentMetricsFilter { From = today.AddDays(-1), To = today.AddDays(2) }, Unrestricted);

        Assert.Equal((4, 4, 100m), (result.AppealsFiled, result.AppealableRejections, result.AppealRate!.Value));
    }

    /// <summary>Review F2: "published for more than N days" counts calendar days (UTC dates), the unit the list shows.</summary>
    [Theory]
    [InlineData(2026, 8, 30, 8, false)]  // 60 calendar days before 2026-10-29 (instant gap 60 d 4 h — the old instant rule listed it) → not stale
    [InlineData(2026, 8, 29, 23, true)]  // 61 calendar days → stale
    public async Task Stale_published_boundary_is_in_calendar_days(int y, int m, int d, int h, bool stale)
    {
        var (db, service) = await SeedAsync();
        await using var _ = db;
        await db.RecruitmentRequisitions.Where(r => r.Id == R2)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.PublishedAt, new DateTime(y, m, d, h, 0, 0, DateTimeKind.Utc)));

        var result = await service.GetOpenVacanciesAsync(Period, Unrestricted);

        Assert.Equal(stale, result.StalePublished.Any(s => s.RequisitionId == R2));
        Assert.All(result.StalePublished, s => Assert.True(s.DaysPublished > result.StalePublishedDays));
    }

    [Fact]
    public void Default_period_is_the_last_twelve_full_months()
    {
        var filter = RecruitmentMetricsFilter.DefaultPeriod(new DateTime(2026, 10, 29, 23, 0, 0, DateTimeKind.Utc), 12);
        Assert.Equal((new DateOnly(2025, 10, 1), new DateOnly(2026, 10, 1)), (filter.From, filter.To));
    }
}
