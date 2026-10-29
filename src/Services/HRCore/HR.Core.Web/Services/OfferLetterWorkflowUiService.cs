using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

/// <summary>
/// PLAN-25 (G-04): approve/reject availability comes from the server evaluator (creator SoD, override distinct approvers,
/// step role, branch scope).
/// </summary>
public sealed class OfferLetterWorkflowUiService(HRCoreDbContext db, WorkflowApprovalEvaluator? evaluator = null)
{
    private readonly WorkflowApprovalEvaluator _evaluator = evaluator ?? WorkflowApprovalEvaluator.CreateDefault(db);

    public async Task<OfferLetterDetailsViewModel> BuildDetailsAsync(
        OfferLetter offer,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, offer.WorkflowInstanceId, ct);

        var workflowPanel = await WorkflowPanelBuilder.BuildAsync(
            instance,
            definition,
            user,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = offer.Status == OfferStatus.PendingApproval,
                MissingWorkflowWarning = offer.WorkflowInstanceId is null ? ApprovalRestartRules.StuckWarning : null
            },
            _evaluator,
            ct);

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
