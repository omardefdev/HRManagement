using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class SelectionCommitteeWorkflowHandler(HRCoreDbContext db)
{
    public async Task HandleAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default)
    {
        if (!string.Equals(instance.EntityType, "SelectionCommitteeSession", StringComparison.OrdinalIgnoreCase))
            return;

        var session = await db.SelectionCommitteeSessions.FirstOrDefaultAsync(s => s.Id == instance.EntityId, ct);
        if (session is null || session.ApprovedAt is not null)
            return;

        if (approved)
        {
            session.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            session.WorkflowInstanceId = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
