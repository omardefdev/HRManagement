using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentOfferService(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    ICodeGenerator codeGenerator,
    RecruitmentLegalSourceValidator legalSourceValidator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration,
    IOptions<RecruitmentOptions> recruitmentOptions)
{
    public const string OfferApprovalWorkflowCode = "RECRUITMENT-OFFER-APPROVAL";

    /// <summary>HCM-027: three-step approval with distinct approvers for merit override offers.</summary>
    public const string OfferOverrideApprovalWorkflowCode = OfferOverrideApprovalRules.WorkflowCode;

    public const string ExpiryNote = "انتهاء صلاحية العرض";

    /// <summary>HCM-027: <c>Recruitment:OfferValidityDays</c> — default validity of a new offer.</summary>
    public int DefaultValidityDays => recruitmentOptions.Value.OfferValidityDays;

    public async Task<(OfferLetter? Offer, string? Error)> CreateDraftAsync(
        Guid applicationId,
        decimal offeredSalary,
        string? gradeCode,
        DateTime proposedStartDate,
        int? validityDays,
        Guid? actorUserId = null,
        bool isMeritOverride = false,
        string? meritOverrideReason = null,
        CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null) return (null, "الطلب غير موجود.");
        if (app.RequisitionId is not Guid requisitionId || app.Requisition?.FundedPositionId is null)
            return (null, "يجب ربط الطلب بطلب شاغر قبل إنشاء العرض.");

        // HCM-027 (G-07): Ranked only, merit order, one active offer per requisition.
        var bestAvailableRank = await GetBestAvailableRankAsync(requisitionId, ct);
        var otherActiveOffer = await HasOtherActiveOfferAsync(requisitionId, app.Id, ct);
        var eligibilityError = OfferEligibilityRules.Validate(
            app, bestAvailableRank, otherActiveOffer, isMeritOverride, meritOverrideReason);
        if (eligibilityError is not null)
            return (null, eligibilityError);

        // HCM-027: the expiry is computed when the offer is issued (IssuedDate + ValidityDays).
        var validity = validityDays ?? DefaultValidityDays;
        if (!OfferWorkflowRules.IsValidityDaysInRange(validity))
            return (null, OfferWorkflowRules.ValidityDaysRangeMessage);

        if (app.ActiveOfferLetterId.HasValue)
        {
            var existing = await db.OfferLetters.FindAsync([app.ActiveOfferLetterId], ct);
            if (existing is not null && OfferWorkflowRules.IsActiveOffer(existing))
                return (null, "يوجد عرض نشط مسبقاً.");
        }

        if (RecruitmentLegalSourceRules.IsEmpty(app.Requisition.LegalSourceId))
            return (null, RecruitmentLegalSourceRules.RequisitionMissingLegalSourceForOfferMessage);

        var code = await codeGenerator.GenerateAsync(CodeGenerationRequests.OfferLetter(), ct);
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id,
            FundedPositionId = app.Requisition.FundedPositionId,
            OfferNumber = code.Code,
            Status = OfferStatus.Draft,
            OfferedSalary = offeredSalary,
            GradeCode = gradeCode,
            ProposedStartDate = proposedStartDate,
            ValidityDays = validity,
            LegalSourceId = app.Requisition.LegalSourceId!.Trim(),
            IsMeritOverride = isMeritOverride,
            MeritOverrideReason = isMeritOverride ? meritOverrideReason!.Trim() : null,
            CreatedBy = actorUserId
        };

        db.OfferLetters.Add(offer);
        app.ActiveOfferLetter = offer;

        app.Status = RecruitmentApplicationStatus.OfferExtended;
        app.ReviewedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return (offer, null);
    }

    public async Task<(bool Success, string? Error)> SubmitForApprovalAsync(
        Guid offerId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default)
    {
        var offer = await LoadOfferAsync(offerId, ct);
        if (offer is null) return (false, "خطاب العرض غير موجود.");
        if (!OfferWorkflowRules.CanSubmitForApproval(offer))
            return (false, "لا يمكن تقديم هذا العرض للاعتماد.");

        var legalError = await legalSourceValidator.ValidateRequiredAsync(
            offer.LegalSourceId,
            RecruitmentLegalSourceRules.RequiredOnOfferSubmitMessage,
            ct);
        if (legalError is not null)
            return (false, legalError);

        var mode = ResolveMode();
        if (mode == PolicyEnforcementMode.Off)
        {
            offer.Status = OfferStatus.PendingApproval;

            var contextJson = JsonSerializer.Serialize(new
            {
                offerId = offer.Id,
                recruitmentApplicationId = offer.RecruitmentApplicationId,
                offerNumber = offer.OfferNumber
            });

            if (IsLabScale())
            {
                offer.WorkflowInstanceId = await workflowStarter.StartByWorkflowCodeAsync(
                    offer.IsMeritOverride ? OfferOverrideApprovalWorkflowCode : OfferApprovalWorkflowCode,
                    "OfferLetter",
                    offer.Id,
                    offer.TenantId,
                    actorUserId,
                    actorUserName,
                    contextJson,
                    PolicyDomains.CoreRecruitment,
                    ct);
            }

            await db.SaveChangesAsync(ct);
            return (true, null);
        }

        var policyContextJson = JsonSerializer.Serialize(new
        {
            offerId = offer.Id,
            recruitmentApplicationId = offer.RecruitmentApplicationId,
            offerNumber = offer.OfferNumber
        });

        var policyRequest = PolicyDomainFactsBuilders.ForOfferLetter(
            offer.Id,
            offer.TenantId,
            "Issue",
            offer.RecruitmentApplicationId,
            policyContextJson,
            offer.IsMeritOverride);
        policyRequest.Mode = mode;
        policyRequest.ActorUserId = actorUserId;
        policyRequest.ActorUserName = actorUserName;

        var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
        if (!policyResult.Allowed)
            return (false, policyResult.Decision.DenyReason ?? "مرفوض بسياسة العرض.");

        offer.Status = OfferStatus.PendingApproval;
        offer.WorkflowInstanceId = policyResult.WorkflowInstanceId;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AcceptAsync(Guid offerId, CancellationToken ct = default)
    {
        var offer = await LoadOfferAsync(offerId, ct);
        if (offer is null) return (false, "خطاب العرض غير موجود.");
        if (!OfferWorkflowRules.CanAccept(offer))
            return (false, offer.Status == OfferStatus.Issued ? "انتهت صلاحية العرض ولا يمكن قبوله." : "لا يمكن قبول هذا العرض.");
        if (offer.RecruitmentApplication!.Status != RecruitmentApplicationStatus.OfferExtended)
            return (false, "لا يمكن قبول العرض إلا لطلب في حالة «عرض مُمدَّد».");

        offer.Status = OfferStatus.Accepted;
        offer.AcceptedDate = DateTime.UtcNow;

        var app = offer.RecruitmentApplication!;
        app.Status = RecruitmentApplicationStatus.OfferAccepted;
        app.ReviewedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// HCM-027 (G-06): the candidate declined an issued offer (recorded by HR on their behalf until PLAN-30).
    /// The application becomes OfferDeclined; the offer stays in its history.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeclineAsync(
        Guid offerId,
        string? reason,
        string declinedBy,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var offer = await LoadOfferAsync(offerId, ct);
        if (offer is null) return (false, "خطاب العرض غير موجود.");
        if (!OfferWorkflowRules.CanDecline(offer))
            return (false, "لا يمكن رفض هذا العرض.");
        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < OfferWorkflowRules.MinDeclineReasonLength)
            return (false, $"سبب الرفض مطلوب ({OfferWorkflowRules.MinDeclineReasonLength} أحرف على الأقل).");
        if (trimmed.Length > 1000)
            return (false, "سبب الرفض يجب ألا يتجاوز 1000 حرف.");
        if (!OfferDeclinedBy.IsValid(declinedBy))
            return (false, "جهة الرفض غير صالحة.");

        var now = DateTime.UtcNow;
        offer.Status = OfferStatus.Declined;
        offer.DeclineReason = trimmed;
        offer.DeclinedAt = now;
        offer.DeclinedBy = declinedBy;
        SettleApplication(offer, RecruitmentApplicationStatus.OfferDeclined, $"رفض العرض: {trimmed}", now);
        AddAudit(offer, "DeclineOffer", actorUserId, new { reason = trimmed, declinedBy });
        // PLAN-30: notify candidate

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// HCM-027 (G-06): HR withdraws a Draft/PendingApproval/Issued offer. An in-progress approval is cancelled.
    /// <paramref name="revertToRanked"/> (administrative error) returns the candidate to Ranked; otherwise the
    /// application is Rejected.
    /// </summary>
    public async Task<(bool Success, string? Error)> RevokeAsync(
        Guid offerId,
        string? reason,
        bool revertToRanked,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var offer = await LoadOfferAsync(offerId, ct);
        if (offer is null) return (false, "خطاب العرض غير موجود.");
        if (!OfferWorkflowRules.CanRevoke(offer))
            return (false, "لا يمكن إلغاء هذا العرض.");
        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < OfferWorkflowRules.MinRevokeReasonLength)
            return (false, $"سبب الإلغاء مطلوب ({OfferWorkflowRules.MinRevokeReasonLength} أحرف على الأقل).");
        if (trimmed.Length > 1000)
            return (false, "سبب الإلغاء يجب ألا يتجاوز 1000 حرف.");
        if (revertToRanked && offer.RecruitmentApplication?.Rank is null)
            return (false, "لا يمكن إعادة المرشح إلى المرتبين دون ترتيب.");

        var now = DateTime.UtcNow;
        offer.Status = OfferStatus.Revoked;
        offer.RevokeReason = trimmed;
        offer.RevokedAt = now;

        if (offer.WorkflowInstanceId is Guid instanceId)
        {
            var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
            if (instance is { Status: WorkflowStatus.InProgress })
            {
                instance.Status = WorkflowStatus.Cancelled;
                instance.CompletedAt = now;
            }
        }

        SettleApplication(
            offer,
            revertToRanked ? RecruitmentApplicationStatus.Ranked : RecruitmentApplicationStatus.Rejected,
            revertToRanked ? $"إلغاء العرض (خطأ إداري): {trimmed}" : $"إلغاء العرض: {trimmed}",
            now);
        AddAudit(offer, "RevokeOffer", actorUserId, new { reason = trimmed, revertToRanked });
        // PLAN-30: notify candidate

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>HCM-027 (G-06): an issued offer past its expiry date becomes Expired; the application OfferDeclined.</summary>
    public async Task<(bool Success, string? Error)> ExpireAsync(Guid offerId, DateTime nowUtc, CancellationToken ct = default)
    {
        var offer = await LoadOfferAsync(offerId, ct);
        if (offer is null) return (false, "خطاب العرض غير موجود.");
        if (!OfferWorkflowRules.CanExpire(offer, nowUtc))
            return (false, "لا يمكن إنهاء صلاحية عرض غير صادر أو لم تنتهِ صلاحيته بعد.");

        ApplyExpiry(offer, nowUtc);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Stages the expiry of a loaded offer (with its application) without saving. Shared by
    /// <see cref="ExpireAsync"/> and the offer expiry job.
    /// </summary>
    internal void ApplyExpiry(OfferLetter offer, DateTime nowUtc)
    {
        offer.Status = OfferStatus.Expired;
        offer.ExpiredAt = nowUtc;
        SettleApplication(offer, RecruitmentApplicationStatus.OfferDeclined, ExpiryNote, nowUtc);
        AddAudit(offer, "ExpireOffer", actorUserId: null, new { expiryDate = offer.ExpiryDate });
        // PLAN-30: notify candidate
    }

    /// <summary>Moves the application off this offer; the offer stays in OfferLetters as history.</summary>
    private static void SettleApplication(OfferLetter offer, RecruitmentApplicationStatus status, string note, DateTime now)
    {
        var app = offer.RecruitmentApplication;
        if (app is null)
            return;

        if (app.ActiveOfferLetterId is null || app.ActiveOfferLetterId == offer.Id)
        {
            app.Status = status;
            app.ReviewedAt = now;
            app.ActiveOfferLetterId = null;
            app.ActiveOfferLetter = null;
        }

        app.Notes = string.IsNullOrWhiteSpace(app.Notes) ? note : $"{app.Notes}\n{note}";
    }

    private void AddAudit(OfferLetter offer, string action, Guid? actorUserId, object changes) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = offer.TenantId,
            EntityType = nameof(OfferLetter),
            EntityId = offer.Id,
            Action = action,
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(changes)
        });

    /// <summary>
    /// HCM-027: lowest rank among the requisition's applications that can still receive an offer
    /// (Ranked or ReserveListed, ranked, no pending appeal); null when none.
    /// </summary>
    public Task<int?> GetBestAvailableRankAsync(Guid requisitionId, CancellationToken ct = default) =>
        db.RecruitmentApplications
            .AsNoTracking()
            .Where(a => a.RequisitionId == requisitionId
                && (a.Status == RecruitmentApplicationStatus.Ranked || a.Status == RecruitmentApplicationStatus.ReserveListed)
                && a.Rank != null
                && a.AppealStatus != RecruitmentAppealStatus.Pending)
            .MinAsync(a => a.Rank, ct);

    private Task<bool> HasOtherActiveOfferAsync(Guid requisitionId, Guid applicationId, CancellationToken ct) =>
        db.OfferLetters.AnyAsync(
            o => o.RecruitmentApplicationId != applicationId
                && o.RecruitmentApplication != null
                && o.RecruitmentApplication.RequisitionId == requisitionId
                && (o.Status == OfferStatus.Draft
                    || o.Status == OfferStatus.PendingApproval
                    || o.Status == OfferStatus.Issued
                    || (o.Status == OfferStatus.Accepted && !o.IsConvertedToEmployee)),
            ct);

    private Task<OfferLetter?> LoadOfferAsync(Guid offerId, CancellationToken ct) =>
        db.OfferLetters
            .Include(o => o.RecruitmentApplication)
                .ThenInclude(a => a!.Requisition)
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);

    private PolicyEnforcementMode ResolveMode()
    {
        var raw = configuration["PolicyEngine:Core.Recruitment"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? nameof(PolicyEnforcementMode.Off);
        return Enum.TryParse<PolicyEnforcementMode>(raw, true, out var mode)
            ? mode
            : PolicyEnforcementMode.Off;
    }

    private bool IsLabScale()
    {
        var value = configuration["NationalScope:IsLabScale"];
        return bool.TryParse(value, out var parsed) && parsed;
    }
}
