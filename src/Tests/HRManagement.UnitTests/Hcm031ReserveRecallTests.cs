using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31, P5, AC-HCM-31g, decision 8): line-level reserve recall within validity, and expiry.</summary>
public sealed class Hcm031ReserveRecallTests
{
    /// <summary>L1 (3) gets c2, c3, c4; L2 (2) gets c0, c1; c5 is on both reserves (rank 1), c6 on L2's (rank 2).</summary>
    private static async Task<(MarkedCompetition Fx, CompetitionLine L1, CompetitionLine L2)> SeedFinalizedAsync(HRCoreDbContext db)
    {
        var fx = await SeedMarkedAsync(db, [90m, 85m, 80m, 75m, 70m, 65m, 60m]);
        var lines = fx.Competition.Lines.OrderBy(l => l.LineNo).ToList();
        var (l1, l2) = (lines[0], lines[1]);
        var a = fx.Applications;
        await SetPreferencesAsync(db, a[0], l2.Id);
        await SetPreferencesAsync(db, a[1], l2.Id);
        await SetPreferencesAsync(db, a[2], l1.Id);
        await SetPreferencesAsync(db, a[3], l1.Id);
        await SetPreferencesAsync(db, a[4], l1.Id);
        await SetPreferencesAsync(db, a[5], l2.Id, l1.Id);
        await SetPreferencesAsync(db, a[6], l2.Id);
        await PublishAndCloseWindowAsync(db, fx.Competition.Id);
        var result = await CreateAllocation(db).FinalizeAsync(fx.Competition.Id, Creator);
        Assert.True(result.Success, result.Error);
        return (fx, l1, l2);
    }

    private static async Task<OfferLetter> IssueOfferAsync(HRCoreDbContext db, RecruitmentOfferService offers, Guid applicationId)
    {
        var (offer, error) = await offers.CreateDraftAsync(applicationId, 600_000m, null, DateTime.UtcNow.Date.AddDays(20), null, Creator);
        Assert.True(offer is not null, error);
        var tracked = await db.OfferLetters.SingleAsync(o => o.Id == offer!.Id);
        tracked.Status = OfferStatus.Issued;
        tracked.IssuedDate = DateTime.UtcNow;
        tracked.ExpiryDate = DateTime.UtcNow.AddDays(14);
        await db.SaveChangesAsync();
        return tracked;
    }

