using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-32 (AC-HCM-32): agency A fixtures of the recruitment reports, with fixed dates and explicit event histories
/// (application events suppressed — nothing is notified). Added only when absent (keyed by <see cref="AcceptanceSeedIds"/>);
/// dedicated positions <c>AC32-POS-*</c>, so the other journeys keep their vacancies.
/// <list type="bullet">
/// <item><b>REQ-2026-A-AC32-1</b> (RIF, br1-mgr's scope): requested 2026-08-01, approved 2026-08-04, closed 2026-09-15 by the
/// hire of applicant 1 (Hired event 2026-09-10 → time to fill 37 days). Six applications, furthest stages
/// 1 Hired · 2 Ranked · 3 Screened · 4 Applied (withdrawn) · 5 OfferIssued (offer declined, «الراتب») · 6 Applied → funnel
/// 6, 4, 3, 3, 3, 3, 2, 1, 1. Offers: accepted (1), declined (5). Applicant 3 appealed (dismissed).</item>
/// <item><b>REQ-2026-A-AC32-2</b> (Aleppo, out of br1-mgr's scope): published, three applications.</item>
/// <item><b>REQ-2026-A-AC32-3</b> (RIF): pending approval since 2026-09-01 at step 1 of RECRUITMENT-REQUISITION-APPROVAL —
/// past the 48 h default SLA (AC-HCM-32d).</item>
/// </list>
/// Applicants carry 11-digit national ids <c>320320000NN</c> so the exports can be proved free of them (AC-HCM-32c).
/// </summary>
public static class AcceptanceRecruitmentReportsPatch
{
    private static readonly DateTime Approved1 = Utc(2026, 8, 4);

    private static DateTime Utc(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    public static async Task ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var agencyId = AcceptanceSeedIds.AgencyAId;
        if (!await All(db.Tenants).AnyAsync(t => t.Id == agencyId, ct))
            return;
        if (await All(db.RecruitmentRequisitions).AnyAsync(r => r.Id == AcceptanceSeedIds.RecruitmentReportsRequisition1Id, ct))
            return;

        var previousTenant = TenantIdFlow.Get();
        TenantIdFlow.Set(agencyId);
        try
        {
            using (db.SuppressApplicationEvents())
            {
                await SeedAsync(db, agencyId, logger, ct);
            }
        }
        finally
        {
            TenantIdFlow.Set(previousTenant);
            db.ChangeTracker.Clear();
        }
    }

