using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Workflow;
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
            // HCM-028: a rejected session goes back to the secretary for rework, unlocked.
            session.WorkflowInstanceId = null;
            session.SubmittedAt = null;
            session.LockedAt = null;
            db.AuditLogs.Add(new EntityAuditLog
            {
                EntityType = nameof(SelectionCommitteeSession),
                EntityId = session.Id,
                Action = "CommitteeWorkflowRejected",
                UserId = LastActorId(instance) ?? instance.InitiatedBy,
                ChangedFields = JsonSerializer.Serialize(new { workflowInstanceId = instance.Id, reason })
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static Guid? LastActorId(WorkflowInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.StepHistoryJson))
            return null;
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson);
        return history?.LastOrDefault()?.ActorId;
    }
}
