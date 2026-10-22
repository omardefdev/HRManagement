using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 (G): every rejection records its kind and date; the appeal window and eligibility use them.</summary>
public class Hcm028RejectionTrackingTests
{
    private static async Task<RecruitmentApplication> ReloadAppAsync(HRCoreDbContext db, Guid id)
    {
        db.ChangeTracker.Clear();
        return await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private static void AssertRejected(RecruitmentApplication app, RecruitmentRejectionKind kind)
    {
        Assert.Equal(RecruitmentApplicationStatus.Rejected, app.Status);
        Assert.Equal(kind, app.RejectionKind);
        Assert.NotNull(app.RejectedAt);
        Assert.True(DateTime.UtcNow - app.RejectedAt!.Value < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Manual_rejection_records_selection_decision_note_and_rank()
    {
        await using var db = CreateContext();
        var (_, app) = await Hcm028AppealTestSupport.SeedRejectedAsync(db, rank: 2);

        var saved = await ReloadAppAsync(db, app.Id);
        AssertRejected(saved, RecruitmentRejectionKind.SelectionDecision);
        Assert.Equal("لم يستوفِ شرط الخبرة", saved.RejectionNote);
        Assert.Equal(2, saved.RankBeforeRejection);
    }

    [Fact]
    public async Task Requisition_cancellation_records_requisition_cancelled()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, "RJC");
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Shortlisted, null);

        var (ok, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false)
            .CancelAsync(req.Id, "إعادة هيكلة المديرية وإلغاء الوظيفة", Guid.NewGuid(), actorCanApprove: true);

        Assert.True(ok, error);
        var saved = await ReloadAppAsync(db, app.Id);
        AssertRejected(saved, RecruitmentRejectionKind.RequisitionCancelled);
        Assert.StartsWith("إلغاء الشاغر", saved.RejectionNote);
    }

