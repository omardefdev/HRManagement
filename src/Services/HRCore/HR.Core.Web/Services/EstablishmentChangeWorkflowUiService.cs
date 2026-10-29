using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

/// <summary>PLAN-25 (G-04): approve/reject availability comes from the server evaluator (creator SoD, step role, central only).</summary>
public sealed class EstablishmentChangeWorkflowUiService(HRCoreDbContext db, WorkflowApprovalEvaluator? evaluator = null)
{
    private readonly WorkflowApprovalEvaluator _evaluator = evaluator ?? WorkflowApprovalEvaluator.CreateDefault(db);

    public async Task<EstablishmentChangeDetailsViewModel> BuildDetailsAsync(
        EstablishmentChangeRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, request.WorkflowInstanceId, ct);

        var workflowPanel = await WorkflowPanelBuilder.BuildAsync(
            instance,
            definition,
            user,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = request.Status == EstablishmentChangeStatus.PendingApproval,
                MissingWorkflowWarning = request.Status == EstablishmentChangeStatus.PendingApproval
                                         && request.WorkflowInstanceId is null
                    ? ApprovalRestartRules.StuckWarning
                    : null
            },
            _evaluator,
            ct);

        return new EstablishmentChangeDetailsViewModel
        {
            Request = request,
            Workflow = workflowPanel
        };
    }
}
