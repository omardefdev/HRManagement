using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-027 (D): expires Issued offers whose expiry date has passed, across tenants, with the same logic as
/// <see cref="RecruitmentOfferService.ExpireAsync"/> (offer Expired, application OfferDeclined). Idempotent.
/// </summary>
public sealed class RecruitmentOfferExpirer(
    HRCoreDbContext db,
    RecruitmentOfferService offerService,
    ILogger<RecruitmentOfferExpirer> logger)
{
    public async Task<int> ExpireDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        // PLAN-30: reminder 48h before expiry
        var due = await db.OfferLetters
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Include(o => o.RecruitmentApplication)
            .Where(o => o.Status == OfferStatus.Issued && o.ExpiryDate != null && o.ExpiryDate < nowUtc)
            .ToListAsync(ct);
        if (due.Count == 0)
            return 0;

        foreach (var offer in due)
            offerService.ApplyExpiry(offer, nowUtc);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Recruitment offer expiry: {Count} issued offer(s) expired.", due.Count);
        return due.Count;
    }
}
