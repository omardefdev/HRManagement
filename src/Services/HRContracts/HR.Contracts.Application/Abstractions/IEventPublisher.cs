using HR.Contracts.Domain.Entities;



namespace HR.Contracts.Application.Abstractions;



public interface IEventPublisher

{

    Task PublishContractActivatedAsync(Contract contract, CancellationToken ct);

    Task PublishCommencementRecordedAsync(Guid transactionId, Guid employeeId, DateOnly startDate, string? correlationId, CancellationToken ct);

    Task PublishConfirmationApprovedAsync(Guid transactionId, Guid employeeId, Guid contractId, string result, string? correlationId, CancellationToken ct);

    Task PublishLeaveApprovedAsync(Guid transactionId, Guid leaveRequestId, Guid employeeId, Guid approvedBy, string? correlationId, CancellationToken ct, string? leaveType = null, DateOnly? startDate = null, DateOnly? endDate = null, int durationDays = 0);

    Task PublishLeaveCancelledAsync(Guid transactionId, Guid leaveRequestId, Guid employeeId, Guid cancelledBy, string? correlationId, CancellationToken ct, string? leaveType = null, DateOnly? startDate = null, DateOnly? endDate = null, int durationDays = 0);

    Task PublishAdvanceApprovedAsync(Guid transactionId, Guid advanceRequestId, Guid employeeId, decimal amount, Guid approvedBy, string? correlationId, CancellationToken ct, decimal monthlyInstallment = 0, int numberOfInstallments = 0);

    Task PublishJobUpdateAppliedAsync(Guid transactionId, Guid decisionId, Guid employeeId, string decisionNumber, string subject, string? correlationId, CancellationToken ct);

    Task PublishTransactionArchivedAsync(Guid transactionId, Guid employeeId, string archiveRef, string? correlationId, CancellationToken ct);

    Task PublishSeparationCompletedAsync(Guid employeeId, string separationType, DateOnly effectiveDate, CancellationToken ct, Guid? contractId = null, Guid? separationRecordId = null);

    /// <summary>INT-003: start separation process manager (replaces immediate fan-out).</summary>
    Task StartSeparationSagaAsync(
        Guid separationRecordId,
        Guid employeeId,
        string separationType,
        DateOnly effectiveDate,
        CancellationToken ct,
        Guid? contractId = null,
        int remainingLeaveDays = 0,
        string? sourceLegalRegimeId = null,
        string? payrollRegimeCode = null);

    /// <summary>FUN-002: abort in-flight separation saga with party compensate.</summary>
    Task CancelSeparationSagaAsync(
        Guid separationRecordId,
        string reason,
        CancellationToken ct);

    Task StartHireSagaAsync(
        Guid serviceTransactionId,
        Guid employeeId,
        Guid? contractId,
        CancellationToken ct,
        Guid? recruitmentApplicationId = null);

    Task PublishSeparationStepCompletedAsync(
        Guid separationRecordId,
        Guid employeeId,
        string stepName,
        string? notes,
        CancellationToken ct);

    Task PublishHireCaseCompletedAsync(Guid employeeId, Guid serviceTransactionId, Guid? contractId, CancellationToken ct, Guid? recruitmentApplicationId = null);

    /// <summary>
    /// FUN-029: open an intervention (IProcessInterventionRequested → ProcessInterventions) outside the saga — unresolved
    /// hire contract type, overdue commencement. Default no-op keeps the test doubles compiling; the MassTransit
    /// publisher implements it.
    /// </summary>
    Task PublishProcessInterventionRequestedAsync(
        string sagaType,
        Guid correlationId,
        string reason,
        string? failedParty,
        Guid? tenantId,
        CancellationToken ct) => Task.CompletedTask;

}

