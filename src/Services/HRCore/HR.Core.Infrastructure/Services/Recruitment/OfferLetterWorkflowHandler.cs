using HR.Core.Domain.Enums;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class OfferLetterWorkflowHandler(HRCoreDbContext db)
{
    public async Task HandleAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default)
    {
        if (!string.Equals(instance.EntityType, "OfferLetter", StringComparison.OrdinalIgnoreCase))
            return;

        var offer = await db.OfferLetters.FirstOrDefaultAsync(o => o.Id == instance.EntityId, ct);
        if (offer is null || offer.Status != OfferStatus.PendingApproval)
            return;

        if (approved)
        {
            // HCM-027: ExpiryDate = IssuedDate + ValidityDays (end of day UTC).
            OfferWorkflowRules.MarkIssued(offer, DateTime.UtcNow);
        }
        else
        {
            offer.Status = OfferStatus.Draft;
            offer.WorkflowInstanceId = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
