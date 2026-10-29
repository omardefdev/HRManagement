using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-028 (H3): an approved RECRUITMENT-APPEAL-REVIEW applies the outcome stored by
/// <see cref="RecruitmentAppealService.DecideAsync"/>; a rejected step is a request for more information.
/// </summary>
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

        var actorId = LastActorId(instance) ?? instance.InitiatedBy;
        if (approved)
        {
            // WorkflowInstanceActionService refuses an approval without a stored outcome.
            if (appeal.Outcome is null)
                return;
            await ApplyOutcomeAsync(appeal, actorId, instance.Id, ct);
        }
        else
        {
            var text = string.IsNullOrWhiteSpace(reason) ? "طلب المراجع معلومات إضافية." : reason.Trim();
            RecordInfoRequest(appeal, text, actorId, instance.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>HCM-031: the corrected mark of an upheld result objection (round ObjectionCorrection, by the reviewer).</summary>
    private async Task StageObjectionCorrectionAsync(RecruitmentAppeal appeal, Guid? actorUserId, DateTime now, CancellationToken ct)
    {
        if (appeal.ObjectionExamCandidateId is not Guid paperId || appeal.ObjectionCorrectedScore is not decimal corrected)
            throw new InvalidOperationException("الاعتراض المقبول يتطلب الدفتر والعلامة المصحّحة.");
        var paper = await db.ExamCandidates.FirstAsync(c => c.Id == paperId, ct);
        db.ExamScores.Add(new ExamScore
        {
            TenantId = paper.TenantId,
            ExamCandidateId = paper.Id,
            MarkerUserId = actorUserId ?? Guid.Empty,
            Round = ExamScoreRound.ObjectionCorrection,
            Score = corrected,
            SubmittedAt = now,
            RecruitmentAppealId = appeal.Id
        });
        paper.FinalMark = Math.Round(corrected, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Stages the effect of <see cref="RecruitmentAppeal.Outcome"/> on the appeal and its application (no save).</summary>
    internal async Task ApplyOutcomeAsync(RecruitmentAppeal appeal, Guid? actorUserId, Guid? workflowInstanceId, CancellationToken ct)
    {
        var app = appeal.RecruitmentApplication
            ?? await db.RecruitmentApplications.FirstAsync(a => a.Id == appeal.RecruitmentApplicationId, ct);
        var outcome = appeal.Outcome!.Value;
        var now = DateTime.UtcNow;

        appeal.Status = RecruitmentAppealStatus.Decided;
        appeal.DecidedAt = now;
        app.AppealStatus = RecruitmentAppealStatus.Decided;

        switch (outcome)
        {
            case RecruitmentAppealOutcome.Upheld:
                // Scores are kept; the application is re-assessed and ranked again, so its old rank is released.
                ClearRejection(app);
                app.Status = RecruitmentApplicationStatus.UnderReview;
                app.ReviewedAt = now;
                app.ScreenedAt = now;
                app.Rank = null;
                app.RankedAt = null;
                break;
            case RecruitmentAppealOutcome.UpheldRankCorrection:
                var rankBeforeRejection = app.RankBeforeRejection ?? app.Rank;
                ClearRejection(app);
                app.Status = RecruitmentApplicationStatus.Assessed;
                var error = await new RecruitmentSelectionService(db).StageAppealRankCorrectionAsync(
                    app, appeal.CorrectedRank!.Value, appeal.Decision!, appeal.Id, rankBeforeRejection, actorUserId, ct);
                if (error is not null)
                    throw new InvalidOperationException(error);
                break;
            case RecruitmentAppealOutcome.UpheldNoRemedy:
                db.AuditLogs.Add(new EntityAuditLog
                {
                    EntityType = nameof(RecruitmentAppeal),
                    EntityId = appeal.Id,
                    Action = "AppealUpheldNoRemedy",
                    UserId = actorUserId,
                    ChangedFields = JsonSerializer.Serialize(new { applicationId = app.Id, requisitionId = app.RequisitionId })
                });
                // PLAN-30: alert compliance
                break;
            case RecruitmentAppealOutcome.Dismissed:
                // The original rejection (kind, date, note) stays.
                break;
            case RecruitmentAppealOutcome.ObjectionUpheld:
                // HCM-031 (PLAN-31): the reviewer's corrected mark replaces the paper's mark; the competition results
                // are recomputed into a new draft version by CompetitionObjectionService after this decision is saved.
                await StageObjectionCorrectionAsync(appeal, actorUserId, now, ct);
                break;
            case RecruitmentAppealOutcome.ObjectionDismissed:
                break;
        }

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentAppeal),
            EntityId = appeal.Id,
            Action = "AppealDecided",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new
            {
                outcome = outcome.ToString(),
                decision = appeal.Decision,
                correctedRank = appeal.CorrectedRank,
                workflowInstanceId
            })
        });
    }

    /// <summary>Stages a request for more information; the appeal stays pending until HR resubmits.</summary>
    internal void RecordInfoRequest(RecruitmentAppeal appeal, string text, Guid? actorUserId, Guid? workflowInstanceId)
    {
        appeal.InfoRequest = text.Length > 2000 ? text[..2000] : text;
        appeal.InfoRequestedAt = DateTime.UtcNow;
        appeal.WorkflowInstanceId = null;
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentAppeal),
            EntityId = appeal.Id,
            Action = "AppealInfoRequested",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new { infoRequest = appeal.InfoRequest, workflowInstanceId })
        });
    }

    private static void ClearRejection(RecruitmentApplication app)
    {
        app.RejectedAt = null;
        app.RejectionKind = null;
        app.RejectionNote = null;
        app.RankBeforeRejection = null;
    }

    private static Guid? LastActorId(WorkflowInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.StepHistoryJson))
            return null;
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson);
        return history?.LastOrDefault()?.ActorId;
    }
}
