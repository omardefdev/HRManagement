using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IDCard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IDCard.Infrastructure.Services;

/// <summary><c>IdCard:Approval</c>.</summary>
public sealed class CardApprovalOptions
{
    public const string SectionName = "IdCard:Approval";

    /// <summary>
    /// D-WF-1: false. A hire leaves a draft that a card office employee submits; a request made by the bus has no person to vouch for, so
    /// true is refused at start-up rather than silently ignored.
    /// </summary>
    public bool AutoSubmitOnHire { get; set; }
}

/// <summary>
/// PLAN-46 (section 3): the card request's side of the contract. A draft is submitted to <c>ID-CARD-ISSUANCE</c> with its requester and
/// submitter excluded; a pending request is withdrawn through the engine first; the outcome, from the event or from the sweep, is applied
/// here only. Every call runs under the request's tenant.
/// </summary>
public sealed class CardRequestApprovalService(
    IDCardDbContext db,
    IWorkflowApprovalClient client,
    ILogger<CardRequestApprovalService> logger)
{
    public const string EntityType = "CardRequest";
    public const string WorkflowCode = "ID-CARD-ISSUANCE";

    public const string NotDraftMessage = "يُقدَّم الطلب وهو مسودة فقط.";
    public const string NotPendingMessage = "لا يوجد اعتماد جارٍ لهذا الطلب.";
    public const string CancelledMessage = "الطلب ملغى.";

    public async Task<WorkflowApprovalSubmitResult> SubmitAsync(CardRequest request, Guid submitter, CancellationToken ct = default)
    {
        if (request.IsCancelled)
            return new WorkflowApprovalSubmitResult(false, CancelledMessage);
        if (!CardIssuanceWorkflow.CanSubmit(request.Status))
            return new WorkflowApprovalSubmitResult(false, NotDraftMessage);

        using var tenant = WorkflowApprovalSubmission.UseTenant(request.TenantId);
        return await WorkflowApprovalSubmission.SubmitAsync(client, request.SubmissionNo, new WorkflowApprovalSubmitSteps
        {
            Request = number => StartRequest(request, submitter, number),
            LinkAndSaveAsync = async (instanceId, number, token) =>
            {
                request.Status = CardRequestStatus.PendingApproval;
                request.WorkflowInstanceId = instanceId;
                request.SubmissionNo = number;
                request.SubmittedBy = submitter;
                request.SubmittedAt = DateTime.UtcNow;
                request.RejectionReason = null;
                await db.SaveChangesAsync(token);
            },
            SpendNumberAsync = async (number, token) =>
            {
                request.SubmissionNo = number;
                await db.SaveChangesAsync(token);
            },
            IsLinkedToAsync = (instanceId, token) =>
                db.CardRequests.AsNoTracking().AnyAsync(r => r.Id == request.Id && r.WorkflowInstanceId == instanceId
                    && r.Status != CardRequestStatus.Pending, token),
            ApplyOutcomeAsync = ApplyOutcomeAsync
        }, logger, ct);
    }

    public async Task<WorkflowApprovalWithdrawResult> WithdrawAsync(CardRequest request, Guid? actor, string reason, CancellationToken ct = default)
    {
        if (!CardIssuanceWorkflow.CanWithdraw(request.Status) || request.WorkflowInstanceId is not Guid instanceId)
            return new WorkflowApprovalWithdrawResult(false, NotPendingMessage);

        using var tenant = WorkflowApprovalSubmission.UseTenant(request.TenantId);
        return await WorkflowApprovalSubmission.WithdrawAsync(
            client, EntityType, request.Id, instanceId, actor, reason, ApplyOutcomeAsync, ct);
    }

    /// <summary>
    /// The one place an outcome changes a request, for the consumer and the sweep alike. The guard keeps a closed or unlinked request as it
    /// is; an approval that names the requester or the submitter is refused and logged.
    /// </summary>
    public async Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct = default)
    {
        var request = await db.CardRequests.FirstOrDefaultAsync(r => r.Id == outcome.EntityId, ct);
        var pending = request is { Status: CardRequestStatus.PendingApproval, IsCancelled: false };
        if (!WorkflowOutcomeGuard.ShouldApply(request?.WorkflowInstanceId, pending, outcome, logger))
            return;

        switch (outcome.Outcome)
        {
            case WorkflowApprovalOutcomes.Approved:
                if (WorkflowOutcomeGuard.HasExcludedDecider(outcome, [request!.CreatedBy, request.RequestedBy, request.SubmittedBy], logger))
                {
                    logger.LogError("Card request {RequestId}: an approval by its requester or submitter was refused.", request.Id);
                    return;
                }

                request.Status = CardRequestStatus.Approved;
                break;
            case WorkflowApprovalOutcomes.Rejected:
                request!.Status = CardRequestStatus.Rejected;
                request.RejectionReason = outcome.Reason;
                break;
            default:
                // Withdrawn: back to a draft that can be edited and submitted again under the next number.
                request!.Status = CardRequestStatus.Pending;
                break;
        }

        request.DecidedBy = outcome.DecidedByUserId;
        request.DecidedAt = outcome.DecidedAt;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Card request {RequestId} is {Status} after instance {InstanceId}.", request.Id, request.Status, outcome.InstanceId);
    }

    private static WorkflowApprovalStartRequest StartRequest(CardRequest request, Guid submitter, int number)
    {
        var excluded = new[] { request.CreatedBy, request.RequestedBy, submitter }
            .Where(id => id is not null && id != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        return new WorkflowApprovalStartRequest(
            EntityType,
            request.Id,
            WorkflowCode,
            $"{EntityType}:{request.Id:D}:{number}",
            excluded,
            // IDCard keeps no copy of the org structure: the card office acts centrally.
            null,
            $"بطاقة تعريف — {PurposeLabel(request.Purpose)}",
            $"/ui/idcard/CardRequests/Details/{request.Id:D}",
            new Dictionary<string, string>
            {
                ["cardPurpose"] = PurposeLabel(request.Purpose),
                ["employeeId"] = request.EmployeeId.ToString("D")
            },
            request.EmployeeId);
    }

    private static string PurposeLabel(CardRequestPurpose purpose) => purpose switch
    {
        CardRequestPurpose.New => "جديدة",
        CardRequestPurpose.Replacement => "بديلة",
        _ => "تحديث"
    };
}
