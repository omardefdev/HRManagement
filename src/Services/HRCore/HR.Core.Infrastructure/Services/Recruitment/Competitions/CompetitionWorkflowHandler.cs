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
        if (string.Equals(instance.EntityType, CompetitionResultsService.EntityType, StringComparison.OrdinalIgnoreCase))
        {
            await HandleResultsAsync(instance, approved, reason, ct);
            return;
        }
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

    /// <summary>HCM-031 (P4): <c>RECRUITMENT-RESULTS-APPROVAL</c> — approved → Approved (ready to publish); rejected → Draft.</summary>
    private async Task HandleResultsAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct)
    {
        var version = await db.CompetitionResultVersions.Include(v => v.Competition)
            .FirstOrDefaultAsync(v => v.Id == instance.EntityId, ct);
        if (version?.Competition is null || version.Status != CompetitionResultVersionStatus.PendingApproval)
            return;

        if (approved)
        {
            version.Status = CompetitionResultVersionStatus.Approved;
        }
        else
        {
            version.Status = CompetitionResultVersionStatus.Draft;
            version.WorkflowInstanceId = null;
            var text = string.IsNullOrWhiteSpace(reason) ? "أُعيدت دون ذكر سبب." : reason.Trim();
            version.ReturnReason = text[..Math.Min(500, text.Length)];
            if (version.Competition.Status == RecruitmentCompetitionStatus.ResultsPendingApproval)
                version.Competition.Status = RecruitmentCompetitionStatus.ResultsDraft;
        }

        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = version.TenantId,
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = version.CompetitionId,
            Action = approved ? "ApproveResults" : "ReturnResults",
            ChangedFields = JsonSerializer.Serialize(new { version.VersionNo, reason = approved ? null : version.ReturnReason })
        });
        await db.SaveChangesAsync(ct);
    }
}
