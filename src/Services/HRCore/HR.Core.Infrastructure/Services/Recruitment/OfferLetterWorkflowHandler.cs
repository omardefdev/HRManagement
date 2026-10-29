using HR.Core.Domain.Enums;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <param name="documents">
/// HCM-029 (D2): stamps the offer letter hash at issue. Optional so existing callers that build the handler by hand
/// keep working; they get the default <see cref="RecruitmentOptions"/> (DI always supplies the configured service).
/// </param>
public sealed class OfferLetterWorkflowHandler(HRCoreDbContext db, OfferLetterDocumentService? documents = null)
{
    public async Task HandleAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default)
    {
        if (!string.Equals(instance.EntityType, "OfferLetter", StringComparison.OrdinalIgnoreCase))
            return;

        var offer = await db.OfferLetters.FirstOrDefaultAsync(o => o.Id == instance.EntityId, ct);
        // PLAN-25 (review finding 2): only the instance linked to the offer decides it.
        if (offer is null || offer.Status != OfferStatus.PendingApproval || offer.WorkflowInstanceId != instance.Id)
            return;

        if (approved)
        {
            // HCM-027: ExpiryDate = IssuedDate + ValidityDays (end of day UTC).
            var nowUtc = DateTime.UtcNow;
            OfferWorkflowRules.MarkIssued(offer, nowUtc);
            // HCM-029 (D2): the hash of the rendered letter is fixed at issue.
            await (documents ?? new OfferLetterDocumentService(
                    db, new LegislationRegisterProvider(db), Options.Create(new RecruitmentOptions())))
                .StampAsync(offer, nowUtc, ct);
        }
        else
        {
            offer.Status = OfferStatus.Draft;
            offer.WorkflowInstanceId = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
