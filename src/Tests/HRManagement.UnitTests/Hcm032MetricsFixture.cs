using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Workflow;
using System.Text.Json;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-032 (PLAN-32): the hand-built recruitment data set behind <see cref="Hcm032RecruitmentMetricsTests"/> — two org
/// units, six requisitions, 27 applications in every channel (legacy rows without events, event histories with explicit
/// <c>OccurredAt</c>, the recorder suppressed), offers in every outcome, appeals, objections, workflow instances with an
/// overdue step and one competition. Every expected number is computed by hand in the test's comment tables.
/// "Now" is 2026-10-29 12:00 UTC.
/// </summary>
internal static class Hcm032MetricsFixture
{
    public static readonly DateTime Now = Utc(2026, 10, 29, 12);

    public static readonly RecruitmentMetricsFilter Period = new() { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 11, 1) };

    public static readonly Guid UnitHq = G("a1"), UnitBranch = G("a2");
    public static readonly Guid P1 = G("b1"), P2 = G("b2"), P3 = G("b3"), P4 = G("b4"), P5 = G("b5"), P6 = G("b6"), P7 = G("b7"), P8 = G("b8");
    public static readonly Guid R1 = G("c1"), R2 = G("c2"), R3 = G("c3"), R4 = G("c4"), R5 = G("c5"), R6 = G("c6");
    public static readonly Guid Competition = G("d1"), Line = G("d2"), ResultVersion = G("d3"), ExamSessionId = G("d4");
    public static readonly Guid WfRequisition = G("e1"), WfOffer = G("e2"), WfCompetition = G("e3");

    public const string CandidateNamePrefix = "مرشح-تقرير-";
    public const string NationalIdPrefix = "3203";

    private static int _n;

    public static DateTime Utc(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static Guid G(string suffix) => Guid.Parse($"32000000-0000-4000-8000-0000000000{suffix}");

    public static OrgAccessScope Unrestricted => OrgAccessScope.Unrestricted();
    public static OrgAccessScope Branch => OrgAccessScope.Restricted(new HashSet<Guid> { UnitBranch });

    public static RecruitmentMetricsService CreateService(HRCoreDbContext db, RecruitmentOptions? options = null) =>
        new(db, Microsoft.Extensions.Options.Options.Create(options ?? DefaultOptions()), new FixedTimeProvider(Now));

    public static RecruitmentOptions DefaultOptions()
    {
        var options = new RecruitmentOptions();
        options.Analytics.ApprovalSlaHours["RECRUITMENT-REQUISITION-APPROVAL"] = 72;
        return options;
    }

    public sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    public static async Task SeedAsync(HRCoreDbContext db)
    {
        await LegislationRegisterSeeder.SeedAsync(db);
        using var _ = db.SuppressApplicationEvents();

        db.OrgUnits.AddRange(
            new OrgUnit { Id = UnitHq, Code = "OU-H32-HQ", NameAr = "الإدارة المركزية", NameEn = "HQ", MaterializedPath = $"/{UnitHq:D}/" },
            new OrgUnit { Id = UnitBranch, Code = "OU-H32-BR", NameAr = "فرع حلب", NameEn = "Branch", MaterializedPath = $"/{UnitBranch:D}/" });

        Position(db, P1, UnitHq, FundedPositionStatus.Occupied, Utc(2025, 12, 1));
        Position(db, P2, UnitHq, FundedPositionStatus.Vacant, Utc(2025, 12, 1));
        Position(db, P3, UnitBranch, FundedPositionStatus.Occupied, Utc(2025, 12, 1));
        Position(db, P4, UnitBranch, FundedPositionStatus.Vacant, Utc(2026, 5, 1));
        Position(db, P5, UnitHq, FundedPositionStatus.Vacant, Utc(2026, 1, 1));
        Position(db, P6, UnitBranch, FundedPositionStatus.Vacant, Utc(2026, 1, 1));
        Position(db, P7, UnitHq, FundedPositionStatus.Vacant, Utc(2026, 1, 1));
        Position(db, P8, UnitBranch, FundedPositionStatus.Frozen, Utc(2026, 1, 1));

        var competition = new RecruitmentCompetition
        {
            Id = Competition, Number = "COMP-H32-1", Title = "مسابقة التقرير", LegalSourceId = Syr001LegislationIds.Law50_2004,
            Status = RecruitmentCompetitionStatus.Finalized, ApplicationClosingDate = new DateOnly(2026, 8, 1),
            AnnouncedAt = Utc(2026, 7, 15), TieBreakOrder = "ExamScore",
            ObjectionWindowDays = 7, ReserveValidityMonths = 6, MaxLinesPerCandidate = 1, PassMark = 50, ReconcileThreshold = 10
        };
        db.RecruitmentCompetitions.Add(competition);
        db.CompetitionLines.Add(new CompetitionLine { Id = Line, CompetitionId = Competition, LineNo = 1, Title = "سطر 1", GradeCode = "G5", RequiredCount = 1 });

        Requisition(db, R1, "REQ-H32-1", P1, RequisitionStatus.Closed, Utc(2026, 1, 5), Utc(2026, 1, 10), Utc(2026, 1, 12), closed: Utc(2026, 3, 1));
        Requisition(db, R2, "REQ-H32-2", P2, RequisitionStatus.Published, Utc(2026, 2, 1), approved: null, Utc(2026, 2, 3));
        Requisition(db, R3, "REQ-H32-3", P3, RequisitionStatus.Closed, Utc(2026, 3, 1), Utc(2026, 3, 4), Utc(2026, 3, 6), closed: Utc(2026, 10, 1));
        Requisition(db, R4, "REQ-H32-4", P6, RequisitionStatus.Published, Utc(2026, 7, 1), Utc(2026, 7, 5), Utc(2026, 7, 20), line: Line);
        Requisition(db, R5, "REQ-H32-5", P5, RequisitionStatus.PendingApproval, Utc(2026, 10, 20), approved: null, published: null);
        Requisition(db, R6, "REQ-H32-6", P7, RequisitionStatus.Cancelled, Utc(2026, 3, 20), Utc(2026, 3, 25), published: null, cancelled: Utc(2026, 4, 10));

        var e1 = Employee(db, Utc(2026, 2, 20));
        var e2 = Employee(db, Utc(2026, 10, 2));
        var e3 = Employee(db, Utc(2026, 10, 28));

        // ---- R1 (HQ): legacy rows, no events ------------------------------------------------------------------------
        var a1 = App(db, "A1", R1, RecruitmentIntakeChannel.HrEntered, Utc(2026, 1, 15), RecruitmentApplicationStatus.Hired,
            screened: Utc(2026, 1, 20), ranked: Utc(2026, 1, 28), hired: e1);
        Offer(db, a1, P1, "O1", OfferStatus.Accepted, created: Utc(2026, 2, 4), issued: Utc(2026, 2, 5), accepted: Utc(2026, 2, 8));
        App(db, "A2", R1, RecruitmentIntakeChannel.Legacy, Utc(2026, 1, 16), RecruitmentApplicationStatus.Rejected,
            screened: Utc(2026, 1, 20), ranked: Utc(2026, 1, 28), rejected: (Utc(2026, 2, 10), RecruitmentRejectionKind.NotSelectedOnHire));
        // Legacy rejection without a kind (pre-HCM-028): appealable as SelectionDecision.
        var a3 = App(db, "A3", R1, RecruitmentIntakeChannel.Public, Utc(2026, 1, 17), RecruitmentApplicationStatus.Rejected,
            screened: Utc(2026, 1, 20), rejected: (Utc(2026, 1, 25), RecruitmentRejectionKind.SelectionDecision));
        a3.RejectionKind = null;
        App(db, "A4", R1, RecruitmentIntakeChannel.Public, Utc(2026, 1, 17), RecruitmentApplicationStatus.Rejected,
            rejected: (Utc(2026, 3, 1), RecruitmentRejectionKind.RequisitionClosed));
        var a5 = App(db, "A5", R1, RecruitmentIntakeChannel.Internal, Utc(2026, 1, 18), RecruitmentApplicationStatus.OfferDeclined,
            screened: Utc(2026, 1, 20), ranked: Utc(2026, 1, 28));
        Offer(db, a5, P1, "O2", OfferStatus.Declined, created: Utc(2026, 1, 31), issued: Utc(2026, 2, 1), declined: Utc(2026, 2, 3),
            declinedBy: OfferDeclinedBy.HrOnBehalf, category: null);
        App(db, "A6", R1, RecruitmentIntakeChannel.Internal, Utc(2026, 1, 19), RecruitmentApplicationStatus.Withdrawn);

        // ---- R3 (branch): full event histories -----------------------------------------------------------------------
        var b1 = App(db, "B1", R3, RecruitmentIntakeChannel.Public, Utc(2026, 9, 1), RecruitmentApplicationStatus.Hired,
            screened: Utc(2026, 9, 3), ranked: Utc(2026, 9, 20), hired: e2);
        var o3 = Offer(db, b1, P3, "O3", OfferStatus.Accepted, created: Utc(2026, 9, 21), issued: Utc(2026, 9, 22), accepted: Utc(2026, 9, 25));
        Events(db, b1, (9, 1, S.Submitted), (9, 3, S.UnderReview), (9, 8, S.Shortlisted), (9, 10, S.InterviewScheduled),
            (9, 15, S.Assessed), (9, 20, S.Ranked), (9, 22, S.OfferExtended), (9, 25, S.OfferAccepted), (10, 1, S.Hired));
        Event(db, b1, RecruitmentApplicationEventTypes.InterviewScheduled, 0, Utc(2026, 9, 10));
        Event(db, b1, RecruitmentApplicationEventTypes.OfferIssued, (int)OfferStatus.Issued, Utc(2026, 9, 22), o3);
        Event(db, b1, RecruitmentApplicationEventTypes.OfferAccepted, (int)OfferStatus.Accepted, Utc(2026, 9, 25), o3);

        var b2 = App(db, "B2", R3, RecruitmentIntakeChannel.Public, Utc(2026, 9, 2), RecruitmentApplicationStatus.Rejected,
            rejected: (Utc(2026, 9, 12), RecruitmentRejectionKind.SelectionDecision));
        Events(db, b2, (9, 2, S.Submitted), (9, 4, S.UnderReview), (9, 9, S.Shortlisted), (9, 12, S.Rejected));
        Appeal(db, b2, RecruitmentAppealKind.Rejection, filed: Utc(2026, 9, 14), decided: Utc(2026, 9, 30), RecruitmentAppealOutcome.Dismissed);

        var b3 = App(db, "B3", R3, RecruitmentIntakeChannel.Internal, Utc(2026, 9, 3), RecruitmentApplicationStatus.OfferDeclined);
        var o4 = Offer(db, b3, P3, "O4", OfferStatus.Expired, created: Utc(2026, 9, 27), issued: Utc(2026, 9, 28), expired: Utc(2026, 10, 5), merit: true);
        Events(db, b3, (9, 3, S.Submitted), (9, 5, S.UnderReview), (9, 10, S.Shortlisted), (9, 11, S.InterviewScheduled),
            (9, 16, S.Assessed), (9, 21, S.Ranked), (9, 28, S.OfferExtended), (10, 5, S.OfferDeclined));
        Event(db, b3, RecruitmentApplicationEventTypes.OfferIssued, (int)OfferStatus.Issued, Utc(2026, 9, 28), o4);

        // Rejected 09-07, appeal upheld 09-20: as RecruitmentAppealWorkflowHandler does, the rejection is cleared and the
        // application goes back to UnderReview (only the event log keeps the rejection).
        var b4 = App(db, "B4", R3, RecruitmentIntakeChannel.HrEntered, Utc(2026, 9, 4), RecruitmentApplicationStatus.UnderReview);
        Events(db, b4, (9, 4, S.Submitted), (9, 6, S.UnderReview), (9, 7, S.Rejected), (9, 20, S.UnderReview));
        Appeal(db, b4, RecruitmentAppealKind.Rejection, filed: Utc(2026, 9, 8), decided: Utc(2026, 9, 20), RecruitmentAppealOutcome.Upheld);

        var b5 = App(db, "B5", R3, RecruitmentIntakeChannel.Public, Utc(2026, 10, 1), RecruitmentApplicationStatus.UnderReview);
        Events(db, b5, (10, 1, S.Submitted), (10, 2, S.UnderReview));
        var b6 = App(db, "B6", R3, RecruitmentIntakeChannel.Legacy, Utc(2026, 10, 3), RecruitmentApplicationStatus.Submitted);
        Events(db, b6, (10, 3, S.Submitted));
        var b7 = App(db, "B7", R3, RecruitmentIntakeChannel.Public, Utc(2026, 9, 5), RecruitmentApplicationStatus.ReserveListed, ranked: Utc(2026, 9, 20));
        Events(db, b7, (9, 5, S.Submitted), (9, 6, S.UnderReview), (9, 9, S.Shortlisted), (9, 20, S.Ranked), (10, 2, S.ReserveListed));
        var b8 = App(db, "B8", R3, RecruitmentIntakeChannel.Internal, Utc(2026, 9, 6), RecruitmentApplicationStatus.Withdrawn);
        Events(db, b8, (9, 6, S.Submitted), (9, 8, S.Withdrawn));
        var b9 = App(db, "B9", R3, RecruitmentIntakeChannel.Public, Utc(2026, 9, 7), RecruitmentApplicationStatus.Ranked, ranked: Utc(2026, 9, 18));
        var o5 = Offer(db, b9, P3, "O5", OfferStatus.Revoked, created: Utc(2026, 9, 19), revoked: Utc(2026, 9, 21), gradeDeviation: true);
        Events(db, b9, (9, 7, S.Submitted), (9, 8, S.UnderReview), (9, 10, S.Shortlisted), (9, 18, S.Ranked));
        var b10 = App(db, "B10", R3, RecruitmentIntakeChannel.Public, Utc(2026, 9, 10), RecruitmentApplicationStatus.OfferDeclined);
        var o7 = Offer(db, b10, P3, "O7", OfferStatus.Declined, created: Utc(2026, 9, 22), issued: Utc(2026, 9, 23), declined: Utc(2026, 9, 26),
            declinedBy: OfferDeclinedBy.Candidate, category: OfferDeclineReasonCategory.Salary);
        Events(db, b10, (9, 10, S.Submitted), (9, 11, S.UnderReview), (9, 12, S.Shortlisted), (9, 19, S.Ranked), (9, 23, S.OfferExtended), (9, 26, S.OfferDeclined));
        Event(db, b10, RecruitmentApplicationEventTypes.OfferIssued, (int)OfferStatus.Issued, Utc(2026, 9, 23), o7);

        // ---- R2 (HQ): mixed legacy / events --------------------------------------------------------------------------
        App(db, "C1", R2, RecruitmentIntakeChannel.Public, Utc(2026, 2, 10), RecruitmentApplicationStatus.Shortlisted, screened: Utc(2026, 2, 12));
        var c2 = App(db, "C2", R2, RecruitmentIntakeChannel.HrEntered, Utc(2026, 10, 24), RecruitmentApplicationStatus.UnderReview);
        Events(db, c2, (10, 24, S.Submitted), (10, 25, S.UnderReview));
        App(db, "C3", R2, RecruitmentIntakeChannel.Internal, Utc(2026, 2, 11), RecruitmentApplicationStatus.Submitted);
        App(db, "C4", R2, RecruitmentIntakeChannel.Legacy, Utc(2026, 2, 12), RecruitmentApplicationStatus.Rejected,
            screened: Utc(2026, 2, 20), rejected: (Utc(2026, 3, 2), RecruitmentRejectionKind.SelectionDecision));
        var c5 = App(db, "C5", R2, RecruitmentIntakeChannel.Public, Utc(2026, 10, 23), RecruitmentApplicationStatus.Hired, hired: e3);
        var o6 = Offer(db, c5, P2, "O6", OfferStatus.Accepted, created: Utc(2026, 10, 24), issued: Utc(2026, 10, 25), accepted: Utc(2026, 10, 26));
        Events(db, c5, (10, 23, S.Submitted), (10, 24, S.Ranked));
        Event(db, c5, RecruitmentApplicationEventTypes.OfferIssued, (int)OfferStatus.Issued, Utc(2026, 10, 25), o6);
        Event(db, c5, RecruitmentApplicationEventTypes.OfferAccepted, (int)OfferStatus.Accepted, Utc(2026, 10, 26), o6);

        // ---- competition (R4 on its line, branch) -------------------------------------------------------------------
        db.CompetitionResultVersions.Add(new CompetitionResultVersion
        {
            Id = ResultVersion, CompetitionId = Competition, VersionNo = 1, Status = CompetitionResultVersionStatus.Published,
            ComputedAt = Utc(2026, 9, 15), PublishedAt = Utc(2026, 9, 16)
        });
        db.ExamSessions.Add(new ExamSession
        {
            Id = ExamSessionId, CompetitionId = Competition, Type = ExamSessionType.Written, Title = "الاختبار الكتابي",
            ScheduledAt = Utc(2026, 9, 1), DurationMinutes = 120, Venue = "القاعة 1", Capacity = 10, MaxScore = 100,
            Status = ExamSessionStatus.MarkingClosed
        });
        var d1 = Comp(db, "D1", RecruitmentIntakeChannel.Public, 1, RecruitmentApplicationStatus.Ranked, R4,
            [(8, 1, S.Submitted), (8, 5, S.UnderReview), (8, 10, S.Shortlisted), (9, 25, S.Ranked)], ExamAttendance.Present, CompetitionResultOutcome.Passed);
        db.CompetitionAllocations.Add(new CompetitionAllocation { CompetitionId = Competition, RecruitmentApplicationId = d1.Id, AllocatedLineId = Line, AllocatedRequisitionId = R4 });
        var d2 = Comp(db, "D2", RecruitmentIntakeChannel.Public, 2, RecruitmentApplicationStatus.ReserveListed, null,
            [(8, 2, S.Submitted), (8, 5, S.UnderReview), (8, 10, S.Shortlisted), (9, 25, S.ReserveListed)], ExamAttendance.Present, CompetitionResultOutcome.Passed);
        db.CompetitionReserveEntries.Add(new CompetitionReserveEntry { CompetitionLineId = Line, RecruitmentApplicationId = d2.Id, LineRank = 1, ReserveUntil = new DateOnly(2027, 3, 25) });
        var d3 = Comp(db, "D3", RecruitmentIntakeChannel.Internal, 3, RecruitmentApplicationStatus.Rejected, null,
            [(8, 3, S.Submitted), (8, 5, S.UnderReview), (8, 10, S.Shortlisted), (9, 25, S.Rejected)], ExamAttendance.Present, CompetitionResultOutcome.Failed,
            rejected: (Utc(2026, 9, 25), RecruitmentRejectionKind.CompetitionResult));
        Appeal(db, d3, RecruitmentAppealKind.ResultObjection, filed: Utc(2026, 9, 20), decided: Utc(2026, 9, 24), RecruitmentAppealOutcome.ObjectionDismissed);
        var d4 = Comp(db, "D4", RecruitmentIntakeChannel.Public, 4, RecruitmentApplicationStatus.Rejected, null,
            [(8, 4, S.Submitted), (8, 6, S.UnderReview), (8, 10, S.Shortlisted), (9, 25, S.Rejected)], ExamAttendance.Absent, CompetitionResultOutcome.Absent,
            rejected: (Utc(2026, 9, 25), RecruitmentRejectionKind.CompetitionResult));
        Appeal(db, d4, RecruitmentAppealKind.ResultObjection, filed: Utc(2026, 9, 21), decided: Utc(2026, 9, 23), RecruitmentAppealOutcome.ObjectionUpheld);
        Comp(db, "D5", RecruitmentIntakeChannel.Public, 5, RecruitmentApplicationStatus.Rejected, null,
            [(8, 5, S.Submitted), (8, 8, S.Rejected)], attendance: null, outcome: null,
            rejected: (Utc(2026, 8, 8), RecruitmentRejectionKind.CompetitionResult));
        var d6 = Comp(db, "D6", RecruitmentIntakeChannel.Public, 6, RecruitmentApplicationStatus.Ranked, R4,
            [(8, 6, S.Submitted), (8, 7, S.UnderReview), (8, 10, S.Shortlisted), (9, 25, S.ReserveListed), (10, 10, S.Ranked)], ExamAttendance.Present, CompetitionResultOutcome.Passed);
        db.CompetitionReserveEntries.Add(new CompetitionReserveEntry
        {
            CompetitionLineId = Line, RecruitmentApplicationId = d6.Id, LineRank = 2, ReserveUntil = new DateOnly(2027, 3, 25),
            Status = CompetitionReserveEntryStatus.Recalled, RecalledRequisitionId = R4
        });
        db.CompetitionAllocations.Add(new CompetitionAllocation { CompetitionId = Competition, RecruitmentApplicationId = d6.Id, AllocatedLineId = Line, AllocatedRequisitionId = R4, FromReserve = true });

        // ---- workflows ---------------------------------------------------------------------------------------------
        Definition(db, WfRequisition, "RECRUITMENT-REQUISITION-APPROVAL", "اعتماد طلب شاغر", nameof(RecruitmentRequisition),
            new WorkflowStepSnapshot { Level = 1, Name = "مدير الموارد البشرية", RoleRequired = "hr-manager" },
            new WorkflowStepSnapshot { Level = 2, Name = "المدير العام", RoleRequired = "hr-management-approver" });
        Definition(db, WfOffer, "RECRUITMENT-OFFER-APPROVAL", "اعتماد خطاب عرض", nameof(OfferLetter),
            new WorkflowStepSnapshot { Level = 1, Name = "اعتماد العرض", RoleRequired = "hr-management-approver", TimeoutHours = 24 });
        Definition(db, WfCompetition, "RECRUITMENT-COMPETITION-APPROVAL", "اعتماد مسابقة", nameof(RecruitmentCompetition),
            new WorkflowStepSnapshot { Level = 1, Name = "لجنة المسابقة", RoleRequired = "hr-core" });

        // WI1: R3 approved in two steps (24 h, 48 h).
        Instance(db, WfRequisition, nameof(RecruitmentRequisition), R3, Utc(2026, 3, 1, 8), WorkflowStatus.Completed, 2, Utc(2026, 3, 4, 8),
            (1, "مدير الموارد البشرية", WorkflowStepStatus.Approved, Utc(2026, 3, 2, 8)), (2, "المدير العام", WorkflowStepStatus.Approved, Utc(2026, 3, 4, 8)));
        // WI2: R5 waiting at step 2 since 2026-10-21 08:00 → 196 h > 72 h (option) → overdue.
        Instance(db, WfRequisition, nameof(RecruitmentRequisition), R5, Utc(2026, 10, 20, 8), WorkflowStatus.InProgress, 2, null,
            (1, "مدير الموارد البشرية", WorkflowStepStatus.Approved, Utc(2026, 10, 21, 8)));
        // WI3: O6 (HQ) approved in 12 h. WI4: O4 (branch) approved in 12 h.
        Instance(db, WfOffer, nameof(OfferLetter), o6, Utc(2026, 10, 24, 9), WorkflowStatus.Completed, 1, Utc(2026, 10, 24, 21),
            (1, "اعتماد العرض", WorkflowStepStatus.Approved, Utc(2026, 10, 24, 21)));
        Instance(db, WfOffer, nameof(OfferLetter), o4, Utc(2026, 9, 27, 8), WorkflowStatus.Completed, 1, Utc(2026, 9, 27, 20),
            (1, "اعتماد العرض", WorkflowStepStatus.Approved, Utc(2026, 9, 27, 20)));
        // WI5: O5 (branch) waiting 10 h < 24 h step timeout → not overdue.
        Instance(db, WfOffer, nameof(OfferLetter), o5, Utc(2026, 10, 29, 2), WorkflowStatus.InProgress, 1, null);
        // WI6: competition approval waiting since 2026-10-01 08:00 → 676 h > 48 h default → overdue (no unit: unrestricted only).
        Instance(db, WfCompetition, nameof(RecruitmentCompetition), Competition, Utc(2026, 10, 1, 8), WorkflowStatus.InProgress, 1, null);

        await db.SaveChangesAsync();
        // Competition → current result version → competition is a cycle within one insert batch.
        competition.CurrentResultVersionId = ResultVersion;
        await db.SaveChangesAsync();
    }

    private static class S
    {
        public const RecruitmentApplicationStatus Submitted = RecruitmentApplicationStatus.Submitted;
        public const RecruitmentApplicationStatus UnderReview = RecruitmentApplicationStatus.UnderReview;
        public const RecruitmentApplicationStatus Shortlisted = RecruitmentApplicationStatus.Shortlisted;
        public const RecruitmentApplicationStatus Rejected = RecruitmentApplicationStatus.Rejected;
        public const RecruitmentApplicationStatus Hired = RecruitmentApplicationStatus.Hired;
        public const RecruitmentApplicationStatus InterviewScheduled = RecruitmentApplicationStatus.InterviewScheduled;
        public const RecruitmentApplicationStatus Assessed = RecruitmentApplicationStatus.Assessed;
        public const RecruitmentApplicationStatus Ranked = RecruitmentApplicationStatus.Ranked;
        public const RecruitmentApplicationStatus OfferExtended = RecruitmentApplicationStatus.OfferExtended;
        public const RecruitmentApplicationStatus OfferAccepted = RecruitmentApplicationStatus.OfferAccepted;
        public const RecruitmentApplicationStatus Withdrawn = RecruitmentApplicationStatus.Withdrawn;
        public const RecruitmentApplicationStatus OfferDeclined = RecruitmentApplicationStatus.OfferDeclined;
        public const RecruitmentApplicationStatus ReserveListed = RecruitmentApplicationStatus.ReserveListed;
    }

    private static void Position(HRCoreDbContext db, Guid id, Guid unit, FundedPositionStatus status, DateTime created) =>
        db.FundedPositions.Add(new FundedPosition
        {
            Id = id, PositionCode = "POS-H32-" + id.ToString("N")[^2..], GradeCode = "G5", BudgetYear = 2026, Status = status,
            OrgUnitId = unit, CreatedAt = created
        });

    private static void Requisition(HRCoreDbContext db, Guid id, string number, Guid position, RequisitionStatus status,
        DateTime requested, DateTime? approved, DateTime? published, DateTime? closed = null, DateTime? cancelled = null, Guid? line = null) =>
        db.RecruitmentRequisitions.Add(new RecruitmentRequisition
        {
            Id = id, RequisitionNumber = number, FundedPositionId = position, JobTitle = "محلل " + number, Status = status,
            RequestedDate = requested, ApprovedDate = approved, PublishedAt = published, ClosedDate = closed, CancelledAt = cancelled,
            VacancyCount = 1, CompetitionLineId = line, CreatedAt = requested
        });

    private static Employee Employee(HRCoreDbContext db, DateTime created)
    {
        var n = Interlocked.Increment(ref _n);
        var employee = new Employee { FullName = $"موظف معيّن {n}", NationalId = $"{NationalIdPrefix}{n:0000000}", CreatedAt = created };
        db.Employees.Add(employee);
        return employee;
    }

    private static RecruitmentApplication App(HRCoreDbContext db, string label, Guid? requisition, RecruitmentIntakeChannel channel,
        DateTime applied, RecruitmentApplicationStatus status, DateTime? screened = null, DateTime? ranked = null, Employee? hired = null,
        (DateTime At, RecruitmentRejectionKind Kind)? rejected = null)
    {
        var n = Interlocked.Increment(ref _n);
        var app = new RecruitmentApplication
        {
            ApplicantFullName = CandidateNamePrefix + label,
            NationalId = $"{NationalIdPrefix}{n:0000000}",
            RequisitionId = requisition,
            AppliedPosition = "محلل",
            IntakeChannel = channel,
            AppliedAt = applied,
            Status = status,
            ScreenedAt = screened,
            RankedAt = ranked,
            HiredEmployeeId = hired?.Id,
            RejectedAt = rejected?.At,
            RejectionKind = rejected?.Kind,
            CreatedAt = applied
        };
        db.RecruitmentApplications.Add(app);
        return app;
    }

    private static RecruitmentApplication Comp(HRCoreDbContext db, string label, RecruitmentIntakeChannel channel, int seat,
        RecruitmentApplicationStatus status, Guid? requisition, (int M, int D, RecruitmentApplicationStatus To)[] history,
        ExamAttendance? attendance, CompetitionResultOutcome? outcome, (DateTime At, RecruitmentRejectionKind Kind)? rejected = null)
    {
        var app = App(db, label, requisition, channel, Utc(2026, history[0].M, history[0].D), status, rejected: rejected);
        app.CompetitionId = Competition;
        app.CompetitionLineId = requisition is null ? null : Line;
        Events(db, app, history);
        var examNumber = $"3200000{seat}";
        if (attendance is { } present)
        {
            db.ExamCandidates.Add(new ExamCandidate
            {
                ExamSessionId = ExamSessionId, CompetitionId = Competition, ExamType = ExamSessionType.Written,
                RecruitmentApplicationId = app.Id, ExamNumber = examNumber, SeatNumber = seat, Attendance = present
            });
        }
        if (outcome is { } result)
            db.CompetitionResults.Add(new CompetitionResult { ResultVersionId = ResultVersion, RecruitmentApplicationId = app.Id, ExamNumber = examNumber, Outcome = result });
        return app;
    }

    private static Guid Offer(HRCoreDbContext db, RecruitmentApplication app, Guid position, string number, OfferStatus status,
        DateTime created, DateTime? issued = null, DateTime? accepted = null, DateTime? declined = null, DateTime? expired = null,
        DateTime? revoked = null, string? declinedBy = null, OfferDeclineReasonCategory? category = null, bool merit = false,
        bool gradeDeviation = false)
    {
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id, FundedPositionId = position, OfferNumber = "OFF-H32-" + number, Status = status,
            CreatedAt = created, IssuedDate = issued, ExpiryDate = issued?.AddDays(14), AcceptedDate = accepted, DeclinedAt = declined,
            DeclinedBy = declinedBy, DeclineReason = declined is null ? null : "سبب الرفض", DeclineReasonCategory = category,
            ExpiredAt = expired, RevokedAt = revoked, IsMeritOverride = merit,
            MeritOverrideReason = merit ? "توصية اللجنة" : null, GradeDeviationReason = gradeDeviation ? "درجة مختلفة" : null,
            OfferedSalary = 500000, ProposedStartDate = Utc(2026, 11, 15)
        };
        db.OfferLetters.Add(offer);
        return offer.Id;
    }

    private static void Events(HRCoreDbContext db, RecruitmentApplication app, params (int M, int D, RecruitmentApplicationStatus To)[] history)
    {
        for (var i = 0; i < history.Length; i++)
        {
            var (m, d, to) = history[i];
            Event(db, app, i == 0 ? RecruitmentApplicationEventTypes.Received : RecruitmentApplicationEventTypes.StatusChanged,
                (int)to, Utc(2026, m, d), from: i == 0 ? null : (int)history[i - 1].To);
        }
    }

    private static void Event(HRCoreDbContext db, RecruitmentApplication app, string type, int toStatus, DateTime at, Guid? related = null, int? from = null) =>
        db.RecruitmentApplicationEvents.Add(new RecruitmentApplicationEvent
        {
            RecruitmentApplicationId = app.Id, EventType = type, FromStatus = from, ToStatus = toStatus, OccurredAt = at,
            RelatedEntityId = related, NotificationsPlannedAt = at
        });

    private static void Appeal(HRCoreDbContext db, RecruitmentApplication app, RecruitmentAppealKind kind, DateTime filed, DateTime? decided,
        RecruitmentAppealOutcome? outcome) =>
        db.RecruitmentAppeals.Add(new RecruitmentAppeal
        {
            RecruitmentApplicationId = app.Id, Kind = kind, Reason = "سبب الاستئناف للاختبار", CreatedAt = filed,
            Status = decided is null ? RecruitmentAppealStatus.Pending : RecruitmentAppealStatus.Decided, DecidedAt = decided, Outcome = outcome
        });

    private static void Definition(HRCoreDbContext db, Guid id, string code, string name, string entityType, params WorkflowStepSnapshot[] steps) =>
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = id, Code = code, Name = name, Version = 1, IsPublished = true, DomainEntityType = entityType,
            StepsJson = JsonSerializer.Serialize(steps)
        });

    private static void Instance(HRCoreDbContext db, Guid definition, string entityType, Guid entityId, DateTime started, WorkflowStatus status,
        int currentLevel, DateTime? completed, params (int Level, string Name, WorkflowStepStatus Status, DateTime At)[] history) =>
        db.WorkflowInstances.Add(new WorkflowInstance
        {
            DefinitionId = definition, DefinitionVersion = 1, EntityType = entityType, EntityId = entityId, StartedAt = started,
            Status = status, CurrentStepLevel = currentLevel, CompletedAt = completed,
            StepHistoryJson = JsonSerializer.Serialize(history.Select(h => new WorkflowStepHistoryEntry
            {
                Level = h.Level, StepName = h.Name, Status = h.Status, ActionAt = h.At
            }).ToList())
        });
}