    [Fact]
    public async Task Requisition_close_records_requisition_closed()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, "RJL");
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.UnderReview, null);

        var (ok, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false).CloseAsync(req.Id);

        Assert.True(ok, error);
        AssertRejected(await ReloadAppAsync(db, app.Id), RecruitmentRejectionKind.RequisitionClosed);
    }

    [Fact]
    public async Task Hire_rejects_unranked_siblings_as_not_selected_on_hire()
    {
        await using var db = CreateContext();
        var (winner, req) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var assessed = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Assessed, null, "مُقيَّم");

        await Hcm027ReserveListTests.HireAsync(db, winner, Hcm027ReserveListTests.HireConfig(rejectSiblings: true));

        var saved = await ReloadAppAsync(db, assessed.Id);
        AssertRejected(saved, RecruitmentRejectionKind.NotSelectedOnHire);
        Assert.Equal(ReserveListService.NotSelectedOnHireNote, saved.RejectionNote);
    }

    [Fact]
    public async Task Reserve_expiry_records_reserve_expired()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, "RJR");
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 2);
        app.ReserveUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await db.SaveChangesAsync();

        Assert.Equal(1, await Hcm027ReserveListTests.CreateReserveList(db).ExpireReserveAsync(DateOnly.FromDateTime(DateTime.UtcNow)));

        var saved = await ReloadAppAsync(db, app.Id);
        AssertRejected(saved, RecruitmentRejectionKind.ReserveExpired);
        Assert.Equal(2, saved.RankBeforeRejection);
    }

    [Fact]
    public async Task Offer_revoke_without_revert_records_offer_revoked()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db, "RJO");
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);
        var service = CreateOfferService(db);
        var (offer, draftError) = await service.CreateDraftAsync(app.Id, 5000, "G5", DateTime.UtcNow.Date.AddDays(30), null, CreatorId);
        Assert.True(offer is not null, draftError);

        var (ok, error) = await service.RevokeAsync(offer!.Id, "تبيّن عدم استيفاء شرط الشهادة", revertToRanked: false, CreatorId);

        Assert.True(ok, error);
        var saved = await ReloadAppAsync(db, app.Id);
        AssertRejected(saved, RecruitmentRejectionKind.OfferRevoked);
        Assert.Contains("تبيّن عدم استيفاء شرط الشهادة", saved.RejectionNote);
    }

    [Fact]
    public void Window_runs_from_rejected_at_not_reviewed_at()
    {
        var now = DateTime.UtcNow;
        var app = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Rejected };
        ApplicationRejection.Apply(app, RecruitmentRejectionKind.SelectionDecision, null, now.AddDays(-15));
        app.ReviewedAt = now; // a later operation overwrote ReviewedAt

        Assert.Equal(RecruitmentAppealWorkflowRules.WindowExpiredMessage, RecruitmentAppealWorkflowRules.GetAppealError(app, now));
        Assert.Null(RecruitmentAppealWorkflowRules.GetAppealError(app, now, appealWindowDays: 20));

        app.RejectedAt = now.AddDays(-13);
        Assert.True(RecruitmentAppealWorkflowRules.CanAppeal(app, now));
    }

    [Fact]
    public void Legacy_rows_use_reviewed_at_and_count_as_selection_decision()
    {
        var now = DateTime.UtcNow;
        var legacy = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Rejected, ReviewedAt = now.AddDays(-5) };
        Assert.True(RecruitmentAppealWorkflowRules.CanAppeal(legacy, now));
        Assert.Equal(RecruitmentRejectionKind.SelectionDecision, RecruitmentAppealWorkflowRules.EffectiveRejectionKind(legacy));

        legacy.ReviewedAt = now.AddDays(-15);
        Assert.False(RecruitmentAppealWorkflowRules.CanAppeal(legacy, now));
    }

    [Theory]
    [InlineData(RecruitmentRejectionKind.RequisitionCancelled)]
    [InlineData(RecruitmentRejectionKind.RequisitionClosed)]
    [InlineData(RecruitmentRejectionKind.ReserveExpired)]
    public void Non_appealable_kinds_are_refused(RecruitmentRejectionKind kind)
    {
        var app = new RecruitmentApplication();
        ApplicationRejection.Apply(app, kind, null, DateTime.UtcNow);

        Assert.Equal(RecruitmentAppealWorkflowRules.KindNotAppealableMessage, RecruitmentAppealWorkflowRules.GetAppealError(app, DateTime.UtcNow));
        // Configurable: Recruitment:AppealableRejectionKinds replaces the default list.
        Assert.Null(RecruitmentAppealWorkflowRules.GetAppealError(app, DateTime.UtcNow, appealableKinds: [kind]));
    }

    [Theory]
    [InlineData(RecruitmentRejectionKind.SelectionDecision)]
    [InlineData(RecruitmentRejectionKind.NotSelectedOnHire)]
    [InlineData(RecruitmentRejectionKind.OfferRevoked)]
    public void Default_appealable_kinds_are_accepted(RecruitmentRejectionKind kind)
    {
        var app = new RecruitmentApplication();
        ApplicationRejection.Apply(app, kind, null, DateTime.UtcNow);
        Assert.True(RecruitmentAppealWorkflowRules.CanAppeal(app, DateTime.UtcNow));
    }

    [Fact]
    public async Task Filing_uses_configured_window_and_kinds()
    {
        await using var db = CreateContext();
        var (_, app) = await Hcm028AppealTestSupport.SeedRejectedAsync(db);
        app.RejectedAt = DateTime.UtcNow.AddDays(-10);
        await db.SaveChangesAsync();

        var shortWindow = Hcm028AppealTestSupport.CreateService(db, options: new RecruitmentOptions { AppealWindowDays = 7 });
        var (appeal, error) = await shortWindow.FileAppealAsync(app.Id, Hcm028AppealTestSupport.Reason, "HrOnBehalf", Hcm028AppealTestSupport.HrUserId);
        Assert.Null(appeal);
        Assert.Equal(RecruitmentAppealWorkflowRules.WindowExpiredMessage, error);

        var noSelectionKind = Hcm028AppealTestSupport.CreateService(db, options: new RecruitmentOptions
        {
            AppealableRejectionKinds = [RecruitmentRejectionKind.OfferRevoked]
        });
        (appeal, error) = await noSelectionKind.FileAppealAsync(app.Id, Hcm028AppealTestSupport.Reason, "HrOnBehalf", Hcm028AppealTestSupport.HrUserId);
        Assert.Null(appeal);
        Assert.Equal("لا يقبل هذا النوع من الرفض الاستئناف.", error);
    }

    [Fact]
    public void Options_binding_replaces_default_kinds()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Recruitment:AppealableRejectionKinds:0"] = "ReserveExpired" })
            .Build();
        var options = config.GetSection(RecruitmentOptions.SectionName).Get<RecruitmentOptions>()!;

        Assert.Equal([RecruitmentRejectionKind.ReserveExpired], options.GetAppealableRejectionKinds());
        Assert.Equal(RecruitmentAppealWorkflowRules.DefaultAppealableKinds, new RecruitmentOptions().GetAppealableRejectionKinds());
        Assert.Equal(14, new RecruitmentOptions().AppealWindowDays);
    }
}
