using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentRequisitionLifecycleService(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    RecruitmentLegalSourceValidator legalSourceValidator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration,
    IOptions<RecruitmentOptions> recruitmentOptions)
{
    private const string RequisitionApprovalWorkflowCode = "RECRUITMENT-REQUISITION-APPROVAL";
    public async Task<(bool Success, string? Error)> SubmitForApprovalAsync(
        RecruitmentRequisition requisition,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(requisition.Id, ct);
        if (loaded is null) return (false, "طلب الشاغر غير موجود.");

        var (req, position) = loaded.Value;
        if (!RequisitionWorkflowRules.CanSubmit(req.Status, position))
            return (false, "لا يمكن تقديم الطلب: يجب أن تكون المسودة مربوطة بمنصب شاغر.");

        var legalError = await legalSourceValidator.ValidateRequiredAsync(req.LegalSourceId, ct);
        if (legalError is not null)
            return (false, legalError);

        var mode = ResolveMode();
        if (mode == PolicyEnforcementMode.Off)
        {
            req.Status = RequisitionStatus.PendingApproval;

            var contextJson = JsonSerializer.Serialize(new
            {
                requisitionId = req.Id,
                fundedPositionId = req.FundedPositionId,
                requisitionNumber = req.RequisitionNumber
            });

            if (IsLabScale())
            {
                req.WorkflowInstanceId = await workflowStarter.StartByWorkflowCodeAsync(
                    RequisitionApprovalWorkflowCode,
                    "RecruitmentRequisition",
                    req.Id,
                    req.TenantId,
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
            requisitionId = req.Id,
            fundedPositionId = req.FundedPositionId,
            requisitionNumber = req.RequisitionNumber
        });

        var policyRequest = PolicyDomainFactsBuilders.ForRecruitmentRequisition(
            req.Id,
            req.TenantId,
            "SubmitForApproval",
            req.Status.ToString(),
            position.Status == FundedPositionStatus.Vacant,
            req.FundedPositionId,
            contextJson: policyContextJson);
        policyRequest.Mode = mode;
        policyRequest.ActorUserId = actorUserId;
        policyRequest.ActorUserName = actorUserName;

        var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
        if (!policyResult.Allowed)
            return (false, policyResult.Decision.DenyReason ?? "مرفوض بسياسة التوظيف.");

        req.Status = RequisitionStatus.PendingApproval;
        req.WorkflowInstanceId = policyResult.WorkflowInstanceId;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <param name="applicationClosingDate">HCM-026: sets the closing date at first publish when the requisition has none.</param>
    public async Task<(bool Success, string? Error)> PublishAsync(
        RecruitmentRequisition requisition,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default,
        DateOnly? applicationClosingDate = null)
    {
        var loaded = await LoadAsync(requisition.Id, ct);
        if (loaded is null) return (false, "طلب الشاغر غير موجود.");

        var (req, position) = loaded.Value;
        var hasPublishedPosting = await db.PublicJobPostings
            .AnyAsync(p => p.RequisitionId == req.Id && p.IsPublished, ct);

        if (!RequisitionWorkflowRules.CanPublish(req.Status, position, hasPublishedPosting, req.PublishingChannel))
            return (false, "لا يمكن النشر العام: تحقق من الحالة والقناة وعدم وجود إعلان منشور.");

        if (RecruitmentLegalSourceRules.IsEmpty(req.LegalSourceId))
            return (false, RecruitmentLegalSourceRules.RequiredOnPublishMessage);

        var criteriaError = await ValidateCriteriaBeforePublishAsync(req.Id, ct);
        if (criteriaError is not null)
            return (false, criteriaError);

        var closingDateError = ApplyClosingDateForPublish(req, applicationClosingDate);
        if (closingDateError is not null)
            return (false, closingDateError);

        var mode = ResolveMode();
        if (mode != PolicyEnforcementMode.Off)
        {
            var policyRequest = PolicyDomainFactsBuilders.ForRecruitmentRequisition(
                req.Id,
                req.TenantId,
                "Publish",
                req.Status.ToString(),
                position.Status == FundedPositionStatus.Vacant,
                req.FundedPositionId);
            policyRequest.Mode = mode;
            policyRequest.ActorUserId = actorUserId;
            policyRequest.ActorUserName = actorUserName;

            var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
            if (!policyResult.Allowed)
                return (false, policyResult.Decision.DenyReason ?? "مرفوض بسياسة النشر.");
        }

        var posting = req.PublicJobPostingId is Guid postingId
            ? await db.PublicJobPostings.FirstOrDefaultAsync(p => p.Id == postingId, ct)
            : null;

        if (posting is null)
        {
            posting = RecruitmentRequisitionService.MapToPublicJobPosting(req, position);
            db.PublicJobPostings.Add(posting);
        }
        else
        {
            var mapped = RecruitmentRequisitionService.MapToPublicJobPosting(req, position);
            posting.Title = mapped.Title;
            posting.Entity = mapped.Entity;
            posting.Location = mapped.Location;
            posting.Description = mapped.Description;
            posting.Requirements = mapped.Requirements;
            posting.ClosingDate = mapped.ClosingDate;
            posting.RequisitionId = req.Id;
            posting.IsPublished = true;
        }

        req.Status = RequisitionStatus.Published;
        req.PublishedAt ??= DateTime.UtcNow;
        req.PublicJobPosting = posting;
        req.PublicJobPostingId = posting.Id;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <param name="applicationClosingDate">HCM-026: sets the closing date at first publish when the requisition has none.</param>
    public async Task<(bool Success, string? Error)> PublishInternalAsync(
        RecruitmentRequisition requisition,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default,
        DateOnly? applicationClosingDate = null)
    {
        var loaded = await LoadAsync(requisition.Id, ct);
        if (loaded is null) return (false, "طلب الشاغر غير موجود.");

        var (req, position) = loaded.Value;
        var hasInternalVacancy = req.InternalVacancyId.HasValue
            || await db.InternalVacancies.AnyAsync(v => v.RequisitionId == req.Id, ct);

        if (!RequisitionWorkflowRules.CanPublishInternal(req.Status, position, hasInternalVacancy, req.PublishingChannel))
            return (false, "لا يمكن النشر الداخلي: تحقق من الحالة والقناة وعدم وجود شاغر داخلي مسبق.");

        if (RecruitmentLegalSourceRules.IsEmpty(req.LegalSourceId))
            return (false, RecruitmentLegalSourceRules.RequiredOnPublishMessage);

        var criteriaError = await ValidateCriteriaBeforePublishAsync(req.Id, ct);
        if (criteriaError is not null)
            return (false, criteriaError);

        var closingDateError = ApplyClosingDateForPublish(req, applicationClosingDate);
        if (closingDateError is not null)
            return (false, closingDateError);

        var mode = ResolveMode();
        if (mode != PolicyEnforcementMode.Off)
        {
            var policyRequest = PolicyDomainFactsBuilders.ForRecruitmentRequisition(
                req.Id,
                req.TenantId,
                "PublishInternal",
                req.Status.ToString(),
                position.Status == FundedPositionStatus.Vacant,
                req.FundedPositionId);
            policyRequest.Mode = mode;
            policyRequest.ActorUserId = actorUserId;
            policyRequest.ActorUserName = actorUserName;

            var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
            if (!policyResult.Allowed)
                return (false, policyResult.Decision.DenyReason ?? "مرفوض بسياسة النشر الداخلي.");
        }

        var vacancy = req.InternalVacancyId is Guid vacancyId
            ? await db.InternalVacancies.FirstOrDefaultAsync(v => v.Id == vacancyId, ct)
            : null;

        if (vacancy is null)
        {
            vacancy = RecruitmentRequisitionService.MapToInternalVacancy(req, position);
            db.InternalVacancies.Add(vacancy);
        }
        else
        {
            var mapped = RecruitmentRequisitionService.MapToInternalVacancy(req, position);
            vacancy.Title = mapped.Title;
            vacancy.OrgUnitId = mapped.OrgUnitId;
            vacancy.Description = mapped.Description;
            vacancy.RequiredQualifications = mapped.RequiredQualifications;
            vacancy.ClosingDate = mapped.ClosingDate;
            vacancy.RequisitionId = req.Id;
            vacancy.IsDerivedFromRequisition = true;
            vacancy.IsOpen = true;
        }

        if (req.Status == RequisitionStatus.Approved)
        {
            req.Status = RequisitionStatus.Published;
            req.PublishedAt ??= DateTime.UtcNow;
        }

        req.InternalVacancy = vacancy;
        req.InternalVacancyId = vacancy.Id;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CloseAsync(Guid requisitionId, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(requisitionId, ct);
        if (loaded is null) return (false, "طلب الشاغر غير موجود.");

        var (req, _) = loaded.Value;
        if (!RequisitionWorkflowRules.CanClose(req.Status))
            return (false, "يمكن إغلاق الطلبات المنشورة فقط.");

        // HCM-026: an offer still in flight (or accepted but not converted) must be revoked or completed first.
        var hasActiveOffer = await db.OfferLetters.AnyAsync(
            o => o.RecruitmentApplication != null
                && o.RecruitmentApplication.RequisitionId == req.Id
                && (o.Status == OfferStatus.Draft
                    || o.Status == OfferStatus.PendingApproval
                    || o.Status == OfferStatus.Issued
                    || (o.Status == OfferStatus.Accepted && !o.IsConvertedToEmployee)),
            ct);
        if (hasActiveOffer)
            return (false, "يوجد عرض توظيف قيد الإجراء على هذا الطلب — ألغِ العرض أو أكمل التعيين قبل الإغلاق.");

        var now = DateTime.UtcNow;
        req.Status = RequisitionStatus.Closed;
        req.ClosedDate = now;

        await RecruitmentRequisitionCascade.UnpublishAsync(db, req, ct);
        // HCM-027: OfferDeclined is terminal and left as is; ReserveListed is open, so closing the requisition
        // ends its reserve list (reserve candidates are rejected with the other open applications).
        await RecruitmentRequisitionCascade.RejectOpenApplicationsAsync(db, req.Id, "إغلاق الشاغر", now, ct);
        await RecruitmentRequisitionCascade.CancelScheduledInterviewsAsync(db, req.Id, "إغلاق الشاغر", ct);

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// HCM-026 (G-11): Draft/PendingApproval need HrCoreWrite only; Approved/Published need
    /// <paramref name="actorCanApprove"/> (HrCoreApprove) and a written reason, and cascade to the posting,
    /// vacancy, offers, applications and interviews in one save.
    /// </summary>
    public async Task<(bool Success, string? Error)> CancelAsync(
        Guid requisitionId,
        string? reason,
        Guid? actorUserId,
        bool actorCanApprove,
        CancellationToken ct = default)
    {
        var req = await db.RecruitmentRequisitions.FirstOrDefaultAsync(r => r.Id == requisitionId, ct);
        if (req is null) return (false, "طلب الشاغر غير موجود.");

        if (!RequisitionWorkflowRules.CanCancel(req.Status))
            return (false, "لا يمكن إلغاء طلب شاغر مغلق أو ملغى.");

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is { Length: > 500 })
            return (false, "سبب الإلغاء يجب ألا يتجاوز 500 حرف.");

        if (RequisitionWorkflowRules.CancelRequiresApproval(req.Status))
        {
            if (!actorCanApprove)
                return (false, "إلغاء طلب شاغر معتمد أو منشور يتطلب صلاحية الاعتماد.");
            if (trimmedReason is null || trimmedReason.Length < RequisitionWorkflowRules.MinCancellationReasonLength)
                return (false, $"سبب الإلغاء مطلوب ({RequisitionWorkflowRules.MinCancellationReasonLength} أحرف على الأقل).");
        }

        var blocked = await RecruitmentRequisitionCascade.ValidateCancellableAsync(db, req.Id, ct);
        if (blocked is not null)
            return (false, blocked);

        await RecruitmentRequisitionCascade.StageCancellationAsync(db, req, trimmedReason ?? "إلغاء المسودة", actorUserId, ct);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private async Task<(RecruitmentRequisition Req, FundedPosition Position)?> LoadAsync(Guid id, CancellationToken ct)
    {
        var req = await db.RecruitmentRequisitions
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.OrgUnit)
            .Include(r => r.FundedPosition!)
                .ThenInclude(p => p.JobCatalogEntry)
            .Include(r => r.PublicJobPosting)
            .Include(r => r.InternalVacancy)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (req?.FundedPosition is null)
            return null;

        return (req, req.FundedPosition);
    }

    /// <summary>
    /// HCM-026 (E3): extends the application window (later date only, with a reason) and reopens intake
    /// if the intake-close job had already closed it while the requisition is still Published on a vacant position.
    /// </summary>
    public async Task<(bool Success, string? Error)> ExtendClosingDateAsync(
        Guid requisitionId,
        DateOnly newClosingDate,
        string? reason,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(requisitionId, ct);
        if (loaded is null) return (false, "طلب الشاغر غير موجود.");

        var (req, position) = loaded.Value;
        if (req.Status is not (RequisitionStatus.Approved or RequisitionStatus.Published))
            return (false, "يمكن تمديد تاريخ الإغلاق للطلبات المعتمدة أو المنشورة فقط.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var error = RequisitionWorkflowRules.ValidateClosingDateExtension(req.ApplicationClosingDate, newClosingDate, today, reason);
        if (error is not null)
            return (false, error);

        var previous = req.ApplicationClosingDate;
        req.ApplicationClosingDate = newClosingDate;

        var postings = await db.PublicJobPostings
            .Where(p => p.RequisitionId == req.Id || p.Id == req.PublicJobPostingId)
            .ToListAsync(ct);
        var vacancies = await db.InternalVacancies
            .Where(v => v.RequisitionId == req.Id || v.Id == req.InternalVacancyId)
            .ToListAsync(ct);
        foreach (var posting in postings)
            posting.ClosingDate = newClosingDate;
        foreach (var vacancy in vacancies)
            vacancy.ClosingDate = newClosingDate;

        var reopened = false;
        if (req.IntakeClosedAt is not null
            && req.Status == RequisitionStatus.Published
            && position.Status == FundedPositionStatus.Vacant)
        {
            req.IntakeClosedAt = null;
            foreach (var posting in postings)
                posting.IsPublished = true;
            foreach (var vacancy in vacancies)
                vacancy.IsOpen = true;
            reopened = true;
        }

        db.AuditLogs.Add(new HRManagement.SharedKernel.Entities.EntityAuditLog
        {
            TenantId = req.TenantId,
            EntityType = nameof(RecruitmentRequisition),
            EntityId = req.Id,
            Action = "ExtendApplicationClosingDate",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new
            {
                previousClosingDate = previous?.ToString("yyyy-MM-dd"),
                newClosingDate = newClosingDate.ToString("yyyy-MM-dd"),
                reason = reason!.Trim(),
                intakeReopened = reopened
            })
        });

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>HCM-026 (E1): closing date is required to publish; at first publish it must leave MinPostingDays of intake.</summary>
    private string? ApplyClosingDateForPublish(RecruitmentRequisition req, DateOnly? provided)
    {
        var isFirstPublish = req.PublishedAt is null;
        var candidate = isFirstPublish && provided is not null ? provided : req.ApplicationClosingDate;
        var error = RequisitionWorkflowRules.ValidateClosingDateForPublish(
            candidate,
            isFirstPublish,
            DateOnly.FromDateTime(DateTime.UtcNow),
            recruitmentOptions.Value.MinPostingDays);
        if (error is null)
            req.ApplicationClosingDate = candidate;
        return error;
    }

    /// <summary>HCM-026 (A4): publication requires an active assessment criteria version.</summary>
    private async Task<string?> ValidateCriteriaBeforePublishAsync(Guid requisitionId, CancellationToken ct)
    {
        if (!recruitmentOptions.Value.RequireCriteriaBeforePublish)
            return null;

        var hasActive = await db.RecruitmentAssessmentCriteria
            .AnyAsync(c => c.RequisitionId == requisitionId && c.IsActive, ct);
        return hasActive ? null : AssessmentCriteriaRules.RequiredBeforePublishMessage;
    }

    private PolicyEnforcementMode ResolveMode()
    {
        var mode = configuration["PolicyEngine:Core.Recruitment"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? "Off";
        return Enum.TryParse<PolicyEnforcementMode>(mode, true, out var parsed)
            ? parsed
            : PolicyEnforcementMode.Off;
    }

    private bool IsLabScale()
    {
        var value = configuration["NationalScope:IsLabScale"];
        return bool.TryParse(value, out var parsed) && parsed;
    }
}
