using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-027 (D): the offer expiry worker.</summary>
public class Hcm027OfferExpirerTests
{
    [Fact]
    public async Task Expires_due_offers_only_and_updates_application()
    {
        await using var db = CreateContext();
        var (dueOfferId, dueAppId) = await Hcm027OfferOutcomeTests.CreateIssuedOfferAsync(db);
        var (freshOfferId, freshAppId) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: true, suffix: "B");
        var (pendingOfferId, _) = await Hcm027OfferOutcomeTests.CreateOfferAsync(db, approve: false, suffix: "C");
        await Hcm027OfferOutcomeTests.BackdateExpiryAsync(db, dueOfferId);

        var count = await CreateExpirer(db).ExpireDueAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, count);
        db.ChangeTracker.Clear();
        Assert.Equal(OfferStatus.Expired, (await db.OfferLetters.SingleAsync(o => o.Id == dueOfferId)).Status);
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync(o => o.Id == freshOfferId)).Status);
        Assert.Equal(OfferStatus.PendingApproval, (await db.OfferLetters.SingleAsync(o => o.Id == pendingOfferId)).Status);

        var dueApp = await db.RecruitmentApplications.SingleAsync(a => a.Id == dueAppId);
        Assert.Equal(RecruitmentApplicationStatus.OfferDeclined, dueApp.Status);
        Assert.Null(dueApp.ActiveOfferLetterId);
        Assert.Contains(RecruitmentOfferService.ExpiryNote, dueApp.Notes);
        Assert.Equal(RecruitmentApplicationStatus.OfferExtended, (await db.RecruitmentApplications.SingleAsync(a => a.Id == freshAppId)).Status);
        Assert.True(await db.AuditLogs.AnyAsync(l => l.EntityId == dueOfferId && l.Action == "ExpireOffer"));
    }

    [Fact]
    public async Task Is_idempotent()
    {
        await using var db = CreateContext();
        var (offerId, _) = await Hcm027OfferOutcomeTests.CreateIssuedOfferAsync(db);
        await Hcm027OfferOutcomeTests.BackdateExpiryAsync(db, offerId);

        Assert.Equal(1, await CreateExpirer(db).ExpireDueAsync(DateTime.UtcNow, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await CreateExpirer(db).ExpireDueAsync(DateTime.UtcNow, CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == offerId && l.Action == "ExpireOffer"));
    }

    [Fact]
    public async Task Nothing_due_returns_zero()
    {
        await using var db = CreateContext();
        await Hcm027OfferOutcomeTests.CreateIssuedOfferAsync(db);

        Assert.Equal(0, await CreateExpirer(db).ExpireDueAsync(DateTime.UtcNow, CancellationToken.None));
    }

    private static RecruitmentOfferExpirer CreateExpirer(HR.Core.Infrastructure.Data.HRCoreDbContext db) =>
        new(db, CreateOfferService(db), NullLogger<RecruitmentOfferExpirer>.Instance);
}
