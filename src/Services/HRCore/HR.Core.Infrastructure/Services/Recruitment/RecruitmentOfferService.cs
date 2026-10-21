using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
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
    IOptions<RecruitmentOptions> recruitmentOptions,
    IPayrollGradeLookup payrollGradeLookup)
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
        string? gradeDeviationReason = null,
        string? salaryJustification = null,
        CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.FundedPosition)
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

        // HCM-027 (E): grade from the funded position, salary within the Payroll grade bounds.
        var (terms, termsError) = await CheckTermsAsync(
            offeredSalary,
            gradeCode,
            app.Requisition.FundedPosition?.GradeCode,
            proposedStartDate,
            gradeDeviationReason,
            salaryJustification,
            ct);
        if (termsError is not null)
            return (null, termsError);

        var code = await codeGenerator.GenerateAsync(CodeGenerationRequests.OfferLetter(), ct);
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id,
            FundedPositionId = app.Requisition.FundedPositionId,
            OfferNumber = code.Code,
            Status = OfferStatus.Draft,
            OfferedSalary = offeredSalary,
            GradeCode = terms!.Grade,
            ProposedStartDate = proposedStartDate,
            ValidityDays = validity,
            LegalSourceId = app.Requisition.LegalSourceId!.Trim(),
            IsMeritOverride = isMeritOverride,
            MeritOverrideReason = isMeritOverride ? meritOverrideReason!.Trim() : null,
            GradeDeviationReason = terms.GradeDeviationReason,
            SalaryUnverified = terms.SalaryUnverified,
            SalaryJustification = terms.SalaryJustification,
            GradeMinSalary = terms.MinSalary,
            GradeMaxSalary = terms.MaxSalary,
            SalaryCheckedAt = terms.CheckedAt,
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
                    offer.IsExceptionOffer ? OfferOverrideApprovalWorkflowCode : OfferApprovalWorkflowCode,
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
            offer.IsMeritOverride,
            offer.IsExceptionOffer);
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

    /// <summary>Validated offer terms (HCM-027 E).</summary>
    private sealed record OfferTerms(
        string Grade,
        string? GradeDeviationReason,
        bool SalaryUnverified,
        string? SalaryJustification,
        decimal? MinSalary,
        decimal? MaxSalary,
        DateTime? CheckedAt);

    /// <summary>
    /// HCM-027 (E1/E3/E4): start date window, grade from the funded position (a different grade needs a reason),
    /// salary within the Payroll grade bounds at the proposed start date. When Payroll is unavailable the offer
    /// may proceed only with a salary justification and is flagged <see cref="OfferLetter.SalaryUnverified"/>.
    /// </summary>
    private async Task<(OfferTerms? Terms, string? Error)> CheckTermsAsync(
        decimal offeredSalary,
        string? gradeCode,
        string? positionGrade,
        DateTime proposedStartDate,
        string? gradeDeviationReason,
        string? salaryJustification,
        CancellationToken ct)
    {
        var startError = OfferTermsRules.ValidateStartDate(
            proposedStartDate, DateOnly.FromDateTime(DateTime.UtcNow), recruitmentOptions.Value.MaxStartDays);
        if (startError is not null)
            return (null, startError);

        var grade = OfferTermsRules.ResolveGrade(gradeCode, positionGrade);
        if (grade is null)
            return (null, OfferTermsRules.MissingGradeMessage);

        string? deviationReason = null;
        if (OfferTermsRules.IsGradeDeviation(grade, positionGrade))
        {
            deviationReason = gradeDeviationReason?.Trim();
            if (deviationReason is null || deviationReason.Length < OfferTermsRules.MinGradeDeviationReasonLength)
                return (null, OfferTermsRules.GradeDeviationReasonMessage);
            if (deviationReason.Length > OfferTermsRules.MaxGradeDeviationReasonLength)
                return (null, $"مبرر اختلاف الدرجة يجب ألا يتجاوز {OfferTermsRules.MaxGradeDeviationReasonLength} حرف.");
        }

        var lookup = await payrollGradeLookup.GetAsync(grade, DateOnly.FromDateTime(proposedStartDate), ct);
        switch (lookup)
        {
            case PayrollGradeLookupResult.Found found:
                if (!OfferTermsRules.IsSalaryInRange(offeredSalary, found.MinSalary, found.MaxSalary))
                    return (null, OfferTermsRules.SalaryOutOfRangeMessage(grade, found.MinSalary, found.MaxSalary));
                return (new OfferTerms(grade, deviationReason, false, null, found.MinSalary, found.MaxSalary, DateTime.UtcNow), null);

            case PayrollGradeLookupResult.NotFound:
                return (null, OfferTermsRules.GradeNotFoundMessage(grade));

            default:
                var justification = salaryJustification?.Trim();
                if (justification is null || justification.Length < OfferTermsRules.MinSalaryJustificationLength)
                    return (null, OfferTermsRules.SalaryJustificationMessage);
                if (justification.Length > OfferTermsRules.MaxSalaryJustificationLength)
                    return (null, $"مبرر الراتب يجب ألا يتجاوز {OfferTermsRules.MaxSalaryJustificationLength} حرف.");
                return (new OfferTerms(grade, deviationReason, true, justification, null, null, null), null);
        }
    }

    /// <summary>
    /// HCM-027 (E5): grade of the application's funded position and its Payroll bounds at
    /// <paramref name="asOf"/>, for the offer form hint.
    /// </summary>
    public async Task<(string? PositionGrade, PayrollGradeLookupResult? Lookup)> GetOfferTermsHintAsync(
        Guid applicationId,
        DateOnly asOf,
        CancellationToken ct = default)
    {
        var positionGrade = await db.RecruitmentApplications
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => a.Requisition != null && a.Requisition.FundedPosition != null
                ? a.Requisition.FundedPosition.GradeCode
                : null)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(positionGrade))
            return (null, null);

        return (positionGrade, await payrollGradeLookup.GetAsync(positionGrade, asOf, ct));
    }

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

