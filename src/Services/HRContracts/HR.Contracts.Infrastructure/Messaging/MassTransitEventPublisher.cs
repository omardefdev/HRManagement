using HR.Contracts.Application.Abstractions;

using HR.Contracts.Domain.Entities;

using HRManagement.MessageContracts.Events;

using HRManagement.ServiceDefaults;

using MassTransit;



namespace HR.Contracts.Infrastructure.Messaging;



public class MassTransitEventPublisher(IPublishEndpoint publishEndpoint, IExecutionContextAccessor contextAccessor) : IEventPublisher

{

    private string? CurrentCorrelationId => contextAccessor.CorrelationId ?? CorrelationIdFlow.Get();

    private string CurrentSourceSystem => contextAccessor.SourceSystem;



    public Task PublishContractActivatedAsync(Contract contract, CancellationToken ct) =>

        publishEndpoint.Publish<IContractActivated>(new

        {

            ContractId = contract.Id,

            EmployeeId = contract.EmployeeId,

            ContractType = contract.ContractType.Code,

            StartDate = contract.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),

            EndDate = contract.EndDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),

            BaseSalary = (decimal?)null,

            PayGradeCode = (string?)null,

            CorrelationId = CurrentCorrelationId ?? Guid.NewGuid().ToString(),

            SourceSystem = CurrentSourceSystem

        }, ct);



    public Task PublishCommencementRecordedAsync(Guid transactionId, Guid employeeId, DateOnly startDate, string? correlationId, CancellationToken ct) =>

        publishEndpoint.Publish<ICommencementRecorded>(new

        {

            TransactionId = transactionId,

            EmployeeId = employeeId,

            StartDate = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),

            OccurredAt = DateTime.UtcNow,

            CorrelationId = correlationId ?? CurrentCorrelationId,

            SourceSystem = CurrentSourceSystem

        }, ct);



    public Task PublishConfirmationApprovedAsync(Guid transactionId, Guid employeeId, Guid contractId, string result, string? correlationId, CancellationToken ct) =>

        publishEndpoint.Publish<IConfirmationApproved>(new

        {

            TransactionId = transactionId,

            EmployeeId = employeeId,

            ContractId = contractId,

            Result = result,

            OccurredAt = DateTime.UtcNow,

            CorrelationId = correlationId ?? CurrentCorrelationId,

            SourceSystem = CurrentSourceSystem

        }, ct);



    public Task PublishLeaveApprovedAsync(Guid transactionId, Guid leaveRequestId, Guid employeeId, Guid approvedBy, string? correlationId, CancellationToken ct, string? leaveType = null, DateOnly? startDate = null, DateOnly? endDate = null, int durationDays = 0) =>

        publishEndpoint.Publish<ILeaveApproved>(new

        {

            TransactionId = transactionId,

            LeaveRequestId = leaveRequestId,

            EmployeeId = employeeId,

            ApprovedBy = approvedBy,

            OccurredAt = DateTime.UtcNow,

            CorrelationId = correlationId ?? CurrentCorrelationId,

            SourceSystem = CurrentSourceSystem,

            LeaveType = leaveType ?? "Unknown",

            StartDate = startDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? DateTime.UtcNow,

            EndDate = endDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? DateTime.UtcNow,

            DurationDays = durationDays

        }, ct);



    public Task PublishLeaveCancelledAsync(
        Guid transactionId,
        Guid leaveRequestId,
        Guid employeeId,
        Guid cancelledBy,
        string? correlationId,
        CancellationToken ct,
        string? leaveType = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int durationDays = 0) =>
        publishEndpoint.Publish<ILeaveCancelled>(new
        {
            TransactionId = transactionId,
            LeaveRequestId = leaveRequestId,
            EmployeeId = employeeId,
            CancelledBy = cancelledBy,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = correlationId ?? CurrentCorrelationId,
            SourceSystem = CurrentSourceSystem,
            LeaveType = leaveType ?? "Unknown",
            StartDate = startDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? DateTime.UtcNow,
            EndDate = endDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? DateTime.UtcNow,
            DurationDays = durationDays
        }, ct);



    public Task PublishAdvanceApprovedAsync(Guid transactionId, Guid advanceRequestId, Guid employeeId, decimal amount, Guid approvedBy, string? correlationId, CancellationToken ct, decimal monthlyInstallment = 0, int numberOfInstallments = 0) =>

        publishEndpoint.Publish<IAdvanceApproved>(new

        {

            TransactionId = transactionId,

            AdvanceRequestId = advanceRequestId,

            EmployeeId = employeeId,

            Amount = amount,

            ApprovedBy = approvedBy,

            OccurredAt = DateTime.UtcNow,

            CorrelationId = correlationId ?? CurrentCorrelationId,

            SourceSystem = CurrentSourceSystem,

            MonthlyInstallment = monthlyInstallment,

            NumberOfInstallments = numberOfInstallments

        }, ct);



    public Task PublishJobUpdateAppliedAsync(Guid transactionId, Guid decisionId, Guid employeeId, string decisionNumber, string subject, string? correlationId, CancellationToken ct) =>

        publishEndpoint.Publish<IJobUpdateApplied>(new

        {

            TransactionId = transactionId,

            DecisionId = decisionId,

            EmployeeId = employeeId,

            DecisionNumber = decisionNumber,

            Subject = subject,

            OccurredAt = DateTime.UtcNow,

            CorrelationId = correlationId ?? CurrentCorrelationId,

            SourceSystem = CurrentSourceSystem

        }, ct);



    public Task PublishTransactionArchivedAsync(Guid transactionId, Guid employeeId, string archiveRef, string? correlationId, CancellationToken ct) =>

        publishEndpoint.Publish<ITransactionArchived>(new

        {

            TransactionId = transactionId,

            EmployeeId = employeeId,

            ArchiveRef = archiveRef,

            ArchivedAt = DateTime.UtcNow,

            CorrelationId = correlationId ?? CurrentCorrelationId,

            SourceSystem = CurrentSourceSystem

        }, ct);



    public Task PublishSeparationCompletedAsync(Guid employeeId, string separationType, DateOnly effectiveDate, CancellationToken ct, Guid? contractId = null, Guid? separationRecordId = null) =>

        publishEndpoint.Publish<ISeparationCompleted>(new

        {

            EmployeeId = employeeId,

            SeparationType = separationType,

            EffectiveDate = effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),

            OccurredAt = DateTime.UtcNow,

            CorrelationId = CurrentCorrelationId,

            SourceSystem = CurrentSourceSystem,

            ContractId = contractId,

            SeparationRecordId = separationRecordId

        }, ct);

    public Task PublishSeparationStepCompletedAsync(
        Guid separationRecordId,
        Guid employeeId,
        string stepName,
        string? notes,
        CancellationToken ct) =>
        publishEndpoint.Publish<ISeparationStepCompleted>(new
        {
            SeparationRecordId = separationRecordId,
            EmployeeId = employeeId,
            StepName = stepName,
            CompletedAt = DateTime.UtcNow,
            Notes = notes,
            SourceSystem = CurrentSourceSystem,
            CorrelationId = CurrentCorrelationId
        }, ct);

    public Task PublishHireCaseCompletedAsync(
        Guid employeeId,
        Guid serviceTransactionId,
        Guid? contractId,
        CancellationToken ct,
        Guid? recruitmentApplicationId = null) =>
        publishEndpoint.Publish<IHireCaseCompleted>(new
        {
            EmployeeId = employeeId,
            ServiceTransactionId = serviceTransactionId,
            CompletedAt = DateTime.UtcNow,
            CorrelationId = CurrentCorrelationId,
            SourceSystem = CurrentSourceSystem,
            ContractId = contractId,
            RecruitmentApplicationId = recruitmentApplicationId
        }, ct);

    public Task StartSeparationSagaAsync(
        Guid separationRecordId,
        Guid employeeId,
        string separationType,
        DateOnly effectiveDate,
        CancellationToken ct,
        Guid? contractId = null,
        int remainingLeaveDays = 0) =>
        publishEndpoint.Publish<HRManagement.MessageContracts.Commands.IStartSeparationSaga>(new
        {
            SeparationRecordId = separationRecordId,
            EmployeeId = employeeId,
            SeparationType = separationType,
            EffectiveDate = effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            ContractId = contractId,
            CorrelationId = CurrentCorrelationId,
            TenantId = TenantIdFlow.Get(),
            RemainingLeaveDays = remainingLeaveDays
        }, ct);

    public Task CancelSeparationSagaAsync(
        Guid separationRecordId,
        string reason,
        CancellationToken ct) =>
        publishEndpoint.Publish<HRManagement.MessageContracts.Commands.ICancelSeparationSaga>(new
        {
            SeparationRecordId = separationRecordId,
            Reason = reason,
            CorrelationId = CurrentCorrelationId,
            TenantId = TenantIdFlow.Get()
        }, ct);

    public Task StartHireSagaAsync(
        Guid serviceTransactionId,
        Guid employeeId,
        Guid? contractId,
        CancellationToken ct,
        Guid? recruitmentApplicationId = null) =>
        publishEndpoint.Publish<HRManagement.MessageContracts.Commands.IStartHireSaga>(new
        {
            ServiceTransactionId = serviceTransactionId,
            EmployeeId = employeeId,
            ContractId = contractId,
            RecruitmentApplicationId = recruitmentApplicationId,
            CorrelationId = CurrentCorrelationId,
            TenantId = TenantIdFlow.Get()
        }, ct);
}

