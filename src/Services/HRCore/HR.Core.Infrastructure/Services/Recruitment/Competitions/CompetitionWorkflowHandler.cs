using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>
/// HCM-031 (PLAN-31): outcome of <c>RECRUITMENT-COMPETITION-APPROVAL</c> — approved → <c>Approved</c>; rejected → back
/// to <c>Draft</c> with the reason (the line criteria versions are unfrozen so the competition can be edited).
/// </summary>
public sealed class CompetitionWorkflowHandler(HRCoreDbContext db)
{
    public async Task HandleAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default)
    {
        if (!string.Equals(instance.EntityType, CompetitionLifecycleService.EntityType, StringComparison.OrdinalIgnoreCase))
            return;

        var competition = await db.RecruitmentCompetitions
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == instance.EntityId, ct);
        if (competition is null || competition.Status != RecruitmentCompetitionStatus.PendingApproval)
            return;

        if (approved)
        {
            competition.Status = RecruitmentCompetitionStatus.Approved;
            competition.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            competition.Status = RecruitmentCompetitionStatus.Draft;
            competition.ApprovalWorkflowInstanceId = null;
            competition.ReturnReason = string.IsNullOrWhiteSpace(reason) ? "أُعيدت دون ذكر سبب." : reason.Trim()[..Math.Min(500, reason.Trim().Length)];
            foreach (var line in competition.Lines)
                line.CriteriaVersionId = null;
        }

        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = competition.TenantId,
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = competition.Id,
            Action = approved ? "ApproveCompetition" : "ReturnCompetition",
            ChangedFields = JsonSerializer.Serialize(new { reason = approved ? null : competition.ReturnReason })
        });
        await db.SaveChangesAsync(ct);
    }
}
