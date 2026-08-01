namespace HRManagement.MessageContracts.Events;

public interface ILoanAssignmentExecuted
{
    Guid LoanAssignmentRequestId { get; }
    Guid EmployeeId { get; }
    Guid LendingOrgUnitId { get; }
    Guid BorrowingOrgUnitId { get; }
    DateTime StartDate { get; }
    DateTime EndDate { get; }
    Guid CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
