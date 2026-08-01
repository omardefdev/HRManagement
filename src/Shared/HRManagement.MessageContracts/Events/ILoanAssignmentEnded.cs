namespace HRManagement.MessageContracts.Events;

/// <summary>FUN-003: loan assignment closed — restore directory/payroll markers.</summary>
public interface ILoanAssignmentEnded
{
    Guid LoanAssignmentRequestId { get; }
    Guid EmployeeId { get; }
    Guid LendingOrgUnitId { get; }
    Guid BorrowingOrgUnitId { get; }
    DateTime EndedAt { get; }
    Guid CorrelationId { get; }
    string SourceSystem { get; }
    Guid? TenantId => null;
    int SchemaVersion => 1;
}
