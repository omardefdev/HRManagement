using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-027 (F): reserve list — ranked siblings reserved on hire (D-7), next candidate by merit, optional
/// auto-draft of the next offer, reserve expiry.
/// </summary>
public class Hcm027ReserveListTests
{
    // ---------------------------------------------------------------- F2: hire closure

    [Fact]
    public async Task Hire_moves_ranked_siblings_to_reserve_and_rejects_other_open_siblings()
    {
        await using var db = CreateContext();
        var (winner, req) = await SeedAcceptedOfferAsync(db);
        var rank2 = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2, "الثاني");
        var rank3 = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 3, "الثالث");
        var assessed = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Assessed, null, "مُقيَّم");
        var shortlisted = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Shortlisted, null, "مختصر");
        var declined = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.OfferDeclined, 4, "رافض");
        var withdrawn = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Withdrawn, null, "منسحب");
        var earlierReserve = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 5, "احتياط سابق");
        earlierReserve.ReserveUntil = new DateOnly(2027, 1, 31);
        await db.SaveChangesAsync();

        await HireAsync(db, winner, HireConfig(rejectSiblings: true));

        db.ChangeTracker.Clear();
        var apps = await db.RecruitmentApplications.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var expectedUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6);
        foreach (var reserved in new[] { rank2, rank3 })
        {
            var saved = apps[reserved.Id];
            Assert.Equal(RecruitmentApplicationStatus.ReserveListed, saved.Status);
            Assert.Equal(expectedUntil, saved.ReserveUntil);
            Assert.NotNull(saved.ReserveListedAt);
            Assert.Contains(ReserveListService.MovedToReserveNote, saved.Notes);
        }

        Assert.Equal(RecruitmentApplicationStatus.Rejected, apps[assessed.Id].Status);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, apps[shortlisted.Id].Status);
        Assert.Null(apps[assessed.Id].ReserveUntil);
        Assert.Equal(RecruitmentApplicationStatus.OfferDeclined, apps[declined.Id].Status);
        Assert.Equal(RecruitmentApplicationStatus.Withdrawn, apps[withdrawn.Id].Status);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, apps[earlierReserve.Id].Status);
        Assert.Equal(new DateOnly(2027, 1, 31), apps[earlierReserve.Id].ReserveUntil);
        Assert.Equal(RecruitmentApplicationStatus.Hired, apps[winner.Id].Status);
    }

    [Fact]
    public async Task Hire_reserve_validity_follows_option()
    {
        await using var db = CreateContext();
        var (winner, req) = await SeedAcceptedOfferAsync(db);
        var rank2 = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);

        await HireAsync(db, winner, HireConfig(rejectSiblings: true), new RecruitmentOptions { ReserveValidityMonths = 3 });

        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == rank2.Id);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3), saved.ReserveUntil);
    }

    [Fact]
    public async Task Hire_with_sibling_handling_off_leaves_siblings_unchanged()
    {
        await using var db = CreateContext();
        var (winner, req) = await SeedAcceptedOfferAsync(db);
        var rank2 = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);
        var assessed = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Assessed, null);

        await HireAsync(db, winner, HireConfig(rejectSiblings: false));

        db.ChangeTracker.Clear();
        var savedRank2 = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == rank2.Id);
        var savedAssessed = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == assessed.Id);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, savedRank2.Status);
        Assert.Null(savedRank2.ReserveUntil);
        Assert.Null(savedRank2.ReserveListedAt);
        Assert.Null(savedRank2.ReviewedAt);
        Assert.Equal(RecruitmentApplicationStatus.Assessed, savedAssessed.Status);
    }

    // ---------------------------------------------------------------- F1/F3: next candidate

    [Fact]
    public async Task Next_candidate_skips_declined_rejected_pending_appeal_and_unranked()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.OfferDeclined, 1);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Rejected, 2);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 3, appeal: RecruitmentAppealStatus.Pending);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Assessed, null);
        var expected = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 4, "الرابع");
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 5);

        var next = await CreateReserveList(db).GetNextCandidateAsync(req.Id);

        Assert.Equal(expected.Id, next?.Id);
        Assert.Equal(4, await CreateOfferService(db).GetBestAvailableRankAsync(req.Id));
    }

    [Fact]
    public async Task Suggestion_appears_only_after_a_settled_offer_on_a_published_requisition()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var first = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1, "الأول");
        var second = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2, "الثاني");
        var reserveList = CreateReserveList(db);

        Assert.Null(await reserveList.GetNextCandidateSuggestionAsync(req.Id));

        var offer = await IssueOfferAsync(db, first.Id);
        Assert.Null(await reserveList.GetNextCandidateSuggestionAsync(req.Id));

        var (declined, error) = await CreateOfferService(db).DeclineAsync(offer.Id, "قبل عرضاً آخر", OfferDeclinedBy.HrOnBehalf);
        Assert.True(declined, error);

        var suggestion = await reserveList.GetNextCandidateSuggestionAsync(req.Id);
        Assert.NotNull(suggestion);
        Assert.Equal(second.Id, suggestion!.ApplicationId);
        Assert.Equal("الثاني", suggestion.ApplicantFullName);
        Assert.Equal(2, suggestion.Rank);

        var tracked = await db.RecruitmentRequisitions.SingleAsync(r => r.Id == req.Id);
        tracked.Status = RequisitionStatus.Closed;
        await db.SaveChangesAsync();
        Assert.Null(await reserveList.GetNextCandidateSuggestionAsync(req.Id));
    }

    [Fact]
    public async Task Auto_draft_creates_next_offer_that_passes_eligibility_with_previous_terms()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var first = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        var second = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);
        var service = CreateOfferService(db, options: new RecruitmentOptions { AutoDraftNextOffer = true });
        var offer = await IssueOfferAsync(db, first.Id, service, validityDays: 10, salary: 5100);
        Assert.True((await service.DeclineAsync(offer.Id, "ظروف عائلية", OfferDeclinedBy.HrOnBehalf)).Success);

        var (draft, error) = await service.TryAutoDraftNextOfferAsync(offer.Id, CreatorId);

        Assert.True(draft is not null, error);
        Assert.Equal(second.Id, draft!.RecruitmentApplicationId);
        Assert.Equal(OfferStatus.Draft, draft.Status);
        Assert.False(draft.IsMeritOverride);
        Assert.Equal("G5", draft.GradeCode);
        Assert.Equal(5100m, draft.OfferedSalary);
        Assert.Equal(10, draft.ValidityDays);
        db.ChangeTracker.Clear();
        var savedSecond = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == second.Id);
        Assert.Equal(RecruitmentApplicationStatus.OfferExtended, savedSecond.Status);
        Assert.Equal(draft.Id, savedSecond.ActiveOfferLetterId);
    }

    [Fact]
    public async Task Auto_draft_is_off_by_default()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var first = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);
        var service = CreateOfferService(db);
        var offer = await IssueOfferAsync(db, first.Id, service);
        Assert.True((await service.DeclineAsync(offer.Id, "ظروف عائلية", OfferDeclinedBy.HrOnBehalf)).Success);

        var (draft, error) = await service.TryAutoDraftNextOfferAsync(offer.Id, CreatorId);

        Assert.Null(draft);
        Assert.Null(error);
        Assert.Equal(1, await db.OfferLetters.CountAsync());
    }

    [Fact]
    public async Task Auto_draft_skips_administrative_revoke_that_returns_candidate_to_ranked()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var first = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);
        var service = CreateOfferService(db, options: new RecruitmentOptions { AutoDraftNextOffer = true });
        var offer = await IssueOfferAsync(db, first.Id, service);
        Assert.True((await service.RevokeAsync(offer.Id, "خطأ في الراتب المدخل", revertToRanked: true)).Success);

        var (draft, _) = await service.TryAutoDraftNextOfferAsync(offer.Id, CreatorId);

        Assert.Null(draft);
    }

    [Fact]
    public async Task Offer_expiry_job_auto_drafts_next_offer_when_enabled()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var first = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        var second = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2);
        var service = CreateOfferService(db, options: new RecruitmentOptions { AutoDraftNextOffer = true });
        var offer = await IssueOfferAsync(db, first.Id, service);
        offer.ExpiryDate = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var expired = await new RecruitmentOfferExpirer(db, service, NullLogger<RecruitmentOfferExpirer>.Instance)
            .ExpireDueAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, expired);
        db.ChangeTracker.Clear();
        var draft = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.RecruitmentApplicationId == second.Id);
        Assert.Equal(OfferStatus.Draft, draft.Status);
        Assert.Equal(OfferStatus.Expired, (await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).Status);
    }

    // ---------------------------------------------------------------- F4: reserve expiry

    [Fact]
    public async Task Reserve_expiry_rejects_past_reserve_dates_and_is_idempotent()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expired = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 2);
        expired.ReserveUntil = today.AddDays(-1);
        var lastDay = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 3);
        lastDay.ReserveUntil = today;
        var noDate = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 4);
        await db.SaveChangesAsync();
        var expirer = new RecruitmentReserveExpirer(CreateReserveList(db));

        Assert.Equal(1, await expirer.ExpireDueAsync(today, CancellationToken.None));
        Assert.Equal(0, await expirer.ExpireDueAsync(today, CancellationToken.None));

        db.ChangeTracker.Clear();
        var apps = await db.RecruitmentApplications.AsNoTracking().ToDictionaryAsync(a => a.Id);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, apps[expired.Id].Status);
        Assert.Equal(ReserveListService.ReserveExpiryNote, apps[expired.Id].Notes);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, apps[lastDay.Id].Status);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, apps[noDate.Id].Status);
    }

    // ---------------------------------------------------------------- helpers

    private static ReserveListService CreateReserveList(HRCoreDbContext db, RecruitmentOptions? options = null) =>
        new(db, Microsoft.Extensions.Options.Options.Create(options ?? new RecruitmentOptions()), NullLogger<ReserveListService>.Instance);

    private static IConfiguration HireConfig(bool rejectSiblings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:RejectSiblingApplicationsOnHire"] = rejectSiblings.ToString(),
                ["Recruitment:LegacyHireWithoutOffer"] = "false"
            })
            .Build();

    /// <summary>Drafts and issues an offer (Draft → Issued with expiry from the validity).</summary>
    private static async Task<OfferLetter> IssueOfferAsync(
        HRCoreDbContext db,
        Guid applicationId,
        RecruitmentOfferService? service = null,
        int? validityDays = null,
        decimal salary = 5000)
    {
        var (offer, error) = await (service ?? CreateOfferService(db)).CreateDraftAsync(
            applicationId, salary, "G5", DateTime.UtcNow.Date.AddDays(30), validityDays, CreatorId);
        Assert.True(offer is not null, error);
        OfferWorkflowRules.MarkIssued(offer!, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return offer!;
    }

    private static async Task<(RecruitmentApplication Winner, RecruitmentRequisition Requisition)> SeedAcceptedOfferAsync(HRCoreDbContext db)
    {
        await LegislationRegisterSeeder.SeedAsync(db);
        await ServiceRegimeSeeder.SeedAsync(db, new LegislationRegisterProvider(db));
        var req = await SeedRequisitionAsync(db);
        var winner = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.OfferAccepted, 1, "الفائز");
        winner.NationalId = "0127270001";
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = winner.Id,
            FundedPositionId = req.FundedPositionId,
            OfferNumber = "OFF-H27-HIRE",
            Status = OfferStatus.Accepted,
            OfferedSalary = 5000,
            GradeCode = "G5",
            ProposedStartDate = DateTime.UtcNow.AddDays(30),
            AcceptedDate = DateTime.UtcNow,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        winner.ActiveOfferLetterId = offer.Id;
        await db.SaveChangesAsync();
        return (winner, req);
    }

    private static async Task HireAsync(
        HRCoreDbContext db,
        RecruitmentApplication winner,
        IConfiguration config,
        RecruitmentOptions? options = null)
    {
        var legislationProvider = new LegislationRegisterProvider(db);
        var legalValidator = new RecruitmentLegalSourceValidator(legislationProvider);
        var regimeValidator = new ServiceRegimeValidator(db, legalValidator);
        var exceptionService = new ServiceRegimeExceptionService(
            db, regimeValidator, new ServiceRegimeTransitionService(db, regimeValidator));
        var closure = new RecruitmentHireClosureService(
            db,
            new EstablishmentService(),
            new FundedPositionVersionService(db),
            new RequisitionCloseService(),
            legalValidator,
            regimeValidator,
            exceptionService,
            CreateReserveList(db, options),
            Mock.Of<IPublishEndpoint>(),
            config,
            NullLogger<RecruitmentHireClosureService>.Instance);

        await closure.HireAndCreateEmployeeAsync(
            winner,
            winner.NationalId!,
            "فائز",
            null,
            "الجدارة",
            DateTime.UtcNow.AddYears(-30),
            Gender.Male,
            primaryOrgUnitId: null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            "PERMANENT");
    }
}
