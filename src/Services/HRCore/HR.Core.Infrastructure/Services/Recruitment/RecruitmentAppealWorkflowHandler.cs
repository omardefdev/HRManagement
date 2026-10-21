using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentAppealWorkflowHandler(HRCoreDbContext db)
{
    public async Task HandleAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default)
    {
        if (!string.Equals(instance.EntityType, "RecruitmentAppeal", StringComparison.OrdinalIgnoreCase))
            return;

        var appeal = await db.RecruitmentAppeals
            .Include(a => a.RecruitmentApplication)
            .FirstOrDefaultAsync(a => a.Id == instance.EntityId, ct);
        if (appeal is null || appeal.Status != RecruitmentAppealStatus.Pending)
            return;

        if (approved)
        {
            var decision = string.IsNullOrWhiteSpace(reason) ? "تم قبول الاستئناف عبر مسار الاعتماد." : reason.Trim();
            await ApplyDecisionAsync(appeal, upheld: true, decision, appeal.IndependentReviewerId ?? Guid.Empty, ct);
            return;
        }

        var rejectDecision = string.IsNullOrWhiteSpace(reason) ? "تم رفض الاستئناف." : reason.Trim();
        await ApplyDecisionAsync(appeal, upheld: false, rejectDecision, appeal.IndependentReviewerId ?? Guid.Empty, ct);
    }

    private async Task ApplyDecisionAsync(
        RecruitmentAppeal appeal,
        bool upheld,
        string decision,
        Guid actorUserId,
        CancellationToken ct)
    {
        appeal.Status = RecruitmentAppealStatus.Decided;
        appeal.DecidedAt = DateTime.UtcNow;
        appeal.Decision = decision;

        var app = appeal.RecruitmentApplication!;
        app.AppealStatus = RecruitmentAppealStatus.Decided;
        app.Status = upheld ? RecruitmentApplicationStatus.UnderReview : RecruitmentApplicationStatus.Rejected;
        app.ReviewedAt = DateTime.UtcNow;

        db.AuditLogs.Add(new HRManagement.SharedKernel.Entities.EntityAuditLog
        {
            EntityType = nameof(RecruitmentAppeal),
            EntityId = appeal.Id,
            Action = upheld ? "AppealUpheld" : "AppealRejected",
            UserId = actorUserId,
            ChangedFields = $$"""{"decision":{{System.Text.Json.JsonSerializer.Serialize(decision)}}}"""
        });

        await db.SaveChangesAsync(ct);
    }
}
