using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
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
        // PLAN-25 (review finding 2): only the instance linked to the change request decides it.
        if (request is null || request.Status != EstablishmentChangeStatus.PendingApproval || request.WorkflowInstanceId != instance.Id)
            return;

        if (approved)
        {
            request.Status = EstablishmentChangeStatus.Approved;
            await db.SaveChangesAsync(ct);
            // PLAN-44: nothing re-applies a change later, so a change that cannot apply refuses the decision that would have finished it.
            var (applied, error) = await lifecycleService.ApplyApprovedAsync(request, instance.InitiatedBy, ct);
            if (!applied)
                throw new WorkflowOutcomeRefusedException(error ?? "تعذّر تطبيق طلب تغيير الملاك.");
        }
        else
        {
            request.Status = EstablishmentChangeStatus.Draft;
            request.WorkflowInstanceId = null;
            await db.SaveChangesAsync(ct);
        }
    }
}
