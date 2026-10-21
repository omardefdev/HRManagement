using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

public sealed class OfferLetterWorkflowUiService(
    HRCoreDbContext db,
    EstablishmentScopeService establishmentScope)
{
    public async Task<OfferLetterDetailsViewModel> BuildDetailsAsync(
        OfferLetter offer,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, offer.WorkflowInstanceId, ct);
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        var accessScope = await establishmentScope.GetScopeAsync(user, ct);
        var orgUnitId = offer.FundedPosition?.OrgUnitId;

        var workflowPanel = WorkflowPanelBuilder.Build(
            instance,
            definition,
            user,
            userId,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = offer.Status == OfferStatus.PendingApproval,
                EntityCreatedBy = offer.CreatedBy,
                CreatorSoDMessage = "لا يمكن للمنشئ اعتماد عرضه الخاص.",
                MissingWorkflowWarning = offer.WorkflowInstanceId is null
                    ? "لم يُنشأ مسار اعتماد لهذا العرض. فعّل PolicyEngine:Core.Recruitment أو استخدم بيئة المختبر (IsLabScale)."
                    : null,
                AdditionalBlockReason = (_, _, _) => WorkflowInboxOrgScope.GetBlockReason(accessScope, orgUnitId)
            });

        return new OfferLetterDetailsViewModel
        {
            Offer = offer,
            CanSubmit = OfferWorkflowRules.CanSubmitForApproval(offer),
            CanAccept = OfferWorkflowRules.CanAccept(offer),
            CanDecline = OfferWorkflowRules.CanDecline(offer),
            CanRevoke = OfferWorkflowRules.CanTransition(offer.Status, OfferStatus.Revoked),
            Workflow = workflowPanel
        };
    }
}
