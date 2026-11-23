using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Payroll.Application.Abstractions;

namespace Payroll.Infrastructure.Integration;

/// <summary>
/// PLAN-53: stages the salary change events in the bus outbox of <c>PayrollDbContext</c>. Nothing is saved here: the caller's one
/// <c>SaveChanges</c> writes the <c>OutboxMessage</c> rows with the request and the pay profile (PLAN-40, SingleSave).
/// </summary>
public sealed class PayProfileChangeEventPublisher(IPublishEndpoint publish) : IPayProfileChangeEventPublisher
{
    [StagesOutboxMessages]
    public Task PublishSubmittedAsync(PayProfileChangeSubmittedEvent message, CancellationToken ct) =>
        publish.Publish<IPayProfileChangeSubmitted>(new
        {
            message.RequestId,
            message.EmployeeId,
            message.ChangeKind,
            message.Source,
            message.EffectiveFrom,
            message.RequestedBy,
            message.OccurredAt
        }, ct);

    [StagesOutboxMessages]
    public Task PublishDecidedAsync(PayProfileChangeDecidedEvent message, CancellationToken ct) =>
        publish.Publish<IPayProfileChangeDecided>(new
        {
            message.RequestId,
            message.EmployeeId,
            message.Decision,
            message.ResultingState,
            message.DecidedBy,
            message.OccurredAt
        }, ct);

    [StagesOutboxMessages]
    public Task PublishAppliedAsync(PayProfileChangeAppliedEvent message, CancellationToken ct) =>
        publish.Publish<IPayProfileChangeApplied>(new
        {
            message.RequestId,
            message.EmployeeId,
            message.NewProfileId,
            message.PreviousProfileId,
            message.EffectiveFrom,
            message.Source,
            message.SourceReference,
            message.PayGradeCode,
            message.BaseSalary,
            message.PreviousBaseSalary,
            message.OccurredAt
        }, ct);
}
