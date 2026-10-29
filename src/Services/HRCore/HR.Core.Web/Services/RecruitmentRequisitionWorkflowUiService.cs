using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

/// <summary>PLAN-25 (G-04): approve/reject availability comes from the server evaluator (creator SoD, step role, branch scope).</summary>
public sealed class RecruitmentRequisitionWorkflowUiService(HRCoreDbContext db, WorkflowApprovalEvaluator? evaluator = null)
{
    private readonly WorkflowApprovalEvaluator _evaluator = evaluator ?? WorkflowApprovalEvaluator.CreateDefault(db);

    public async Task<RecruitmentRequisitionDetailsViewModel> BuildDetailsAsync(
        RecruitmentRequisition requisition,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, requisition.WorkflowInstanceId, ct);

        var workflowPanel = await WorkflowPanelBuilder.BuildAsync(
            instance,
            definition,
            user,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = requisition.Status == RequisitionStatus.PendingApproval,
                MissingWorkflowWarning = requisition.WorkflowInstanceId is null ? ApprovalRestartRules.StuckWarning : null
            },
            _evaluator,
            ct);

        return new RecruitmentRequisitionDetailsViewModel
        {
            Requisition = requisition,
            Workflow = workflowPanel
        };
    }
}
