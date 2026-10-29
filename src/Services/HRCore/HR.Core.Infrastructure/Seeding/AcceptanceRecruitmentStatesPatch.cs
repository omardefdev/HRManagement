using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-33 (AC-HCM-21/22, AC-WF-23): deterministic recruitment states in agency A, RIF (br1-mgr's scope), listed in
/// <c>testdata/acceptance/recruitment-states.json</c>. Added only when absent (keyed by <see cref="AcceptanceSeedIds"/>);
/// application events suppressed. The scenarios only read these states or are refused on them, so they survive repeated
/// runs; scenarios that change state create their own requisition on a pool position (<c>AC33-POOL-01…40</c>).
/// <c>scripts/acceptance-reset.ps1</c> drops the stack volumes, and the next boot seeds the states again.
/// <list type="bullet">
/// <item>Requisitions by <c>central-a</c>: Draft; PendingApproval at step 1 and at step 2 (step 1 approved by br1-mgr) of
/// RECRUITMENT-REQUISITION-APPROVAL with live instances; Approved; Published (closing date 2028-08-01) with criteria v1.</item>
/// <item>Applications on the published requisition: Submitted, Shortlisted, Assessed (two assessors), Ranked 1/2/3, one
/// with an offer PendingApproval by central-a (live RECRUITMENT-OFFER-APPROVAL at step 1), one with an Issued offer, and
/// one Rejected with a pending appeal assigned to <c>a-compliance</c> (live RECRUITMENT-APPEAL-REVIEW).</item>
/// <item>Committee sessions on the approved requisition: one with a chair + 3 members, one with a chair only.</item>
/// <item>A legacy <see cref="ApprovalRequest"/> (step 1 role <c>hr-core</c>) requested by central-a.</item>
/// </list>
/// </summary>
public static class AcceptanceRecruitmentStatesPatch
{
    private const string AgencyCode = "AGENCY-A";
    private static readonly DateTime Created = Utc(2026, 10, 1);
    private static readonly DateOnly ClosingDate = new(2028, 8, 1);

    private static DateTime Utc(int y, int m, int d, int h = 9) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static Guid Id(string key) => AcceptanceSeedCatalog.DeterministicGuid(AgencyCode, "ac33-" + key);

    public static async Task ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var agencyId = AcceptanceSeedIds.AgencyAId;
        if (!await All(db.Tenants).AnyAsync(t => t.Id == agencyId, ct))
            return;
        if (await All(db.RecruitmentRequisitions).AnyAsync(r => r.Id == AcceptanceSeedIds.RecruitmentStatesDraftRequisitionId, ct))
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

    private static async Task SeedAsync(HRCoreDbContext db, Guid tenant, ILogger? logger, CancellationToken ct)
    {
        var centralA = AcceptanceKeycloakIds.PersonaUserId("central-a");
        var br1Mgr = AcceptanceKeycloakIds.PersonaUserId("br1-mgr");
        var unit = AcceptanceSeedIds.Br1OrgUnitId;

        for (var i = 0; i < AcceptanceSeedIds.RecruitmentStatesPoolSize; i++)
            Position(db, tenant, AcceptanceSeedIds.RecruitmentStatesPoolPositionId(i), $"AC33-POOL-{i + 1:D2}", unit);

        var draft = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentStatesDraftRequisitionId,
            AcceptanceSeedIds.RecruitmentStatesDraftRequisitionNumber, Position(db, tenant, Id("position-draft"), "AC33-POS-DRAFT", unit),
            RequisitionStatus.Draft, centralA);
        var pending1 = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentStatesPendingStep1RequisitionId,
            AcceptanceSeedIds.RecruitmentStatesPendingStep1RequisitionNumber, Position(db, tenant, Id("position-pend1"), "AC33-POS-PEND1", unit),
            RequisitionStatus.PendingApproval, centralA);
        var pending2 = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentStatesPendingStep2RequisitionId,
            AcceptanceSeedIds.RecruitmentStatesPendingStep2RequisitionNumber, Position(db, tenant, Id("position-pend2"), "AC33-POS-PEND2", unit),
            RequisitionStatus.PendingApproval, centralA);
        var approved = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentStatesApprovedRequisitionId,
            AcceptanceSeedIds.RecruitmentStatesApprovedRequisitionNumber, Position(db, tenant, Id("position-appr"), "AC33-POS-APPR", unit),
            RequisitionStatus.Approved, centralA);
        approved.ApprovedDate = Created.AddDays(2);
        var publishedPosition = Position(db, tenant, Id("position-pub"), "AC33-POS-PUB", unit);
        var published = Requisition(db, tenant, AcceptanceSeedIds.RecruitmentStatesPublishedRequisitionId,
            AcceptanceSeedIds.RecruitmentStatesPublishedRequisitionNumber, publishedPosition, RequisitionStatus.Published, centralA);
        published.ApprovedDate = Created.AddDays(2);
        published.PublishedAt = Created.AddDays(3);
        published.ApplicationClosingDate = ClosingDate;
        var posting = RecruitmentRequisitionService.MapToPublicJobPosting(published, publishedPosition);
        posting.Id = AcceptanceSeedIds.RecruitmentStatesPublishedPostingId;
        db.PublicJobPostings.Add(posting);
        published.PublicJobPosting = posting;
        published.PublicJobPostingId = posting.Id;
        _ = draft;

        var criteria = new RecruitmentAssessmentCriteria
        {
            Id = AcceptanceSeedIds.RecruitmentStatesCriteriaId, TenantId = tenant, RequisitionId = published.Id, VersionNumber = 1,
            Title = "معايير التقييم v1", CriteriaJson = RecruitmentSeedCriteria.Json, IsActive = true,
            EffectiveFrom = Created, CreatedAt = Created, UpdatedAt = Created, CreatedBy = centralA
        };
        db.RecruitmentAssessmentCriteria.Add(criteria);

        const RecruitmentApplicationStatus Ranked = RecruitmentApplicationStatus.Ranked;
        App(db, tenant, AcceptanceSeedIds.RecruitmentStatesSubmittedApplicationId, 1, published, RecruitmentApplicationStatus.Submitted);
        var shortlisted = App(db, tenant, AcceptanceSeedIds.RecruitmentStatesShortlistedApplicationId, 2, published, RecruitmentApplicationStatus.Shortlisted);
        shortlisted.ConflictOfInterestDeclared = true;
        shortlisted.ConflictOfInterestDeclaredAt = Created.AddDays(5);
        var assessed = Assessed(db, tenant, AcceptanceSeedIds.RecruitmentStatesAssessedApplicationId, 3, published, criteria,
            RecruitmentApplicationStatus.Assessed, 74m, rank: null);
        var ranked = new[]
        {
            Assessed(db, tenant, AcceptanceSeedIds.RecruitmentStatesRanked1ApplicationId, 4, published, criteria, Ranked, 91m, rank: 1),
            Assessed(db, tenant, AcceptanceSeedIds.RecruitmentStatesRanked2ApplicationId, 5, published, criteria, Ranked, 86m, rank: 2),
            Assessed(db, tenant, AcceptanceSeedIds.RecruitmentStatesRanked3ApplicationId, 6, published, criteria, Ranked, 81m, rank: 3)
        };
        var offerPendingApp = Assessed(db, tenant, AcceptanceSeedIds.RecruitmentStatesOfferPendingApplicationId, 7, published, criteria,
            RecruitmentApplicationStatus.OfferExtended, 79m, rank: 4);
        var offerIssuedApp = Assessed(db, tenant, AcceptanceSeedIds.RecruitmentStatesOfferIssuedApplicationId, 8, published, criteria,
            RecruitmentApplicationStatus.OfferExtended, 77m, rank: 5);
        var appealApp = App(db, tenant, AcceptanceSeedIds.RecruitmentStatesAppealApplicationId, 9, published, RecruitmentApplicationStatus.Rejected);
        appealApp.RejectedAt = Created.AddDays(6);
        appealApp.RejectionKind = RecruitmentRejectionKind.SelectionDecision;
        appealApp.AppealStatus = RecruitmentAppealStatus.Pending;
        _ = assessed;
        _ = ranked;

        var pendingOffer = Offer(db, tenant, AcceptanceSeedIds.RecruitmentStatesPendingOfferId, AcceptanceSeedIds.RecruitmentStatesPendingOfferNumber,
            offerPendingApp, publishedPosition, OfferStatus.PendingApproval, centralA);
        var issuedOffer = Offer(db, tenant, AcceptanceSeedIds.RecruitmentStatesIssuedOfferId, AcceptanceSeedIds.RecruitmentStatesIssuedOfferNumber,
            offerIssuedApp, publishedPosition, OfferStatus.Issued, centralA);
        issuedOffer.IssuedDate = Created.AddDays(8);
        issuedOffer.ExpiryDate = Utc(2028, 8, 1);

        var complianceEmployee = AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id;
        var appeal = new RecruitmentAppeal
        {
            Id = AcceptanceSeedIds.RecruitmentStatesPendingAppealId, TenantId = tenant, RecruitmentApplicationId = appealApp.Id,
            Kind = RecruitmentAppealKind.Rejection, Status = RecruitmentAppealStatus.Pending,
            Reason = "استئناف قرار الفرز في حالة قبول ثابتة لاختبارات PLAN-33.", FiledBy = RecruitmentAppealFiledBy.HrOnBehalf,
            FiledByEmployeeId = AcceptancePersonaCatalog.Get("central-a").Employee?.Id,
            IndependentReviewerEmployeeId = complianceEmployee, AssignedAt = Created.AddDays(7), AssignedBy = centralA,
            CreatedAt = Created.AddDays(7), UpdatedAt = Created.AddDays(7), CreatedBy = centralA
        };
        db.RecruitmentAppeals.Add(appeal);

        await db.SaveChangesAsync(ct);

        // Application ↔ active offer is a cycle for the relational insert order: link the offers in a second save.
        offerPendingApp.ActiveOfferLetterId = pendingOffer.Id;
        offerIssuedApp.ActiveOfferLetterId = issuedOffer.Id;
        await db.SaveChangesAsync(ct);

        await StartAsync(db, tenant, "RECRUITMENT-REQUISITION-APPROVAL", nameof(RecruitmentRequisition), pending1.Id,
            AcceptanceSeedIds.RecruitmentStatesPendingStep1WorkflowId, centralA, step: 1, approvedSteps: [], logger, ct,
            id => pending1.WorkflowInstanceId = id);
        await StartAsync(db, tenant, "RECRUITMENT-REQUISITION-APPROVAL", nameof(RecruitmentRequisition), pending2.Id,
            AcceptanceSeedIds.RecruitmentStatesPendingStep2WorkflowId, centralA, step: 2,
            approvedSteps: [new WorkflowStepHistoryEntry
            {
                Level = 1, StepName = "مراجعة HR", Status = WorkflowStepStatus.Approved, ActorId = br1Mgr,
                ActorName = AcceptancePersonaCatalog.Get("br1-mgr").Username, Comments = "PLAN-33 seeded step 1", ActionAt = Created.AddDays(1)
            }], logger, ct, id => pending2.WorkflowInstanceId = id);
        await StartAsync(db, tenant, "RECRUITMENT-OFFER-APPROVAL", nameof(OfferLetter), pendingOffer.Id,
            AcceptanceSeedIds.RecruitmentStatesPendingOfferWorkflowId, centralA, step: 1, approvedSteps: [], logger, ct,
            id => pendingOffer.WorkflowInstanceId = id);
        await StartAsync(db, tenant, "RECRUITMENT-APPEAL-REVIEW", nameof(RecruitmentAppeal), appeal.Id,
            AcceptanceSeedIds.RecruitmentStatesPendingAppealWorkflowId, centralA, step: 1, approvedSteps: [], logger, ct,
            id => appeal.WorkflowInstanceId = id);

        await SeedCommitteeSessionsAsync(db, tenant, approved, centralA, ct);

        var legacy = new ApprovalRequest
        {
            Id = AcceptanceSeedIds.RecruitmentStatesLegacyApprovalRequestId, TenantId = tenant, EntityType = "ApprovedCV",
            EntityId = Id("legacy-approval-entity"), Description = "طلب اعتماد قديم ثابت لاختبار صندوق العمل (AC-WF-23f).",
            RequestedBy = centralA, RequestedByName = AcceptancePersonaCatalog.Get("central-a").Username, RequestedAt = Created.AddDays(4),
            Status = ApprovalStatus.Pending, CurrentLevel = 1, CreatedAt = Created.AddDays(4), UpdatedAt = Created.AddDays(4), CreatedBy = centralA
        };
        legacy.Steps.Add(new ApprovalStep
        {
            Id = Id("legacy-approval-step-1"), TenantId = tenant, ApprovalRequestId = legacy.Id, Level = 1, RoleRequired = "hr-core",
            Status = ApprovalStatus.Pending, CreatedAt = Created.AddDays(4), UpdatedAt = Created.AddDays(4)
        });
        db.ApprovalRequests.Add(legacy);

        await db.SaveChangesAsync(ct);
        logger?.LogInformation("PLAN-33 acceptance patch: deterministic recruitment states seeded.");
    }

    private static async Task StartAsync(HRCoreDbContext db, Guid tenant, string code, string entityType, Guid entityId, Guid instanceId,
        Guid initiatedBy, int step, List<WorkflowStepHistoryEntry> approvedSteps, ILogger? logger, CancellationToken ct, Action<Guid> link)
    {
        var definition = await All(db.WorkflowDefinitions)
            .Where(d => d.Code == code && d.IsPublished && (d.TenantId == tenant || d.TenantId == TenantDefaults.DefaultTenantId))
            .OrderByDescending(d => d.TenantId == tenant).ThenByDescending(d => d.Version)
            .Select(d => new { d.Id, d.Version })
            .FirstOrDefaultAsync(ct);
        if (definition is null)
        {
            logger?.LogWarning("PLAN-33 acceptance patch: {Code} is not seeded; {EntityType} {EntityId} has no live workflow.", code, entityType, entityId);
            return;
        }

        db.WorkflowInstances.Add(new WorkflowInstance
        {
            Id = instanceId, TenantId = tenant, DefinitionId = definition.Id, DefinitionVersion = definition.Version,
            EntityType = entityType, EntityId = entityId, CurrentStepLevel = step, Status = WorkflowStatus.InProgress,
            StepHistoryJson = JsonSerializer.Serialize(approvedSteps), InitiatedBy = initiatedBy,
            InitiatedByName = AcceptancePersonaCatalog.Get("central-a").Username, StartedAt = Created.AddDays(1)
        });
        link(instanceId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Committee sessions on the approved requisition (no assessed candidates, so no decisions are needed to submit):
    /// a chair + 3 members (PLAN-28 quorum) and a chair only (submit refused). Members are active employees linked to no
    /// persona and sitting on no other committee, so the appeal reviewer and the approvers stay independent.
    /// </summary>
    private static async Task SeedCommitteeSessionsAsync(HRCoreDbContext db, Guid tenant, RecruitmentRequisition requisition, Guid centralA,
        CancellationToken ct)
    {
        var personaEmployees = AcceptancePersonaCatalog.Personas.Where(p => p.Employee is not null).Select(p => p.Employee!.Id).ToHashSet();
        var committeeEmployees = await All(db.SelectionCommitteeMembers).Select(m => m.EmployeeId).Distinct().ToListAsync(ct);
        var candidates = await All(db.Employees)
            .Where(e => e.TenantId == tenant && e.CurrentStatus == EmploymentStatus.Active && !committeeEmployees.Contains(e.Id))
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .Take(64)
            .ToListAsync(ct);
        var members = candidates.Where(id => !personaEmployees.Contains(id)).Take(5).ToList();
        if (members.Count < 5)
            return;

        Session(db, tenant, AcceptanceSeedIds.RecruitmentStatesCommitteeReadySessionId, requisition, members.Take(4).ToList(), centralA, "ready");
        Session(db, tenant, AcceptanceSeedIds.RecruitmentStatesCommitteeShortSessionId, requisition, [members[4]], centralA, "short");
        await db.SaveChangesAsync(ct);
    }

    private static void Session(HRCoreDbContext db, Guid tenant, Guid id, RecruitmentRequisition requisition, List<Guid> members, Guid centralA,
        string key)
    {
        db.SelectionCommitteeSessions.Add(new SelectionCommitteeSession
        {
            Id = id, TenantId = tenant, RequisitionId = requisition.Id, CommitteeDate = Created.AddDays(10), Purpose = CommitteePurpose.Ranking,
            Findings = "جلسة لجنة ثابتة لاختبارات PLAN-33.", ChairEmployeeId = members[0], CreatedAt = Created, UpdatedAt = Created, CreatedBy = centralA
        });
        for (var i = 0; i < members.Count; i++)
        {
            db.SelectionCommitteeMembers.Add(new SelectionCommitteeMember
            {
                Id = Id($"committee-{key}-member-{i + 1}"), TenantId = tenant, SessionId = id, EmployeeId = members[i],
                Role = i == 0 ? SelectionCommitteeMember.ChairRole : SelectionCommitteeMember.MemberRole, CreatedAt = Created, UpdatedAt = Created
            });
        }
    }

    private static FundedPosition Position(HRCoreDbContext db, Guid tenant, Guid id, string code, Guid unit)
    {
        var position = new FundedPosition
        {
            Id = id, TenantId = tenant, PositionCode = code, GradeCode = "G5", BudgetYear = 2026, Status = FundedPositionStatus.Vacant,
            OrgUnitId = unit, CreatedAt = Created
        };
        db.FundedPositions.Add(position);
        return position;
    }

    private static RecruitmentRequisition Requisition(HRCoreDbContext db, Guid tenant, Guid id, string number, FundedPosition position,
        RequisitionStatus status, Guid createdBy)
    {
        var requisition = new RecruitmentRequisition
        {
            Id = id, TenantId = tenant, RequisitionNumber = number, FundedPositionId = position.Id, JobTitle = "محلل نظم (AC-HCM-33)",
            Status = status, RequestedDate = Created, VacancyCount = 1, PublishingChannel = RecruitmentPublishingChannel.PublicOnly,
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
            JustificationNotes = "حالة طلب شاغر ثابتة لاختبارات قبول PLAN-33.", CreatedAt = Created, UpdatedAt = Created, CreatedBy = createdBy
        };
        db.RecruitmentRequisitions.Add(requisition);
        return requisition;
    }

    private static RecruitmentApplication App(HRCoreDbContext db, Guid tenant, Guid id, int n, RecruitmentRequisition requisition,
        RecruitmentApplicationStatus status)
    {
        var nationalId = $"AC-1-AC33{n:D3}";
        var applied = Created.AddDays(4).AddHours(n);
        var app = new RecruitmentApplication
        {
            Id = id, TenantId = tenant, ApplicantFullName = $"مرشح حالة AC33 {n}", NationalId = nationalId,
            NationalIdHash = PiiMask.Sha256Hex(nationalId), RequisitionId = requisition.Id, AppliedPosition = requisition.JobTitle,
            IntakeChannel = RecruitmentIntakeChannel.HrEntered, AppliedAt = applied, Status = status, CreatedAt = applied, UpdatedAt = applied,
            ScreenedAt = status == RecruitmentApplicationStatus.Submitted ? null : applied.AddDays(1)
        };
        db.RecruitmentApplications.Add(app);
        return app;
    }

    /// <summary>An application with an attended interview and two complete assessors whose mean equals <paramref name="score"/>.</summary>
    private static RecruitmentApplication Assessed(HRCoreDbContext db, Guid tenant, Guid id, int n, RecruitmentRequisition requisition,
        RecruitmentAssessmentCriteria criteria, RecruitmentApplicationStatus status, decimal score, int? rank)
    {
        var app = App(db, tenant, id, n, requisition, status);
        app.ConflictOfInterestDeclared = true;
        app.ConflictOfInterestDeclaredAt = Created.AddDays(5);
        app.AssessmentScore = score;
        app.CriteriaVersionId = criteria.Id;
        app.ReviewedAt = Created.AddDays(7);
        if (rank is not null)
        {
            app.Rank = rank;
            app.RankedAt = Created.AddDays(8);
        }

        var interviewAt = Created.AddDays(6);
        db.RecruitmentInterviewSlots.Add(new RecruitmentInterviewSlot
        {
            Id = Id($"interview-{n}"), TenantId = tenant, RecruitmentApplicationId = app.Id, ScheduledAt = interviewAt, Location = "قاعة المقابلات",
            Status = InterviewSlotStatus.Completed, Mode = InterviewMode.InPerson, CompletedAt = interviewAt.AddHours(1),
            Outcome = InterviewOutcome.Attended, CreatedAt = Created, UpdatedAt = Created
        });

        // Two assessors 4 points apart around the score (equal per-item percentages), so the mean is the score.
        foreach (var (assessor, delta) in new[] { ("assessor-1", -2m), ("assessor-2", 2m) })
        {
            var assessorUserId = Id(assessor);
            foreach (var item in RecruitmentSeedCriteria.Items)
            {
                db.RecruitmentAssessmentScores.Add(new RecruitmentAssessmentScore
                {
                    Id = Id($"score-{n}-{assessor}-{item.Key}"), TenantId = tenant, RecruitmentApplicationId = app.Id,
                    CriteriaVersionId = criteria.Id, ItemKey = item.Key, AssessorUserId = assessorUserId,
                    Score = Math.Round((score + delta) / 100m * item.MaxScore, 2, MidpointRounding.AwayFromZero), NoConflictDeclared = true,
                    SubmittedAt = interviewAt.AddHours(2), CreatedAt = Created, UpdatedAt = Created
                });
            }
        }

        return app;
    }

    private static OfferLetter Offer(HRCoreDbContext db, Guid tenant, Guid id, string number, RecruitmentApplication app, FundedPosition position,
        OfferStatus status, Guid createdBy)
    {
        var offer = new OfferLetter
        {
            Id = id, TenantId = tenant, OfferNumber = number, RecruitmentApplicationId = app.Id, FundedPositionId = position.Id, Status = status,
            OfferedSalary = 500000m, GradeCode = "G5", ValidityDays = 14, ProposedStartDate = Utc(2026, 12, 1),
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
            CreatedAt = Created.AddDays(8), UpdatedAt = Created.AddDays(8), CreatedBy = createdBy
        };
        db.OfferLetters.Add(offer);
        return offer;
    }
}
