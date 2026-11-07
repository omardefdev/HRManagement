using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Domain.Services;
using Compliance.Infrastructure.Consumers;
using Compliance.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Compliance.Infrastructure.Services;

/// <summary>
/// PLAN-46 (section 4): the disciplinary action's side of the contract. A draft or a rejected action is submitted to
/// <c>DISCIPLINARY-APPROVAL</c> (compliance, then the authority, two different people, the creator excluded); the approval alone sends it
/// to payroll, once, in the transaction of the status change. Every call runs under the action's tenant.
/// </summary>
public sealed class DisciplinaryApprovalService(
    ComplianceDbContext db,
    IWorkflowApprovalClient client,
    IPublishEndpoint publish,
    ILogger<DisciplinaryApprovalService> logger)
{
    public const string EntityType = "DisciplinaryAction";
    public const string WorkflowCode = "DISCIPLINARY-APPROVAL";

    public const string NotSubmittableMessage = "يُقدَّم الإجراء وهو مسودة أو بعد رفضه فقط.";
    public const string NotPendingMessage = "لا يوجد اعتماد جارٍ لهذا الإجراء.";

    /// <param name="employeeName">For the title the inbox shows; never a national number.</param>
    public async Task<WorkflowApprovalSubmitResult> SubmitAsync(
        DisciplinaryAction action, Guid submitter, string? employeeName, CancellationToken ct = default)
    {
        if (!DisciplinaryWorkflowRules.CanSubmit(action.ApprovalStatus))
            return new WorkflowApprovalSubmitResult(false, NotSubmittableMessage);

        using var tenant = WorkflowApprovalSubmission.UseTenant(action.TenantId);
        return await WorkflowApprovalSubmission.SubmitAsync(client, action.SubmissionNo, new WorkflowApprovalSubmitSteps
        {
            Request = number => StartRequest(action, submitter, employeeName, number),
            LinkAndSaveAsync = async (instanceId, number, token) =>
            {
                action.ApprovalStatus = DisciplinaryApprovalStatus.PendingApproval;
                action.WorkflowInstanceId = instanceId;
                action.SubmissionNo = number;
                action.SubmittedBy = submitter;
                action.SubmittedAt = DateTime.UtcNow;
                action.RejectionReason = null;
                await db.SaveChangesAsync(token);
            },
            SpendNumberAsync = async (number, token) =>
            {
                action.SubmissionNo = number;
                await db.SaveChangesAsync(token);
            },
            IsLinkedToAsync = (instanceId, token) =>
                db.DisciplinaryActions.AsNoTracking().AnyAsync(a => a.Id == action.Id && a.WorkflowInstanceId == instanceId
                    && a.ApprovalStatus != DisciplinaryApprovalStatus.Draft, token),
            ApplyOutcomeAsync = ApplyOutcomeAsync
        }, logger, ct);
    }

    public async Task<WorkflowApprovalWithdrawResult> WithdrawAsync(
        DisciplinaryAction action, Guid? actor, string reason, CancellationToken ct = default)
    {
        if (!DisciplinaryWorkflowRules.CanWithdraw(action.ApprovalStatus) || action.WorkflowInstanceId is not Guid instanceId)
            return new WorkflowApprovalWithdrawResult(false, NotPendingMessage);

        using var tenant = WorkflowApprovalSubmission.UseTenant(action.TenantId);
        return await WorkflowApprovalSubmission.WithdrawAsync(
            client, EntityType, action.Id, instanceId, actor, reason, ApplyOutcomeAsync, ct);
    }

    /// <summary>
    /// The one place an outcome changes an action, for the consumer and the sweep alike. Approved stages <c>IDisciplinaryActionTaken</c>
    /// with the status in one save: inside the consumer through its outbox, in the sweep through the bus outbox. An approval that names the
    /// creator or the submitter is refused and logged; an action already published is never published again.
    /// </summary>
    public async Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct = default)
    {
        var action = await db.DisciplinaryActions.FirstOrDefaultAsync(a => a.Id == outcome.EntityId, ct);
        var pending = action is { ApprovalStatus: DisciplinaryApprovalStatus.PendingApproval };
        if (!WorkflowOutcomeGuard.ShouldApply(action?.WorkflowInstanceId, pending, outcome, logger))
            return;

        switch (outcome.Outcome)
        {
            case WorkflowApprovalOutcomes.Approved:
                if (WorkflowOutcomeGuard.HasExcludedDecider(outcome, [action!.CreatedBy, action.SubmittedBy], logger))
                {
                    logger.LogError("Disciplinary action {ActionId}: an approval by its creator or submitter was refused.", action.Id);
                    return;
                }

                action.ApprovalStatus = DisciplinaryApprovalStatus.Approved;
                if (action.OutcomePublishedAt is null && !action.ApprovedBeforeEngine)
                {
                    await DisciplinaryApprovalOutcomeConsumer.StageActionTakenAsync(publish, action, ct);
                    action.OutcomePublishedAt = DateTime.UtcNow;
                }

                break;
            case WorkflowApprovalOutcomes.Rejected:
                action!.ApprovalStatus = DisciplinaryApprovalStatus.Rejected;
                action.RejectionReason = outcome.Reason;
                break;
            default:
                // Withdrawn: back to a draft that can be edited and submitted again under the next number.
                action!.ApprovalStatus = DisciplinaryApprovalStatus.Draft;
                break;
        }

        action.DecidedBy = outcome.DecidedByUserId;
        action.DecidedAt = outcome.DecidedAt;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Disciplinary action {ActionId} is {Status} after instance {InstanceId}.", action.Id, action.ApprovalStatus, outcome.InstanceId);
    }

    private static WorkflowApprovalStartRequest StartRequest(DisciplinaryAction action, Guid submitter, string? employeeName, int number)
    {
        var excluded = new[] { action.CreatedBy, submitter }
            .Where(id => id is not null && id != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var type = TypeLabel(action.ActionType);
        var title = string.IsNullOrWhiteSpace(employeeName) || employeeName.Any(char.IsDigit)
            ? $"إجراء تأديبي — {type}"
            : $"إجراء تأديبي — {employeeName.Trim()} — {type}";

        var facts = new Dictionary<string, string>
        {
            ["actionType"] = type,
            ["employeeId"] = action.EmployeeId.ToString("D"),
            ["issuedAt"] = action.IssuedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (action.ComplaintId is Guid complaint)
            facts["complaintId"] = complaint.ToString("D");

        return new WorkflowApprovalStartRequest(
            EntityType,
            action.Id,
            WorkflowCode,
            $"{EntityType}:{action.Id:D}:{number}",
            excluded,
            null,
            title.Length > 300 ? title[..300] : title,
            $"/ui/compliance/DisciplinaryActions/Details/{action.Id:D}",
            facts,
            action.EmployeeId);
    }

    private static string TypeLabel(ActionType type) => type switch
    {
        ActionType.Warning => "إنذار",
        ActionType.Suspension => "إيقاف",
        ActionType.Dismissal => "فصل",
        _ => type.ToString()
    };
}
