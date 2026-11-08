using HRManagement.MessageContracts.Events;
using IDCard.Infrastructure.Services;
using MassTransit;

namespace IDCard.Infrastructure.Consumers;

/// <summary>
/// PLAN-46 (section 3): HR.Core's outcome for a card request. Every owner receives every outcome and keeps its own type; the guard in
/// <see cref="CardRequestApprovalService.ApplyOutcomeAsync"/> applies it only to the linked pending request, so a redelivery changes nothing.
/// </summary>
public sealed class CardRequestApprovalOutcomeConsumer(CardRequestApprovalService approvals) : IConsumer<IWorkflowOutcomeDecided>
{
    public Task Consume(ConsumeContext<IWorkflowOutcomeDecided> context) =>
        string.Equals(context.Message.EntityType, CardRequestApprovalService.EntityType, StringComparison.Ordinal)
            ? approvals.ApplyOutcomeAsync(context.Message, context.CancellationToken)
            : Task.CompletedTask;
}
