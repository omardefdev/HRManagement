using HRManagement.ServiceDefaults.Messaging;

namespace Payroll.Application.Abstractions;

/// <summary>
/// PLAN-53: stages the three salary change events in the bus outbox. No method saves: the caller's single <c>SaveChanges</c> writes the
/// request, the pay profile and the outbox rows together (PLAN-40, the SingleSave pattern).
/// </summary>
public interface IPayProfileChangeEventPublisher
{
    [StagesOutboxMessages]
    Task PublishSubmittedAsync(PayProfileChangeSubmittedEvent message, CancellationToken ct);

    [StagesOutboxMessages]
    Task PublishDecidedAsync(PayProfileChangeDecidedEvent message, CancellationToken ct);

    [StagesOutboxMessages]
    Task PublishAppliedAsync(PayProfileChangeAppliedEvent message, CancellationToken ct);
}

public sealed record PayProfileChangeSubmittedEvent(
    Guid RequestId, Guid EmployeeId, string ChangeKind, string Source, DateOnly EffectiveFrom, Guid RequestedBy, DateTime OccurredAt);

public sealed record PayProfileChangeDecidedEvent(
    Guid RequestId, Guid EmployeeId, string Decision, string ResultingState, Guid DecidedBy, DateTime OccurredAt);

public sealed record PayProfileChangeAppliedEvent(
    Guid RequestId,
    Guid EmployeeId,
    Guid NewProfileId,
    Guid? PreviousProfileId,
    DateOnly EffectiveFrom,
    string Source,
    string? SourceReference,
    string? PayGradeCode,
    decimal BaseSalary,
    decimal? PreviousBaseSalary,
    DateTime OccurredAt);