    [Fact]
    public async Task A_declined_offer_recalls_the_first_reserve_of_the_same_line_AC_HCM_31g()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, l1, l2) = await SeedFinalizedAsync(db);
        var offers = CreateOfferServiceWithRecall(db);
        var decliner = await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == fx.Applications[1].Id);
        var offer = await IssueOfferAsync(db, offers, decliner.Id);

        var settlement = await offers.DeclineAndDraftNextAsync(offer.Id, "قبلت عرضاً آخر", OfferDeclineReasonCategory.OtherOffer, OfferDeclinedBy.Candidate, null);

        Assert.True(settlement.Success, settlement.Error);
        var recalled = await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == fx.Applications[5].Id);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, recalled.Status);
        Assert.Equal(decliner.RequisitionId, recalled.RequisitionId);
        Assert.Equal(l2.Id, recalled.CompetitionLineId);
        Assert.Equal(6, recalled.Rank);
        var entries = await db.CompetitionReserveEntries.AsNoTracking().Where(e => e.RecruitmentApplicationId == recalled.Id).ToListAsync();
        Assert.Equal(CompetitionReserveEntryStatus.Recalled, entries.Single(e => e.CompetitionLineId == l2.Id).Status);
        // The recalled candidate leaves every other line's reserve.
        Assert.Equal(CompetitionReserveEntryStatus.Removed, entries.Single(e => e.CompetitionLineId == l1.Id).Status);
        Assert.Equal(CompetitionReserveEntryStatus.Active,
            (await db.CompetitionReserveEntries.AsNoTracking().SingleAsync(e => e.RecruitmentApplicationId == fx.Applications[6].Id)).Status);
        Assert.Single(await db.CompetitionAllocations.AsNoTracking().Where(x => x.FromReserve).ToListAsync());
        Assert.Single(await db.RecruitmentApplicationEvents.AsNoTracking()
            .Where(e => e.EventType == RecruitmentApplicationEventTypes.CompetitionReserveRecalled).ToListAsync());

        // The PLAN-27 next-candidate path now sees the recalled candidate on the vacated requisition.
        var (draft, draftError) = await offers.CreateDraftAsync(recalled.Id, 600_000m, null, DateTime.UtcNow.Date.AddDays(20), null, Creator);
        Assert.True(draft is not null, draftError);
    }

    [Fact]
    public async Task An_expired_reserve_is_never_recalled_and_HR_is_told_AC_HCM_31g_negative()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, _, l2) = await SeedFinalizedAsync(db);
        foreach (var entry in await db.CompetitionReserveEntries.Where(e => e.CompetitionLineId == l2.Id).ToListAsync())
            entry.ReserveUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await db.SaveChangesAsync();
        var offers = CreateOfferServiceWithRecall(db);
        var offer = await IssueOfferAsync(db, offers, fx.Applications[1].Id);

        await offers.DeclineAndDraftNextAsync(offer.Id, "قبلت عرضاً آخر", OfferDeclineReasonCategory.OtherOffer, OfferDeclinedBy.Candidate, null);

        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == fx.Applications[5].Id)).Status);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == fx.Applications[6].Id)).Status);
        var vacated = (await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == fx.Applications[1].Id)).RequisitionId;
        Assert.DoesNotContain(await db.RecruitmentApplications.AsNoTracking().Where(x => x.RequisitionId == vacated).Select(x => x.Status).ToListAsync(),
            s => s == RecruitmentApplicationStatus.Ranked);
        Assert.Single(await db.AuditLogs.AsNoTracking().Where(x => x.Action == CompetitionReserveService.ExhaustedAuditAction).ToListAsync());
    }

    [Fact]
    public async Task A_revoked_offer_or_a_withdrawal_without_offer_also_recalls()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, _, _) = await SeedFinalizedAsync(db);
        var offers = CreateOfferServiceWithRecall(db);
        var (draft, _) = await offers.CreateDraftAsync(fx.Applications[0].Id, 600_000m, null, DateTime.UtcNow.Date.AddDays(20), null, Creator);
        Assert.True((await offers.RevokeAsync(draft!.Id, "إلغاء العرض بقرار الإدارة", revertToRanked: false, Creator)).Success);
        await offers.DraftNextOfferAfterSettlementAsync(draft.Id, Creator);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, (await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == fx.Applications[5].Id)).Status);

        // c1 withdraws before any offer: the next L2 reserve (c6) is recalled.
        var selection = new RecruitmentSelectionService(db);
        var (withdrawn, error, settled) = await selection.WithdrawByCandidateAsync(fx.Applications[1].Id, "ظروف خاصة", offers);
        Assert.True(withdrawn, error);
        Assert.Null(settled);
        var recall = await new CompetitionReserveService(db).RecallAfterWithdrawalAsync(fx.Applications[1].Id, null);
        Assert.True(recall.Recalled);
        Assert.Equal(fx.Applications[6].Id, recall.RecalledApplicationId);
    }

    [Fact]
    public async Task The_expiry_pass_expires_line_entries_and_rejects_reserves_with_no_entry_left()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (fx, _, _) = await SeedFinalizedAsync(db);
        var until = (await db.CompetitionReserveEntries.AsNoTracking().FirstAsync()).ReserveUntil;
        var expirer = new RecruitmentReserveExpirer(
            new ReserveListService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()), NullLogger<ReserveListService>.Instance),
            new CompetitionReserveService(db));

        Assert.Equal(0, await expirer.ExpireDueAsync(until, default));
        await expirer.ExpireDueAsync(until.AddDays(1), default);

        Assert.All(await db.CompetitionReserveEntries.AsNoTracking().ToListAsync(), e => Assert.Equal(CompetitionReserveEntryStatus.Expired, e.Status));
        var reserve = await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == fx.Applications[6].Id);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, reserve.Status);
        Assert.Equal(RecruitmentRejectionKind.ReserveExpired, reserve.RejectionKind);
    }

    [Fact]
    public async Task The_single_requisition_reserve_list_behaves_as_before()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var requisition = await Hcm027TestSupport.SeedRequisitionAsync(db, "H31R");
        var reserve = await Hcm027TestSupport.SeedApplicationAsync(db, requisition, RecruitmentApplicationStatus.ReserveListed, 2);
        reserve.ReserveUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await db.SaveChangesAsync();
        var expirer = new RecruitmentReserveExpirer(
            new ReserveListService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()), NullLogger<ReserveListService>.Instance),
            new CompetitionReserveService(db));

        Assert.Equal(1, await expirer.ExpireDueAsync(DateOnly.FromDateTime(DateTime.UtcNow), default));

        var saved = await db.RecruitmentApplications.AsNoTracking().SingleAsync(x => x.Id == reserve.Id);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, saved.Status);
        Assert.Equal(RecruitmentRejectionKind.ReserveExpired, saved.RejectionKind);
        Assert.False((await new CompetitionReserveService(db).RecallForVacancyAsync(requisition.Id, null)).Recalled);
    }
}
