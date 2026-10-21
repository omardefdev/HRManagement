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

        if (offerService.AutoDraftNextOfferEnabled)
            await AutoDraftNextOffersAsync(due, ct);

        return due.Count;
    }

    /// <summary>
    /// HCM-027 (F3): drafts the next candidate's offer for each expired offer. The job runs without a request
    /// tenant, so each draft runs in the expired offer's own tenant (tenant filters and the Payroll
    /// <c>X-Tenant-ID</c> header then apply to that tenant only). A failure is logged and does not undo the expiry.
    /// </summary>
    private async Task AutoDraftNextOffersAsync(IReadOnlyList<HR.Core.Domain.Entities.OfferLetter> expired, CancellationToken ct)
    {
        foreach (var offer in expired)
        {
            var previousTenant = TenantIdFlow.Get();
            TenantIdFlow.Set(offer.TenantId);
            try
            {
                var (draft, error) = await offerService.TryAutoDraftNextOfferAsync(offer.Id, actorUserId: null, ct);
                if (draft is not null)
                    logger.LogInformation("Auto-drafted offer {OfferNumber} after expiry of {Expired}.", draft.OfferNumber, offer.OfferNumber);
                else if (error is not null)
                    logger.LogInformation("No auto-draft after expiry of {Expired}: {Reason}", offer.OfferNumber, error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Auto-draft after expiry of {Expired} failed.", offer.OfferNumber);
            }
            finally
            {
                TenantIdFlow.Set(previousTenant);
            }
        }
    }
}
