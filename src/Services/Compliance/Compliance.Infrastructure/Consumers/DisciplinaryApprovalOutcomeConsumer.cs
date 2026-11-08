using Compliance.Domain.Entities;
using Compliance.Infrastructure.Services;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;

namespace Compliance.Infrastructure.Consumers;

/// <summary>
/// PLAN-46 (section 4): HR.Core's outcome for a disciplinary action. The approval is the only thing that sends an action to payroll: this
/// consumer (and the sweep, through the same <see cref="DisciplinaryApprovalService.ApplyOutcomeAsync"/>) stages
/// <see cref="IDisciplinaryActionTaken"/> with the status change. Compliance runs the consumer outbox, so the status and the message commit
/// together, and a redelivery finds the action approved and publishes nothing.
/// </summary>
public sealed class DisciplinaryApprovalOutcomeConsumer(DisciplinaryApprovalService approvals) : IConsumer<IWorkflowOutcomeDecided>
{
    public Task Consume(ConsumeContext<IWorkflowOutcomeDecided> context) =>
        string.Equals(context.Message.EntityType, DisciplinaryApprovalService.EntityType, StringComparison.Ordinal)
            ? approvals.ApplyOutcomeAsync(context.Message, context.CancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// The payload payroll has always read (<c>PenaltyDays</c> stays 0 until wave 2). The message id comes from the action, so payroll's
    /// inbox drops any second copy whatever its path.
    /// </summary>
    [StagesOutboxMessages]
    public static Task StageActionTakenAsync(IPublishEndpoint publish, DisciplinaryAction action, CancellationToken ct) =>
        publish.Publish<IDisciplinaryActionTaken>(new
        {
            action.EmployeeId,
            ActionType = action.ActionType.ToString(),
            action.IssuedAt,
            action.Notes,
            action.ComplaintId,
            CorrelationId = (Guid?)action.Id,
            ActionId = action.Id,
            TenantId = (Guid?)action.TenantId,
            SourceSystem = "Compliance",
            PenaltyDays = 0
        }, p => p.MessageId = DeterministicMessageId.For<IDisciplinaryActionTaken>(action.Id), ct);
}
