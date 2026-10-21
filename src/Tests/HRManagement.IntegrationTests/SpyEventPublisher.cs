using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;

namespace HRManagement.IntegrationTests;

public sealed class SpyEventPublisher : IEventPublisher
{
    public int ContractActivatedCount { get; private set; }
    public int SeparationCompletedCount { get; private set; }
    public int SeparationSagaStartedCount { get; private set; }
    public int SeparationSagaCancelledCount { get; private set; }
    public int LeaveApprovedCount { get; private set; }
    public int AdvanceApprovedCount { get; private set; }
    public int HireCaseCompletedCount { get; private set; }
    public int HireSagaStartedCount { get; private set; }
    public Guid? LastHireServiceTransactionId { get; private set; }
    public Guid? LastRecruitmentApplicationId { get; private set; }
    public Guid? LastCancelledSeparationRecordId { get; private set; }

    public Contract? LastActivatedContract { get; private set; }
    public Guid? LastLeaveRequestId { get; private set; }
    public Guid? LastSeparationEmployeeId { get; private set; }
    public decimal? LastAdvanceAmount { get; private set; }

    public Task PublishContractActivatedAsync(Contract contract, CancellationToken ct)
    {
        ContractActivatedCount++;
        LastActivatedContract = contract;
        return Task.CompletedTask;
    }

    public Task PublishCommencementRecordedAsync(Guid transactionId, Guid employeeId, DateOnly startDate, string? correlationId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task PublishConfirmationApprovedAsync(Guid transactionId, Guid employeeId, Guid contractId, string result, string? correlationId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task PublishLeaveApprovedAsync(
        Guid transactionId,
        Guid leaveRequestId,
        Guid employeeId,
        Guid approvedBy,
        string? correlationId,
        CancellationToken ct,
        string? leaveType = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int durationDays = 0)
    {
        LeaveApprovedCount++;
        LastLeaveRequestId = leaveRequestId;
        return Task.CompletedTask;
    }

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
        Task.CompletedTask;

    public Task PublishAdvanceApprovedAsync(
        Guid transactionId,
        Guid advanceRequestId,
        Guid employeeId,
        decimal amount,
        Guid approvedBy,
        string? correlationId,
        CancellationToken ct,
        decimal monthlyInstallment = 0,
        int numberOfInstallments = 0)
    {
        AdvanceApprovedCount++;
        LastAdvanceAmount = amount;
        return Task.CompletedTask;
    }

    public Task PublishJobUpdateAppliedAsync(Guid transactionId, Guid decisionId, Guid employeeId, string decisionNumber, string subject, string? correlationId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task PublishTransactionArchivedAsync(Guid transactionId, Guid employeeId, string archiveRef, string? correlationId, CancellationToken ct) =>
        Task.CompletedTask;

    public Task PublishHireCaseCompletedAsync(
        Guid employeeId,
        Guid serviceTransactionId,
        Guid? contractId,
        CancellationToken ct,
        Guid? recruitmentApplicationId = null)
    {
        HireCaseCompletedCount++;
        LastHireServiceTransactionId = serviceTransactionId;
        return Task.CompletedTask;
    }

    public Task StartHireSagaAsync(
        Guid serviceTransactionId,
        Guid employeeId,
        Guid? contractId,
        CancellationToken ct,
        Guid? recruitmentApplicationId = null)
    {
        HireSagaStartedCount++;
        LastHireServiceTransactionId = serviceTransactionId;
        LastRecruitmentApplicationId = recruitmentApplicationId;
        return Task.CompletedTask;
    }

    public Task PublishSeparationCompletedAsync(
        Guid employeeId,
        string separationType,
        DateOnly effectiveDate,
        CancellationToken ct,
        Guid? contractId = null,
        Guid? separationRecordId = null)
    {
        SeparationCompletedCount++;
        LastSeparationEmployeeId = employeeId;
        return Task.CompletedTask;
    }

    public Task StartSeparationSagaAsync(
        Guid separationRecordId,
        Guid employeeId,
        string separationType,
        DateOnly effectiveDate,
        CancellationToken ct,
        Guid? contractId = null,
        int remainingLeaveDays = 0,
        string? sourceLegalRegimeId = null,
        string? payrollRegimeCode = null)
    {
        SeparationSagaStartedCount++;
        SeparationCompletedCount++; // legacy assertion alias: orchestration started
        LastSeparationEmployeeId = employeeId;
        return Task.CompletedTask;
    }

    public Task CancelSeparationSagaAsync(Guid separationRecordId, string reason, CancellationToken ct)
    {
        SeparationSagaCancelledCount++;
        LastCancelledSeparationRecordId = separationRecordId;
        return Task.CompletedTask;
    }

    public Task PublishSeparationStepCompletedAsync(
        Guid separationRecordId,
        Guid employeeId,
        string stepName,
        string? notes,
        CancellationToken ct) => Task.CompletedTask;
}
