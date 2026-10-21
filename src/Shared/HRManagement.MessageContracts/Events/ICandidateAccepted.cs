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

    /// <summary>HCM-005: recruitment application lineage for hire saga wiring.</summary>
    Guid? RecruitmentApplicationId => null;

    Guid? RequisitionId => null;

    Guid? FundedPositionId => null;

    Guid? OfferLetterId => null;

    int SchemaVersion => 3;
}
