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
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentOfferService(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    ICodeGenerator codeGenerator,
    RecruitmentLegalSourceValidator legalSourceValidator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration)
{
    private const string OfferApprovalWorkflowCode = "RECRUITMENT-OFFER-APPROVAL";

    public async Task<(OfferLetter? Offer, string? Error)> CreateDraftAsync(
        Guid applicationId,
        decimal offeredSalary,
        string? gradeCode,
        DateTime proposedStartDate,
        DateTime? expiryDate,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var app = await db.RecruitmentApplications
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (app is null) return (null, "الطلب غير موجود.");
        if (!RecruitmentAppealWorkflowRules.CanOffer(app))
            return (null, "لا يمكن إنشاء العرض أثناء استئناف معلق.");
        if (app.RequisitionId is null || app.Requisition?.FundedPositionId is null)
            return (null, "يجب ربط الطلب بطلب شاغر قبل إنشاء العرض.");

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
            ExpiryDate = expiryDate,
            LegalSourceId = app.Requisition.LegalSourceId!.Trim(),
            CreatedBy = actorUserId
        };

        db.OfferLetters.Add(offer);
        app.ActiveOfferLetter = offer;

        if (app.Status == RecruitmentApplicationStatus.Ranked)
        {
            app.Status = RecruitmentApplicationStatus.OfferExtended;
            app.ReviewedAt = DateTime.UtcNow;
        }

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
                    OfferApprovalWorkflowCode,
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
            policyContextJson);
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
            return (false, "لا يمكن قبول هذا العرض.");

        offer.Status = OfferStatus.Accepted;
        offer.AcceptedDate = DateTime.UtcNow;

        var app = offer.RecruitmentApplication!;
        app.Status = RecruitmentApplicationStatus.OfferAccepted;
        app.ReviewedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeclineAsync(Guid offerId, CancellationToken ct = default)
    {
        var offer = await LoadOfferAsync(offerId, ct);
        if (offer is null) return (false, "خطاب العرض غير موجود.");
        if (!OfferWorkflowRules.CanDecline(offer))
            return (false, "لا يمكن رفض هذا العرض.");

        offer.Status = OfferStatus.Declined;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RevokeAsync(Guid offerId, CancellationToken ct = default)
    {
        var offer = await LoadOfferAsync(offerId, ct);
        if (offer is null) return (false, "خطاب العرض غير موجود.");
        if (!OfferWorkflowRules.CanTransition(offer.Status, OfferStatus.Revoked))
            return (false, "لا يمكن إلغاء هذا العرض.");

        offer.Status = OfferStatus.Revoked;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

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
