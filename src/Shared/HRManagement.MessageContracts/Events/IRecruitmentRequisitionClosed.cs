namespace HRManagement.MessageContracts.Events;

/// <summary>
/// HCM-032 (PLAN-32, decision 9): a recruitment requisition was closed (filled or closed by HR) or cancelled. Published by
/// HR.Core through its EF outbox on every close path; consumed by Dev.Analytics (<c>RecruitmentRequisitionFact</c>).
/// Aggregates only — no person-level data (no name, national id, application or employee id).
/// </summary>
public interface IRecruitmentRequisitionClosed
{
    Guid TenantId { get; }
    Guid RequisitionId { get; }
    Guid OrgUnitId { get; }

    /// <summary><c>Closed</c> or <c>Cancelled</c> (the requisition's final status).</summary>
    string Outcome { get; }

    DateTime ClosedAt { get; }

    /// <summary>Applications linked to the requisition (allocated competition applications included).</summary>
    int Applicants { get; }

    int HiredCount { get; }

    /// <summary>Calendar days from approval (fallback request) to the first hire; null when no one was hired.</summary>
    int? TimeToFillDays { get; }

    int ApplicantsPublic { get; }
    int ApplicantsInternal { get; }
    int ApplicantsHrEntered { get; }
    int ApplicantsLegacy { get; }

    int OffersAccepted { get; }
    int OffersDeclined { get; }
    int OffersExpired { get; }
    int OffersRevoked { get; }

    string? CorrelationId { get; }
    string SourceSystem { get; }

    int SchemaVersion => 1;
}
