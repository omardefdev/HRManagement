using HR.Core.Domain.Enums;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentRequisitionWorkflowHandler(HRCoreDbContext db)
{
    public async Task HandleAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default)
    {
        if (!string.Equals(instance.EntityType, "RecruitmentRequisition", StringComparison.OrdinalIgnoreCase))
            return;

        var requisition = await db.RecruitmentRequisitions
            .FirstOrDefaultAsync(r => r.Id == instance.EntityId, ct);
        // PLAN-25 (review finding 2): only the instance linked to the requisition decides it.
        if (requisition is null || requisition.Status != RequisitionStatus.PendingApproval || requisition.WorkflowInstanceId != instance.Id)
            return;

        if (approved)
        {
            requisition.Status = RequisitionStatus.Approved;
            requisition.ApprovedDate = DateTime.UtcNow;
        }
        else
        {
            requisition.Status = RequisitionStatus.Draft;
            requisition.WorkflowInstanceId = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
