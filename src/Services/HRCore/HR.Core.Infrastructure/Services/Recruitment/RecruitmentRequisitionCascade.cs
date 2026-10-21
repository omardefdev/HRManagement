using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-026 (G-11): cascades applied when a requisition is cancelled (manually or by a position freeze)
/// or closed. Methods only stage changes on the tracked context — the caller saves once, so the whole
/// cascade commits atomically with the triggering change.
/// </summary>
internal static class RecruitmentRequisitionCascade
{
    private static readonly OfferStatus[] RevocableOfferStatuses =
        [OfferStatus.Draft, OfferStatus.PendingApproval, OfferStatus.Issued];

    private static readonly RecruitmentApplicationStatus[] TerminalApplicationStatuses =
        [RecruitmentApplicationStatus.Hired, RecruitmentApplicationStatus.Rejected, RecruitmentApplicationStatus.Withdrawn];

    public sealed record Impact(int RevokedOffers, int RejectedApplications, int CancelledInterviews);

    /// <summary>Cancelling is refused once a candidate is hired or has accepted an offer.</summary>
    public static async Task<string?> ValidateCancellableAsync(
        HRCoreDbContext db,
        Guid requisitionId,
        CancellationToken ct)
    {
        var hasCommittedCandidate = await db.RecruitmentApplications.AnyAsync(
            a => a.RequisitionId == requisitionId
                && (a.Status == RecruitmentApplicationStatus.Hired || a.Status == RecruitmentApplicationStatus.OfferAccepted),
            ct);
        if (hasCommittedCandidate)
            return "لا يمكن إلغاء طلب شاغر عليه مرشح معيّن أو قبل عرض التوظيف.";

        var hasAcceptedOffer = await db.OfferLetters.AnyAsync(
            o => o.RecruitmentApplication != null
                && o.RecruitmentApplication.RequisitionId == requisitionId
                && o.Status == OfferStatus.Accepted,
            ct);
        return hasAcceptedOffer
            ? "لا يمكن إلغاء طلب شاغر عليه عرض توظيف مقبول."
            : null;
    }

    /// <summary>Stages the full cancellation cascade for <paramref name="requisition"/> (tracked).</summary>
    public static async Task<Impact> StageCancellationAsync(
        HRCoreDbContext db,
        RecruitmentRequisition requisition,
        string reason,
        Guid? actorUserId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await UnpublishAsync(db, requisition, ct);

        var offers = await db.OfferLetters
            .Where(o => o.RecruitmentApplication != null
                && o.RecruitmentApplication.RequisitionId == requisition.Id
                && RevocableOfferStatuses.Contains(o.Status))
            .ToListAsync(ct);
        foreach (var offer in offers)
        {
            offer.Status = OfferStatus.Revoked;
            await CancelWorkflowAsync(db, offer.WorkflowInstanceId, now, ct);
        }

        var rejected = await RejectOpenApplicationsAsync(db, requisition.Id, $"إلغاء الشاغر: {reason}", now, ct);
        var cancelledInterviews = await CancelScheduledInterviewsAsync(db, requisition.Id, reason, ct);
        // PLAN-30: notify affected candidates (citizen and internal applicants) of the cancellation.

        await CancelWorkflowAsync(db, requisition.WorkflowInstanceId, now, ct);
        requisition.Status = RequisitionStatus.Cancelled;
        requisition.ClosedDate = now;
        requisition.CancelledAt = now;
        requisition.CancelledBy = actorUserId;
        requisition.CancellationReason = reason;

        var impact = new Impact(offers.Count, rejected, cancelledInterviews);
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = requisition.TenantId,
            EntityType = nameof(RecruitmentRequisition),
            EntityId = requisition.Id,
            Action = "CancelRequisition",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new
            {
                reason,
                revokedOffers = impact.RevokedOffers,
                rejectedApplications = impact.RejectedApplications,
                cancelledInterviews = impact.CancelledInterviews
            })
        });

        return impact;
    }

    /// <summary>Hides the public posting and closes the internal vacancy of the requisition.</summary>
    public static async Task UnpublishAsync(HRCoreDbContext db, RecruitmentRequisition requisition, CancellationToken ct)
    {
        var postings = await db.PublicJobPostings
            .Where(p => p.RequisitionId == requisition.Id || p.Id == requisition.PublicJobPostingId)
            .ToListAsync(ct);
        foreach (var posting in postings)
            posting.IsPublished = false;

        var vacancies = await db.InternalVacancies
            .Where(v => v.RequisitionId == requisition.Id || v.Id == requisition.InternalVacancyId)
            .ToListAsync(ct);
        foreach (var vacancy in vacancies)
            vacancy.IsOpen = false;
    }

    /// <summary>Non-terminal applications (not Hired/Rejected/Withdrawn) become Rejected with an explanatory note.</summary>
    public static async Task<int> RejectOpenApplicationsAsync(
        HRCoreDbContext db,
        Guid requisitionId,
        string note,
        DateTime now,
        CancellationToken ct)
    {
        var open = await db.RecruitmentApplications
            .Where(a => a.RequisitionId == requisitionId && !TerminalApplicationStatuses.Contains(a.Status))
            .ToListAsync(ct);
        foreach (var app in open)
        {
            app.Status = RecruitmentApplicationStatus.Rejected;
            app.ReviewedAt = now;
            app.Notes = string.IsNullOrWhiteSpace(app.Notes) ? note : $"{app.Notes}\n{note}";
        }

        return open.Count;
    }

    public static async Task<int> CancelScheduledInterviewsAsync(
        HRCoreDbContext db,
        Guid requisitionId,
        string reason,
        CancellationToken ct)
    {
        var slots = await db.RecruitmentInterviewSlots
            .Where(s => s.Status == InterviewSlotStatus.Scheduled
                && s.RecruitmentApplication != null
                && s.RecruitmentApplication.RequisitionId == requisitionId)
            .ToListAsync(ct);
        var trimmed = reason.Length > 500 ? reason[..500] : reason;
        foreach (var slot in slots)
        {
            slot.Status = InterviewSlotStatus.Cancelled;
            slot.CancelledReason = trimmed;
        }

        return slots.Count;
    }

    private static async Task CancelWorkflowAsync(HRCoreDbContext db, Guid? workflowInstanceId, DateTime now, CancellationToken ct)
    {
        if (workflowInstanceId is not Guid id)
            return;

        var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (instance is { Status: WorkflowStatus.InProgress })
        {
            instance.Status = WorkflowStatus.Cancelled;
            instance.CompletedAt = now;
        }
    }
}
