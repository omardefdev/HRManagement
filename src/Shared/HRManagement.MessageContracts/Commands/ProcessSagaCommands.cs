namespace HRManagement.MessageContracts.Commands;

/// <summary>INT-003: party identifier for orchestrated journeys.</summary>
public static class ProcessParties
{
    // Separation
    public const string Payroll = "Payroll";
    public const string HrCore = "HR.Core";
    public const string IdCard = "IDCard";
    public const string AuthDirectory = "Auth.Directory";
    public const string AdminLogistics = "Admin.Logistics";
    public const string ItInfra = "IT.Infra";
    public const string Archive = "Records.Archive";
    public const string Contracts = "HR.Contracts";
    public const string Health = "HR.Health";
    public const string Movements = "HR.Movements";

    // Hire
    public const string Diwan = "Diwan";
}

public interface IExecuteSeparationParty
{
    Guid SeparationRecordId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string SeparationType { get; }
    DateTime EffectiveDate { get; }
    Guid? ContractId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    bool Compensate { get; }
    int RemainingLeaveDays { get; }
    string? SourceLegalRegimeId => null;
    string? PayrollRegimeCode => null;
    int SchemaVersion => 3;
}

public interface ISeparationPartyCompleted
{
    Guid SeparationRecordId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string? EvidenceRef { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ISeparationPartyFaulted
{
    Guid SeparationRecordId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IStartSeparationSaga
{
    Guid SeparationRecordId { get; }
    Guid EmployeeId { get; }
    string SeparationType { get; }
    DateTime EffectiveDate { get; }
    Guid? ContractId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int RemainingLeaveDays { get; }
    string? SourceLegalRegimeId => null;
    string? PayrollRegimeCode => null;
    int SchemaVersion => 3;
}

public interface IResumeSeparationSaga
{
    Guid SeparationRecordId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ICancelSeparationSaga
{
    Guid SeparationRecordId { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ISeparationPartyTimeout
{
    Guid SeparationRecordId { get; }
    string Party { get; }
    int SchemaVersion => 1;
}

public interface IExecuteHireParty
{
    Guid ServiceTransactionId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    Guid? ContractId { get; }
    Guid? RecruitmentApplicationId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    bool Compensate { get; }
    int SchemaVersion => 1;
}

public interface IHirePartyCompleted
{
    Guid ServiceTransactionId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string? EvidenceRef { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IHirePartyFaulted
{
    Guid ServiceTransactionId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IStartHireSaga
{
    Guid ServiceTransactionId { get; }
    Guid EmployeeId { get; }
    Guid? ContractId { get; }
    Guid? RecruitmentApplicationId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IResumeHireSaga
{
    Guid ServiceTransactionId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ICancelHireSaga
{
    Guid ServiceTransactionId { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IHirePartyTimeout
{
    Guid ServiceTransactionId { get; }
    string Party { get; }
    int SchemaVersion => 1;
}

public interface IExecuteTransferParty
{
    Guid TransferRequestId { get; }
    Guid EmployeeId { get; }
    Guid FromOrgUnitId { get; }
    Guid ToOrgUnitId { get; }
    string Party { get; }
    DateTime ExecutedAt { get; }
    decimal? SalaryChange { get; }
    decimal? AllowanceChange { get; }
    string? PayrollRegimeCode => null;
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    bool Compensate { get; }
    int SchemaVersion => 2;
}

public interface ITransferPartyCompleted
{
    Guid TransferRequestId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string? EvidenceRef { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ITransferPartyFaulted
{
    Guid TransferRequestId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IStartTransferSaga
{
    Guid TransferRequestId { get; }
    Guid EmployeeId { get; }
    Guid FromOrgUnitId { get; }
    Guid ToOrgUnitId { get; }
    DateTime ExecutedAt { get; }
    decimal? SalaryChange { get; }
    decimal? AllowanceChange { get; }
    string? SourceLegalRegimeId => null;
    string? TargetLegalRegimeId => null;
    string? TransitionDecisionRef => null;
    string? ConditionCode => null;
    string? ConflictNotes => null;
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 2;
}

public interface IResumeTransferSaga
{
    Guid TransferRequestId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ICancelTransferSaga
{
    Guid TransferRequestId { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ITransferPartyTimeout
{
    Guid TransferRequestId { get; }
    string Party { get; }
    int SchemaVersion => 1;
}

public interface IExecuteSecondmentParty
{
    Guid SecondmentRequestId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    DateTime ActivatedAt { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    bool Compensate { get; }
    int SchemaVersion => 1;
}

public interface ISecondmentPartyCompleted
{
    Guid SecondmentRequestId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string? EvidenceRef { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ISecondmentPartyFaulted
{
    Guid SecondmentRequestId { get; }
    Guid EmployeeId { get; }
    string Party { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IStartSecondmentSaga
{
    Guid SecondmentRequestId { get; }
    Guid EmployeeId { get; }
    DateTime ActivatedAt { get; }
    string HostOrg { get; }
    DateTime EndDate { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface IResumeSecondmentSaga
{
    Guid SecondmentRequestId { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ICancelSecondmentSaga
{
    Guid SecondmentRequestId { get; }
    string Reason { get; }
    string? CorrelationId { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;
}

public interface ISecondmentPartyTimeout
{
    Guid SecondmentRequestId { get; }
    string Party { get; }
    int SchemaVersion => 1;
}

/// <summary>INT-003: open a human intervention row in the owning service.</summary>
public interface IProcessInterventionRequested
{
    string SagaType { get; }
    Guid CorrelationId { get; }
    string Reason { get; }
    string? FailedParty { get; }
    Guid? TenantId { get; }
}
