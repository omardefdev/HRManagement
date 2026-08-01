namespace HRManagement.MessageContracts.Events;

public interface ICandidateAccepted
{
    Guid EmployeeId { get; }
    string EmploymentCategory { get; }
    DateTime ExpectedStartDate { get; }

    /// <remarks>
    /// The national identifier was previously carried here and read by no consumer — dead payload
    /// that still crossed a service boundary and landed in the broker and any message log.
    /// Subscribers resolve the employee by <see cref="EmployeeId"/>.
    /// </remarks>
    string? FullName { get; }

    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 2;
}
