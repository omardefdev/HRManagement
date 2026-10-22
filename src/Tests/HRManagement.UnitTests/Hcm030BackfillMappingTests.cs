using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (C2/C3): <see cref="UnifiedIntakeStatusMapping"/> implements the backfill table of PLAN-30 session 1, row by
/// row, for both channel enums. <c>Hcm030BackfillTests</c> (PostgreSQL) checks that
/// <c>scripts/sql/hcm030-backfill-unified-intake.sql</c> produces the same state as this mapping for every row.
/// </summary>
public class Hcm030BackfillMappingTests
{
    private static readonly DateTime SourceUpdatedAt = new(2026, 8, 10, 8, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime EndedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<int, RequisitionStatus, RecruitmentApplicationStatus, RecruitmentRejectionKind?, string> Table() => new()
    {
        // source (0 Received/Submitted … 4 Rejected), requisition, status, kind, dates: R = RejectedAt, S = ScreenedAt, V = ReviewedAt, N = accepted note
        { 0, RequisitionStatus.Published, RecruitmentApplicationStatus.Submitted, null, "" },
        { 1, RequisitionStatus.Published, RecruitmentApplicationStatus.UnderReview, null, "S:src V:src" },
        { 2, RequisitionStatus.Published, RecruitmentApplicationStatus.Shortlisted, null, "V:src" },
        { 3, RequisitionStatus.Published, RecruitmentApplicationStatus.Shortlisted, null, "V:src N" },
        { 4, RequisitionStatus.Published, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.SelectionDecision, "R:src V:src" },
        { 0, RequisitionStatus.Cancelled, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionCancelled, "R:end V:end" },
        { 1, RequisitionStatus.Cancelled, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionCancelled, "R:end V:end" },
        { 2, RequisitionStatus.Cancelled, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionCancelled, "R:end V:end" },
        { 3, RequisitionStatus.Cancelled, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionCancelled, "R:end V:end" },
        { 4, RequisitionStatus.Cancelled, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.SelectionDecision, "R:src V:src" },
        { 0, RequisitionStatus.Closed, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionClosed, "R:end V:end" },
        { 2, RequisitionStatus.Closed, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.RequisitionClosed, "R:end V:end" },
        { 4, RequisitionStatus.Closed, RecruitmentApplicationStatus.Rejected, RecruitmentRejectionKind.SelectionDecision, "R:src V:src" }
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Csharp_mapping_matches_the_backfill_table(
        int source,
        RequisitionStatus requisitionStatus,
        RecruitmentApplicationStatus status,
        RecruitmentRejectionKind? kind,
        string dates)
    {
        var requisition = new RecruitmentRequisition
        {
            Status = requisitionStatus,
            CancelledAt = requisitionStatus == RequisitionStatus.Cancelled ? EndedAt : null,
            ClosedDate = requisitionStatus == RequisitionStatus.Closed ? EndedAt : null
        };
        var expected = dates.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        DateTime? Expect(string key) =>
            expected.Contains($"{key}:src") ? SourceUpdatedAt : expected.Contains($"{key}:end") ? EndedAt : null;

        foreach (var state in new[]
                 {
                     UnifiedIntakeStatusMapping.Map((CitizenApplicationStatus)source, SourceUpdatedAt, requisition),
                     UnifiedIntakeStatusMapping.Map((InternalApplicationStatus)source, SourceUpdatedAt, requisition)
                 })
        {
            Assert.Equal(status, state.Status);
            Assert.Equal(kind, state.RejectionKind);
            Assert.Equal(Expect("R"), state.RejectedAt);
            Assert.Equal(Expect("S"), state.ScreenedAt);
            Assert.Equal(Expect("V"), state.ReviewedAt);
            Assert.Equal(expected.Contains("N") ? UnifiedIntakeStatusMapping.AcceptedNote : null, state.Note);
        }
    }

    [Fact]
    public void Cancelled_requisition_without_a_cancel_date_falls_back_to_the_close_date_then_the_source()
    {
        var withCloseDate = new RecruitmentRequisition { Status = RequisitionStatus.Cancelled, ClosedDate = EndedAt };
        var withoutDates = new RecruitmentRequisition { Status = RequisitionStatus.Cancelled };

        Assert.Equal(EndedAt, UnifiedIntakeStatusMapping.Map(CitizenApplicationStatus.Received, SourceUpdatedAt, withCloseDate).RejectedAt);
        Assert.Equal(SourceUpdatedAt, UnifiedIntakeStatusMapping.Map(CitizenApplicationStatus.Received, SourceUpdatedAt, withoutDates).RejectedAt);
    }

    [Fact]
    public void Channel_enums_share_names_and_values()
    {
        Assert.Equal(
            Enum.GetValues<CitizenApplicationStatus>().Select(v => (int)v),
            Enum.GetValues<InternalApplicationStatus>().Select(v => (int)v));
        Assert.Equal(
            ["Received", "UnderReview", "Shortlisted", "Accepted", "Rejected"],
            Enum.GetNames<CitizenApplicationStatus>());
        Assert.Equal(
            ["Submitted", "UnderReview", "Shortlisted", "Accepted", "Rejected"],
            Enum.GetNames<InternalApplicationStatus>());
    }
}
