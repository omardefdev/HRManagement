using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-030 (C2): the recruitment-application state a pre-unification channel row is linked with.</summary>
public sealed record UnifiedIntakeLinkState(
    RecruitmentApplicationStatus Status,
    RecruitmentRejectionKind? RejectionKind,
    DateTime? RejectedAt,
    DateTime? ScreenedAt,
    DateTime? ReviewedAt,
    string? Note);

/// <summary>
/// HCM-030 (C2): maps a citizen/internal channel status (same names and values in both enums) to the recruitment
/// application created for it. <c>scripts/sql/hcm030-backfill-unified-intake.sql</c> implements the same table and a
/// unit test keeps them in step. Dates come from the source row or the requisition — never "now" — so linking never
/// reopens an appeal window.
/// </summary>
public static class UnifiedIntakeStatusMapping
{
    public const string AcceptedNote = "كان مقبولاً في قناة التقديم قبل توحيد المسار.";

    public static UnifiedIntakeLinkState Map(
        CitizenApplicationStatus source,
        DateTime sourceUpdatedAt,
        RecruitmentRequisition requisition) =>
        Map((int)source, sourceUpdatedAt, requisition);

    public static UnifiedIntakeLinkState Map(
        InternalApplicationStatus source,
        DateTime sourceUpdatedAt,
        RecruitmentRequisition requisition) =>
        Map((int)source, sourceUpdatedAt, requisition);

    /// <summary>Requisition cancelled or closed: open channel rows end as rejected with that kind.</summary>
    public static RecruitmentRejectionKind? EndedRequisitionKind(RecruitmentRequisition requisition) =>
        requisition.Status switch
        {
            RequisitionStatus.Cancelled => RecruitmentRejectionKind.RequisitionCancelled,
            RequisitionStatus.Closed => RecruitmentRejectionKind.RequisitionClosed,
            _ => null
        };

    private static UnifiedIntakeLinkState Map(int source, DateTime sourceUpdatedAt, RecruitmentRequisition requisition)
    {
        // 0 Received/Submitted, 1 UnderReview, 2 Shortlisted, 3 Accepted, 4 Rejected.
        const int rejected = (int)CitizenApplicationStatus.Rejected;
        if (source == rejected)
            return Rejected(RecruitmentRejectionKind.SelectionDecision, sourceUpdatedAt);

        if (EndedRequisitionKind(requisition) is RecruitmentRejectionKind endedKind)
        {
            var endedAt = requisition.Status == RequisitionStatus.Cancelled
                ? requisition.CancelledAt ?? requisition.ClosedDate
                : requisition.ClosedDate;
            return Rejected(endedKind, endedAt ?? sourceUpdatedAt);
        }

        return source switch
        {
            (int)CitizenApplicationStatus.UnderReview => new UnifiedIntakeLinkState(
                RecruitmentApplicationStatus.UnderReview, null, null, sourceUpdatedAt, sourceUpdatedAt, null),
            (int)CitizenApplicationStatus.Shortlisted => new UnifiedIntakeLinkState(
                RecruitmentApplicationStatus.Shortlisted, null, null, null, sourceUpdatedAt, null),
            (int)CitizenApplicationStatus.Accepted => new UnifiedIntakeLinkState(
                RecruitmentApplicationStatus.Shortlisted, null, null, null, sourceUpdatedAt, AcceptedNote),
            _ => new UnifiedIntakeLinkState(RecruitmentApplicationStatus.Submitted, null, null, null, null, null)
        };
    }

    private static UnifiedIntakeLinkState Rejected(RecruitmentRejectionKind kind, DateTime at) =>
        new(RecruitmentApplicationStatus.Rejected, kind, at, null, at, null);
}