    private static IQueryable<T> All<T>(DbSet<T> set) where T : class =>
        set.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization);

    private static Guid Id(string key) => AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "ac32-" + key);

    private static async Task SeedAsync(HRCoreDbContext db, Guid tenant, ILogger? logger, CancellationToken ct)
    {
        var p1 = Position(db, tenant, "AC32-POS-01", AcceptanceSeedIds.Br1OrgUnitId, FundedPositionStatus.Occupied);
        var p2 = Position(db, tenant, "AC32-POS-02", AcceptanceSeedIds.Br2OrgUnitId, FundedPositionStatus.Vacant);
        var p3 = Position(db, tenant, "AC32-POS-03", AcceptanceSeedIds.Br1OrgUnitId, FundedPositionStatus.Vacant);

        var r1 = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentReportsRequisition1Id, AcceptanceSeedIds.RecruitmentReportsRequisition1Number,
            p1, RequisitionStatus.Closed, Utc(2026, 8, 1), Approved1, Utc(2026, 8, 6));
        r1.ClosedDate = Utc(2026, 9, 15);
        r1.VacancyCount = 0;
        var r2 = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentReportsRequisition2Id, AcceptanceSeedIds.RecruitmentReportsRequisition2Number,
            p2, RequisitionStatus.Published, Utc(2026, 8, 5), Utc(2026, 8, 7), Utc(2026, 8, 9));
        var r3 = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentReportsRequisition3Id, AcceptanceSeedIds.RecruitmentReportsRequisition3Number,
            p3, RequisitionStatus.PendingApproval, Utc(2026, 9, 1), approved: null, published: null);

        const RecruitmentApplicationStatus S0 = RecruitmentApplicationStatus.Submitted, UR = RecruitmentApplicationStatus.UnderReview,
            SL = RecruitmentApplicationStatus.Shortlisted, RK = RecruitmentApplicationStatus.Ranked, OE = RecruitmentApplicationStatus.OfferExtended,
            OA = RecruitmentApplicationStatus.OfferAccepted, H = RecruitmentApplicationStatus.Hired, RJ = RecruitmentApplicationStatus.Rejected,
            WD = RecruitmentApplicationStatus.Withdrawn, OD = RecruitmentApplicationStatus.OfferDeclined;

        var a1 = App(db, tenant, 1, r1, RecruitmentIntakeChannel.Public, H, [(8, 10, S0), (8, 12, UR), (8, 15, SL), (8, 25, RK), (8, 28, OE), (9, 1, OA), (9, 10, H)]);
        var a2 = App(db, tenant, 2, r1, RecruitmentIntakeChannel.Public, RJ, [(8, 11, S0), (8, 13, UR), (8, 16, SL), (8, 25, RK), (9, 10, RJ)]);
        a2.RejectedAt = Utc(2026, 9, 10);
        a2.RejectionKind = RecruitmentRejectionKind.NotSelectedOnHire;
        var a3 = App(db, tenant, 3, r1, RecruitmentIntakeChannel.Internal, RJ, [(8, 12, S0), (8, 14, UR), (8, 20, RJ)]);
        a3.RejectedAt = Utc(2026, 8, 20);
        a3.RejectionKind = RecruitmentRejectionKind.SelectionDecision;
        App(db, tenant, 4, r1, RecruitmentIntakeChannel.HrEntered, WD, [(8, 13, S0), (8, 18, WD)]);
        var a5 = App(db, tenant, 5, r1, RecruitmentIntakeChannel.Public, OD, [(8, 14, S0), (8, 15, UR), (8, 18, SL), (8, 25, RK), (8, 26, OE), (8, 30, OD)]);
        var a6 = App(db, tenant, 6, r1, RecruitmentIntakeChannel.Legacy, RJ, [(8, 15, S0), (9, 15, RJ)]);
        a6.RejectedAt = Utc(2026, 9, 15);
        a6.RejectionKind = RecruitmentRejectionKind.RequisitionClosed;
        for (var i = 7; i <= 9; i++)
            App(db, tenant, i, r2, RecruitmentIntakeChannel.Public, UR, [(8, 10 + i, S0), (8, 12 + i, UR)]);

        var offer1 = Offer(db, tenant, "OFF-2026-A-AC32-1", a1, p1, OfferStatus.Accepted, issued: Utc(2026, 8, 28), outcome: Utc(2026, 9, 1));
        offer1.IsConvertedToEmployee = true;
        var offer5 = Offer(db, tenant, "OFF-2026-A-AC32-5", a5, p1, OfferStatus.Declined, issued: Utc(2026, 8, 26), outcome: Utc(2026, 8, 30));
        offer5.DeclineReason = "الراتب أقل من المتوقع (اختبار قبول)";
        offer5.DeclineReasonCategory = OfferDeclineReasonCategory.Salary;
        offer5.DeclinedBy = OfferDeclinedBy.Candidate;
        Event(db, tenant, a1.Id, RecruitmentApplicationEventTypes.OfferIssued, (int)OfferStatus.Issued, Utc(2026, 8, 28), offer1.Id);
        Event(db, tenant, a1.Id, RecruitmentApplicationEventTypes.OfferAccepted, (int)OfferStatus.Accepted, Utc(2026, 9, 1), offer1.Id);
        Event(db, tenant, a5.Id, RecruitmentApplicationEventTypes.OfferIssued, (int)OfferStatus.Issued, Utc(2026, 8, 26), offer5.Id);

        db.RecruitmentAppeals.Add(new RecruitmentAppeal
        {
            Id = Id("appeal-3"), TenantId = tenant, RecruitmentApplicationId = a3.Id, Kind = RecruitmentAppealKind.Rejection,
            Reason = "استئناف قرار الفرز (اختبار قبول)", Status = RecruitmentAppealStatus.Decided, Outcome = RecruitmentAppealOutcome.Dismissed,
            DecidedAt = Utc(2026, 9, 5), FiledBy = "Candidate", CreatedAt = Utc(2026, 8, 22)
        });

        var definition = await All(db.WorkflowDefinitions)
            .Where(d => d.Code == "RECRUITMENT-REQUISITION-APPROVAL" && d.IsPublished && (d.TenantId == tenant || d.TenantId == TenantDefaults.DefaultTenantId))
            .OrderByDescending(d => d.TenantId == tenant).ThenByDescending(d => d.Version)
            .Select(d => new { d.Id, d.Version })
            .FirstOrDefaultAsync(ct);
        if (definition is null)
        {
            logger?.LogWarning("PLAN-32 acceptance patch: RECRUITMENT-REQUISITION-APPROVAL is not seeded; AC-HCM-32d has no overdue instance.");
        }
        else
        {
            db.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = AcceptanceSeedIds.RecruitmentReportsWorkflowInstanceId, TenantId = tenant, DefinitionId = definition.Id,
                DefinitionVersion = definition.Version, EntityType = nameof(RecruitmentRequisition), EntityId = r3.Id,
                CurrentStepLevel = 1, Status = WorkflowStatus.InProgress, StepHistoryJson = "[]", StartedAt = Utc(2026, 9, 1)
            });
            r3.WorkflowInstanceId = AcceptanceSeedIds.RecruitmentReportsWorkflowInstanceId;
        }

        await db.SaveChangesAsync(ct);
        logger?.LogInformation("PLAN-32 acceptance patch: recruitment report fixtures seeded.");
    }

    private static FundedPosition Position(HRCoreDbContext db, Guid tenant, string code, Guid unit, FundedPositionStatus status)
    {
        var position = new FundedPosition
        {
            Id = Id("position-" + code), TenantId = tenant, PositionCode = code, GradeCode = "G5", BudgetYear = 2026, Status = status,
            OrgUnitId = unit, CreatedAt = Utc(2026, 7, 1)
        };
        db.FundedPositions.Add(position);
        return position;
    }

    private static RecruitmentRequisition Requisition(HRCoreDbContext db, Guid tenant, Guid id, string number, FundedPosition position,
        RequisitionStatus status, DateTime requested, DateTime? approved, DateTime? published)
    {
        var requisition = new RecruitmentRequisition
        {
            Id = id, TenantId = tenant, RequisitionNumber = number, FundedPositionId = position.Id, JobTitle = "محلل بيانات (AC-HCM-32)",
            Status = status, RequestedDate = requested, ApprovedDate = approved, PublishedAt = published, VacancyCount = 1,
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
            JustificationNotes = "طلب شاغر ثابت لاختبارات قبول تقارير التوظيف (PLAN-32).", CreatedAt = requested, UpdatedAt = requested
        };
        db.RecruitmentRequisitions.Add(requisition);
        return requisition;
    }

    private static RecruitmentApplication App(HRCoreDbContext db, Guid tenant, int n, RecruitmentRequisition requisition,
        RecruitmentIntakeChannel channel, RecruitmentApplicationStatus status, (int M, int D, RecruitmentApplicationStatus To)[] history)
    {
        var nationalId = AcceptanceSeedIds.RecruitmentReportsNationalIdPrefix + n.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        var applied = Utc(2026, history[0].M, history[0].D);
        var app = new RecruitmentApplication
        {
            Id = Id("application-" + n), TenantId = tenant, ApplicantFullName = $"{AcceptanceSeedIds.RecruitmentReportsApplicantPrefix} {n}",
            NationalId = nationalId, RequisitionId = requisition.Id,
            AppliedPosition = requisition.JobTitle, IntakeChannel = channel, AppliedAt = applied, Status = status, CreatedAt = applied,
            ScreenedAt = history.Any(h => h.To == RecruitmentApplicationStatus.UnderReview) ? Utc(2026, history[1].M, history[1].D) : null,
            RankedAt = history.FirstOrDefault(h => h.To == RecruitmentApplicationStatus.Ranked) is { M: > 0 } ranked ? Utc(2026, ranked.M, ranked.D) : null
        };
        db.RecruitmentApplications.Add(app);
        for (var i = 0; i < history.Length; i++)
            Event(db, tenant, app.Id, i == 0 ? RecruitmentApplicationEventTypes.Received : RecruitmentApplicationEventTypes.StatusChanged,
                (int)history[i].To, Utc(2026, history[i].M, history[i].D), from: i == 0 ? null : (int)history[i - 1].To);
        return app;
    }

    private static OfferLetter Offer(HRCoreDbContext db, Guid tenant, string number, RecruitmentApplication app, FundedPosition position,
        OfferStatus status, DateTime issued, DateTime outcome)
    {
        var offer = new OfferLetter
        {
            Id = Id("offer-" + number), TenantId = tenant, OfferNumber = number, RecruitmentApplicationId = app.Id, FundedPositionId = position.Id,
            Status = status, CreatedAt = issued.AddDays(-1), IssuedDate = issued, ExpiryDate = issued.AddDays(14),
            AcceptedDate = status == OfferStatus.Accepted ? outcome : null, AcceptedBy = status == OfferStatus.Accepted ? OfferAcceptedBy.Candidate : null,
            DeclinedAt = status == OfferStatus.Declined ? outcome : null, OfferedSalary = 500000, GradeCode = "G5",
            ProposedStartDate = Utc(2026, 10, 1), LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);
        return offer;
    }

    private static void Event(HRCoreDbContext db, Guid tenant, Guid applicationId, string type, int toStatus, DateTime at, Guid? related = null, int? from = null) =>
        db.RecruitmentApplicationEvents.Add(new RecruitmentApplicationEvent
        {
            Id = Id($"event-{applicationId:N}-{type}-{toStatus}-{at.Ticks}"), TenantId = tenant, RecruitmentApplicationId = applicationId,
            EventType = type, FromStatus = from, ToStatus = toStatus, OccurredAt = at, RelatedEntityId = related,
            // Seeded history is never notified.
            NotificationsPlannedAt = at
        });
}
