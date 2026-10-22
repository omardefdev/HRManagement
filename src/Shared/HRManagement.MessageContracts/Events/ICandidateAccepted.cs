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

    // ── v4 (HCM-029, additive): the approved offer terms, so the contract and the pay profile are built from them.
    // Defaults keep v3 consumers compiling and v3 messages deserializing (members read as null/false).

    /// <summary>HCM-029: <c>OfferLetter.OfferedSalary</c>.</summary>
    decimal? OfferedSalary => null;

    /// <summary>HCM-029: <c>OfferLetter.GradeCode</c> (the funded position's grade when the offer has none).</summary>
    string? GradeCode => null;

    /// <summary>HCM-029 / SYR-003: HRCore service regime id of the employment record (law50/law53/special_*).</summary>
    string? LegalRegimeId => null;

    /// <summary>HCM-029 / SYR-007: payroll regime code mapped from <see cref="LegalRegimeId"/> (LAW_50_2004/LAW_53_2021).</summary>
    string? PayrollRegimeCode => null;

    /// <summary>HCM-029: <c>EmploymentType</c> enum name (Permanent/Administrative/Temporary); same value as <see cref="EmploymentCategory"/>.</summary>
    string? EmploymentType => null;

    /// <summary>HCM-029: <c>FundedPosition.PositionCode</c>.</summary>
    string? PositionCode => null;

    /// <summary>HCM-029: org unit of the funded position (the employee's primary org unit when there is no position).</summary>
    Guid? OrgUnitId => null;

    /// <summary>
    /// HCM-029: always false on published messages — internal mobility publishes <see cref="IInternalPlacementCompleted"/>
    /// only. Kept so consumers can ignore a mobility message defensively.
    /// </summary>
    bool IsInternalMobility => false;

    int SchemaVersion => 4;
}
