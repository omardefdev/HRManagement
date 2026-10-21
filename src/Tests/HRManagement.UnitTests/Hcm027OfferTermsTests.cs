using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-027 (E): offer terms — grade pre-filled from the funded position (G5 in the fixtures), salary within the
/// Payroll grade bounds, start date window; grade deviation and unverified salary make an exception offer.
/// </summary>
public class Hcm027OfferTermsTests
{
    private const string DeviationReason = "ترفيع استثنائي بقرار اللجنة";
    private const string SalaryJustification = "الراتب وفق قرار التعيين المعتمد للدرجة الخامسة";

    private static readonly PayrollGradeLookupResult G5Bounds =
        new PayrollGradeLookupResult.Found(4000m, 6000m, 4500m, "الدرجة الخامسة");

    private static DateTime StartIn30Days => DateTime.UtcNow.Date.AddDays(30);

    [Fact]
    public async Task Grade_is_prefilled_from_funded_position_and_bounds_are_snapshotted()
    {
        await using var db = CreateContext();
        var lookup = new FakePayrollGradeLookup { Result = G5Bounds };
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db, gradeLookup: lookup)
            .CreateDraftAsync(app.Id, 5000, gradeCode: null, StartIn30Days, null);

        Assert.True(offer is not null, error);
        Assert.Equal("G5", offer!.GradeCode);
        Assert.Equal(("G5", DateOnly.FromDateTime(StartIn30Days)), Assert.Single(lookup.Calls));
        Assert.Equal(4000m, offer.GradeMinSalary);
        Assert.Equal(6000m, offer.GradeMaxSalary);
        Assert.NotNull(offer.SalaryCheckedAt);
        Assert.False(offer.SalaryUnverified);
        Assert.False(offer.IsExceptionOffer);
    }

    [Fact]
    public async Task Grade_deviation_without_reason_is_rejected()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db, gradeLookup: new FakePayrollGradeLookup { Result = G5Bounds })
            .CreateDraftAsync(app.Id, 5000, "G6", StartIn30Days, null, gradeDeviationReason: "قصير");

        Assert.Null(offer);
        Assert.Equal(OfferTermsRules.GradeDeviationReasonMessage, error);
    }

    [Fact]
    public async Task Grade_deviation_with_reason_is_an_exception_offer_with_override_workflow()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);
        var service = CreateOfferService(db, gradeLookup: new FakePayrollGradeLookup { Result = G5Bounds });

        var (offer, error) = await service.CreateDraftAsync(
            app.Id, 5000, "G6", StartIn30Days, null, CreatorId, gradeDeviationReason: "  " + DeviationReason + " ");
        Assert.True(offer is not null, error);
        Assert.Equal("G6", offer!.GradeCode);
        Assert.Equal(DeviationReason, offer.GradeDeviationReason);
        Assert.False(offer.IsMeritOverride);
        Assert.True(offer.IsExceptionOffer);

        var submit = await service.SubmitForApprovalAsync(offer.Id, CreatorId, "creator");
        Assert.True(submit.Success, submit.Error);

        db.ChangeTracker.Clear();
        var saved = await db.OfferLetters.SingleAsync();
        Assert.Equal(OfferOverrideApprovalRules.WorkflowCode, await GetWorkflowCodeAsync(db, saved.WorkflowInstanceId));
    }

    [Fact]
    public async Task Same_grade_in_other_case_is_not_a_deviation()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db, gradeLookup: new FakePayrollGradeLookup { Result = G5Bounds })
            .CreateDraftAsync(app.Id, 5000, " g5 ", StartIn30Days, null);

        Assert.True(offer is not null, error);
        Assert.Null(offer!.GradeDeviationReason);
        Assert.False(offer.IsExceptionOffer);
    }

    [Theory]
    [InlineData(3999.99)]
    [InlineData(6000.01)]
    public async Task Salary_outside_grade_bounds_is_rejected_with_bounds_message(double salary)
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db, gradeLookup: new FakePayrollGradeLookup { Result = G5Bounds })
            .CreateDraftAsync(app.Id, (decimal)salary, "G5", StartIn30Days, null);

        Assert.Null(offer);
        Assert.Equal("الراتب المعروض خارج حدود الدرجة G5: 4,000–6,000.", error);
        await AssertApplicationStillRankedAsync(db, app.Id);
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(6000)]
    public async Task Salary_on_the_bounds_is_accepted(int salary)
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db, gradeLookup: new FakePayrollGradeLookup { Result = G5Bounds })
            .CreateDraftAsync(app.Id, salary, "G5", StartIn30Days, null);

        Assert.True(offer is not null, error);
    }

    [Fact]
    public async Task Grade_not_in_payroll_scale_is_rejected()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db, gradeLookup: new FakePayrollGradeLookup { Result = new PayrollGradeLookupResult.NotFound() })
            .CreateDraftAsync(app.Id, 5000, "G5", StartIn30Days, null);

        Assert.Null(offer);
        Assert.Equal("الدرجة G5 غير معرّفة في سلّم الرواتب الساري.", error);
    }

    [Fact]
    public async Task Payroll_unavailable_without_justification_is_rejected()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db, gradeLookup: new UnavailablePayrollGradeLookup())
            .CreateDraftAsync(app.Id, 5000, "G5", StartIn30Days, null, salaryJustification: "مبرر قصير");

        Assert.Null(offer);
        Assert.Equal(OfferTermsRules.SalaryJustificationMessage, error);
    }

    [Fact]
    public async Task Payroll_unavailable_with_justification_is_unverified_exception_offer()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);
        var service = CreateOfferService(db, gradeLookup: new UnavailablePayrollGradeLookup());

        var (offer, error) = await service.CreateDraftAsync(
            app.Id, 5000, "G5", StartIn30Days, null, CreatorId, salaryJustification: SalaryJustification);
        Assert.True(offer is not null, error);
        Assert.True(offer!.SalaryUnverified);
        Assert.Equal(SalaryJustification, offer.SalaryJustification);
        Assert.Null(offer.GradeMinSalary);
        Assert.Null(offer.SalaryCheckedAt);
        Assert.True(offer.IsExceptionOffer);

        var submit = await service.SubmitForApprovalAsync(offer.Id, CreatorId, "creator");
        Assert.True(submit.Success, submit.Error);

        db.ChangeTracker.Clear();
        var saved = await db.OfferLetters.SingleAsync();
        Assert.Equal(OfferOverrideApprovalRules.WorkflowCode, await GetWorkflowCodeAsync(db, saved.WorkflowInstanceId));
    }

    [Fact]
    public async Task Start_date_before_today_is_rejected()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db)
            .CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.Date.AddDays(-1), null);

        Assert.Null(offer);
        Assert.Equal(OfferTermsRules.StartDateInPastMessage, error);
    }

    [Fact]
    public async Task Start_date_after_max_start_days_is_rejected_and_boundary_is_accepted()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);
        var service = CreateOfferService(db, options: new RecruitmentOptions { MaxStartDays = 90 });

        var (late, lateError) = await service.CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.Date.AddDays(91), null);
        Assert.Null(late);
        Assert.Equal(OfferTermsRules.StartDateTooLateMessage(90), lateError);

        var (today, todayError) = await service.CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.Date.AddDays(90), null);
        Assert.True(today is not null, todayError);
    }

    [Fact]
    public async Task Start_date_today_is_accepted()
    {
        await using var db = CreateContext();
        var app = await SeedFirstRankedAsync(db);

        var (offer, error) = await CreateOfferService(db).CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.Date, null);

        Assert.True(offer is not null, error);
    }

    [Theory]
    [InlineData(false, false, "RECRUITMENT-OFFER-APPROVAL")]
    [InlineData(false, true, "RECRUITMENT-OFFER-OVERRIDE-APPROVAL")]
    [InlineData(true, false, "RECRUITMENT-OFFER-OVERRIDE-APPROVAL")]
    public void Static_fallback_selects_override_workflow_for_any_exception_offer(
        bool isMeritOverride, bool isExceptionOffer, string expected)
    {
        var request = PolicyDomainFactsBuilders.ForOfferLetter(
            Guid.NewGuid(), Guid.NewGuid(), "Issue", Guid.NewGuid(),
            isMeritOverride: isMeritOverride, isExceptionOffer: isExceptionOffer);

        Assert.Equal(expected, PolicyStaticFallback.Evaluate(request).GetWorkflowCode());
    }

    [Fact]
    public async Task Seeded_rules_select_override_workflow_for_grade_deviation_in_enforce_mode()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db);
        await PolicyEngineDomainSeeds.SeedIncrementalRulesAsync(db);
        var req = await SeedRequisitionAsync(db);
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        var service = CreateOfferService(
            db, Config(policyMode: "Enforce"), CreateOrchestrator(db),
            gradeLookup: new FakePayrollGradeLookup { Result = G5Bounds });

        var (offer, error) = await service.CreateDraftAsync(
            app.Id, 5000, "G6", StartIn30Days, null, CreatorId, gradeDeviationReason: DeviationReason);
        Assert.True(offer is not null, error);
        var submit = await service.SubmitForApprovalAsync(offer!.Id, CreatorId, "creator");
        Assert.True(submit.Success, submit.Error);

        db.ChangeTracker.Clear();
        var saved = await db.OfferLetters.SingleAsync();
        Assert.Equal(OfferOverrideApprovalRules.WorkflowCode, await GetWorkflowCodeAsync(db, saved.WorkflowInstanceId));
        var decision = await db.PolicyEnforcementLedgers.AsNoTracking().Select(l => l.DecisionJson).SingleAsync();
        Assert.Contains("R-OFF-EXCEPTION", decision);
        Assert.DoesNotContain("fallback", decision);
    }

    private static async Task<HR.Core.Domain.Entities.RecruitmentApplication> SeedFirstRankedAsync(HRCoreDbContext db)
    {
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        return await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
    }

    private static async Task AssertApplicationStillRankedAsync(HRCoreDbContext db, Guid applicationId)
    {
        db.ChangeTracker.Clear();
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, app.Status);
        Assert.Null(app.ActiveOfferLetterId);
    }
}
