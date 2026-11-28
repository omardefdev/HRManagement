using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Application.Services;
using HRManagement.MessageContracts.Events;
using MassTransit;

namespace HR.Contracts.Infrastructure.Consumers;

/// <summary>
/// PLAN-60 (§2): HR.Core's outcome for a separation record. Every owner receives every outcome and keeps its own type; the guard in
/// <see cref="SeparationApprovalService.ApplyOutcomeAsync"/> applies it only to the linked pending record, so a redelivery, a foreign
/// instance or a late outcome changes nothing (logged and counted in <c>workflow_outcome_ignored_total</c>).
/// </summary>
public sealed class SeparationApprovalOutcomeConsumer(SeparationApprovalService approvals) : IConsumer<IWorkflowOutcomeDecided>
{
    public Task Consume(ConsumeContext<IWorkflowOutcomeDecided> context) =>
        string.Equals(context.Message.EntityType, SeparationApprovalRouting.EntityType, StringComparison.Ordinal)
            ? approvals.ApplyOutcomeAsync(context.Message, context.CancellationToken)
            : Task.CompletedTask;
}
