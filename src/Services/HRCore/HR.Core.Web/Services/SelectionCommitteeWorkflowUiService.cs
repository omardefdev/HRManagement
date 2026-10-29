using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

/// <summary>
/// PLAN-25 (G-04): approve/reject availability comes from the server evaluator (creator, member on the final step, quorum
/// on approve, step role, branch scope).
/// </summary>
public sealed class SelectionCommitteeWorkflowUiService(HRCoreDbContext db, WorkflowApprovalEvaluator? evaluator = null)
{
    private readonly WorkflowApprovalEvaluator _evaluator = evaluator ?? WorkflowApprovalEvaluator.CreateDefault(db);

    public async Task<SelectionCommitteeDetailsViewModel> BuildDetailsAsync(
        SelectionCommitteeSession session,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, session.WorkflowInstanceId, ct);

        var workflowPanel = await WorkflowPanelBuilder.BuildAsync(
            instance,
            definition,
            user,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = session.ApprovedAt is null
                    && (session.WorkflowInstanceId is not null || session.SubmittedAt is not null),
                MissingWorkflowWarning = session.WorkflowInstanceId is null && session.SubmittedAt is not null && session.ApprovedAt is null
                    ? ApprovalRestartRules.StuckWarning
                    : null
            },
            _evaluator,
            ct);

        return new SelectionCommitteeDetailsViewModel
        {
            Session = session,
            CanSubmit = session.ApprovedAt is null
                && !session.IsLocked
                && session.WorkflowInstanceId is null
                && session.Members.Any(),
            Workflow = workflowPanel
        };
    }
}
