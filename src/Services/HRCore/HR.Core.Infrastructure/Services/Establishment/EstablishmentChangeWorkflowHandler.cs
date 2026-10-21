using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public sealed class EstablishmentChangeWorkflowHandler(
    HRCoreDbContext db,
    EstablishmentChangeLifecycleService lifecycleService)
{
    public async Task HandleAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default)
    {
        if (!string.Equals(instance.EntityType, "EstablishmentChangeRequest", StringComparison.OrdinalIgnoreCase))
            return;

        var request = await db.EstablishmentChangeRequests
            .FirstOrDefaultAsync(r => r.Id == instance.EntityId, ct);
        if (request is null || request.Status != EstablishmentChangeStatus.PendingApproval)
            return;

        if (approved)
        {
            request.Status = EstablishmentChangeStatus.Approved;
            await db.SaveChangesAsync(ct);
            await lifecycleService.ApplyApprovedAsync(request, instance.InitiatedBy, ct);
        }
        else
        {
            request.Status = EstablishmentChangeStatus.Draft;
            request.WorkflowInstanceId = null;
            await db.SaveChangesAsync(ct);
        }
    }
}
