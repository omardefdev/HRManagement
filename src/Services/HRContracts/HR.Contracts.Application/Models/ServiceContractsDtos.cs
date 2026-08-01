using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Enums;

namespace HR.Contracts.Application.Models;

public record ContractLookupItem(Guid Id, string Label);

public record LeaveRequestDto(
    Guid? Id,
    Guid EmployeeId,
    LeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    int DurationDays,
    ApprovalStatus Status,
    Guid? ApprovedBy,
    string? Notes);

public record AdvanceRequestDto(
    Guid? Id,
    Guid EmployeeId,
    decimal Amount,
    string? Reason,
    ApprovalStatus Status,
    Guid? ApprovedBy);

public record AdministrativeDecisionDto(
    Guid? Id,
    string Subject,
    string DecisionNumber,
    string? IssuedBy,
    DateOnly? IssuedDate,
    Guid? RelatedEmployeeId,
    Guid? FileId = null);

public record CommencementRecordDto(
    Guid? Id,
    Guid EmployeeId,
    Guid? ContractId,
    DateOnly StartDate,
    Guid? NotifiedBy,
    DateTime? NotificationDate);

public record ProbationRecordDto(
    Guid? Id,
    Guid ContractId,
    Guid EmployeeId,
    DateOnly StartDate,
    DateOnly EndDate,
    ProbationResult Result,
    Guid? DecisionBy,
    DateOnly? DecisionDate,
    string? Notes);

public record SeparationRecordDto(
    Guid? Id,
    Guid EmployeeId,
    Guid? ContractId,
    SeparationType SeparationType,
    DateOnly EffectiveDate,
    string? Reason,
    Guid? ApprovedBy,
    string? Documents,
    bool SeparationEventPublished);
