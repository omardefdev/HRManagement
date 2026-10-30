using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-30 (AC-HCM-30): agency A fixtures for the candidate-experience acceptance tests. Always run after the seed pack
/// (like the other acceptance patches), so an existing stack gets them without a reset; every fixture is keyed by a fixed
/// <see cref="AcceptanceSeedIds"/> id and added only when absent (a row the tests or the retention job changed is never
/// recreated or reset).
/// <list type="bullet">
/// <item>AC-HCM-30a–e/h: a published requisition (public channel) with eligibility criteria (BACH, 2 years), required
/// documents (CV + certificate), assessment criteria and its public posting.</item>
/// <item>AC-HCM-30j: two requisition-linked citizen rows on it without a recruitment application (pre-unification shape,
/// Received and UnderReview) for the backfill; submitted recently so the legacy retention rule never anonymizes them.
/// The seed pack's internal application (<see cref="AcceptanceSeedIds.Br1InternalApplicationId"/>) is the internal row.</item>
/// <item>AC-HCM-30i: a requisition cancelled four years before the reference date with two public pairs
/// (recruitment application + linked citizen row): one due (with one candidate document) and one under legal hold.</item>
/// </list>
/// Application events are suppressed: seeded history is never notified.
/// </summary>
public static class AcceptanceCandidateExperiencePatch
{
    public const string BackfillReceivedReference = "AC30-BF-0001";
    public const string BackfillUnderReviewReference = "AC30-BF-0002";
    public const string RetentionDueReference = "AC30-RET-0001";
    public const string RetentionHeldReference = "AC30-RET-0002";
    public const string RetentionDueName = "مرشح احتفاظ مستحق";
    public const string RetentionHeldName = "مرشح احتفاظ محجوز قانونياً";

    public static async Task ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var agencyId = AcceptanceSeedIds.AgencyAId;
        if (!await db.Tenants.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization).AnyAsync(t => t.Id == agencyId, ct))
            return;

        var previousTenant = TenantIdFlow.Get();
        TenantIdFlow.Set(agencyId);
        try
        {
            using (db.SuppressApplicationEvents())
            {
                var requisition = await EnsureCandidateExperienceRequisitionAsync(db, logger, ct);
                if (requisition is not null)
                    await EnsureBackfillRowsAsync(db, requisition, ct);
                await EnsureRetentionPairsAsync(db, ct);
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

    private static async Task<RecruitmentRequisition?> EnsureCandidateExperienceRequisitionAsync(
        HRCoreDbContext db, ILogger? logger, CancellationToken ct)
    {
        var existing = await All(db.RecruitmentRequisitions)
            .FirstOrDefaultAsync(r => r.Id == AcceptanceSeedIds.CandidateExperienceRequisitionId, ct);
        if (existing is not null)
            return existing;

        var agency = AcceptanceSeedCatalog.Agencies.First(a => a.Id == AcceptanceSeedIds.AgencyAId);
        var usedPositionIds = await All(db.RecruitmentRequisitions)
            .Where(r => r.TenantId == agency.Id)
            .Select(r => r.FundedPositionId)
            .ToListAsync(ct);
        // In the RIF branch (br1-mgr scope, panel employee EmpBr1), as the PLAN-28 committee requisition. The last free
        // VAC- position, so the AC-HCM-26..29 journeys (first free graded position) keep theirs.
        var branchUnitIds = agency.OrgUnits
            .Where(u => u.SiteCode == AcceptanceSeedCatalog.GetOrgUnit(AcceptanceSeedIds.Br1OrgUnitId).SiteCode)
            .Select(u => u.Id)
            .ToList();
        var position = await All(db.FundedPositions)
            .Include(p => p.JobCatalogEntry)
            .Include(p => p.OrgUnit)
            .Where(p => p.TenantId == agency.Id
                && p.Status == FundedPositionStatus.Vacant
                && p.PositionCode.StartsWith("VAC-")
                && branchUnitIds.Contains(p.OrgUnitId)
                && !usedPositionIds.Contains(p.Id))
            .OrderByDescending(p => p.PositionCode)
            .FirstOrDefaultAsync(ct);
        if (position is null)
        {
            logger?.LogWarning("PLAN-30 acceptance patch: no free RIF vacancy for the candidate-experience requisition.");
            return null;
        }

        var reference = AcceptanceSeedCatalog.ReferenceDate;
        var requisition = new RecruitmentRequisition
        {
            Id = AcceptanceSeedIds.CandidateExperienceRequisitionId,
            TenantId = agency.Id,
            RequisitionNumber = AcceptanceSeedIds.CandidateExperienceRequisitionNumber,
            FundedPositionId = position.Id,
            JobTitle = "وظيفة AC-HCM-30 (تجربة المرشح)",
            Status = RequisitionStatus.Published,
            RequestedDate = reference.AddDays(-20),
            ApprovedDate = reference.AddDays(-15),
            PublishedAt = reference.AddDays(-14),
            JustificationNotes = "طلب شاغر ثابت لاختبارات قبول تجربة المرشح (PLAN-30).",
            VacancyCount = 1,
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
            PublishingChannel = RecruitmentPublishingChannel.PublicOnly,
            ApplicationClosingDate = AcceptanceDataSeeder.AcceptanceSeedApplicationClosingDate,
            MinQualificationCode = "BACH",
            MinYearsOfExperience = 2m,
            RequiredDocumentTypes = CandidateDocumentTypes.Format([CandidateDocumentTypes.Cv, CandidateDocumentTypes.Certificate]),
            CreatedAt = reference.AddDays(-20),
            UpdatedAt = reference
        };
        db.RecruitmentRequisitions.Add(requisition);
        var posting = RecruitmentRequisitionService.MapToPublicJobPosting(requisition, position);
        posting.Id = AcceptanceSeedIds.CandidateExperiencePostingId;
        db.PublicJobPostings.Add(posting);
        requisition.PublicJobPosting = posting;
        requisition.PublicJobPostingId = posting.Id; // as RecruitmentRequisitionLifecycleService.PublishAsync does
        db.RecruitmentAssessmentCriteria.Add(new RecruitmentAssessmentCriteria
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "candidate-experience-criteria"),
            TenantId = agency.Id,
            RequisitionId = requisition.Id,
            VersionNumber = 1,
            Title = "معايير التقييم v1",
            CriteriaJson = RecruitmentSeedCriteria.Json,
            IsActive = true,
            EffectiveFrom = reference.AddDays(-20),
            CreatedAt = reference,
            UpdatedAt = reference
        });
        await db.SaveChangesAsync(ct);
        return requisition;
    }

    private static async Task EnsureBackfillRowsAsync(HRCoreDbContext db, RecruitmentRequisition requisition, CancellationToken ct)
    {
        (Guid Id, string Reference, string Name, CitizenApplicationStatus Status)[] rows =
        [
            (AcceptanceSeedIds.CandidateBackfillCitizenReceivedId, BackfillReceivedReference, "متقدم قبل التوحيد أ", CitizenApplicationStatus.Received),
            (AcceptanceSeedIds.CandidateBackfillCitizenUnderReviewId, BackfillUnderReviewReference, "متقدم قبل التوحيد ب", CitizenApplicationStatus.UnderReview)
        ];
        var ids = rows.Select(r => r.Id).ToList();
        var present = await All(db.CitizenApplications).Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        // Recent dates (not ReferenceDate-based): the legacy retention rule anonymizes unlinked rows 36 months after
        // submission, and the acceptance stack runs retention with Apply=true.
        var submitted = DateTime.UtcNow.Date.AddDays(-3);
        var added = false;
        for (var i = 0; i < rows.Length; i++)
        {
            if (present.Contains(rows[i].Id))
                continue;
            var nationalId = $"AC-1-CXBF{i + 1:D2}";
            db.CitizenApplications.Add(new CitizenApplication
            {
                Id = rows[i].Id,
                TenantId = requisition.TenantId,
                PublicJobPostingId = AcceptanceSeedIds.CandidateExperiencePostingId,
                RequisitionId = requisition.Id,
                ReferenceNumber = rows[i].Reference,
                FullName = rows[i].Name,
                NationalId = nationalId,
                NationalIdHash = PiiMask.Sha256Hex(nationalId),
                Email = $"ac30-backfill-{i + 1}@test.sy",
                Status = rows[i].Status,
                SubmittedAt = submitted,
                CreatedAt = submitted,
                UpdatedAt = submitted
            });
            added = true;
        }

        if (added)
            await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureRetentionPairsAsync(HRCoreDbContext db, CancellationToken ct)
    {
        var agencyId = AcceptanceSeedIds.AgencyAId;
        var reference = AcceptanceSeedCatalog.ReferenceDate;
        var cancelledAt = reference.AddYears(-4);
        if (!await All(db.RecruitmentRequisitions).AnyAsync(r => r.Id == AcceptanceSeedIds.CandidateRetentionRequisitionId, ct))
        {
            // A cancelled requisition blocks no position: it sits on the seeded (occupied) BR1 position.
            var position = await All(db.FundedPositions)
                .Include(p => p.JobCatalogEntry)
                .Include(p => p.OrgUnit)
                .FirstOrDefaultAsync(p => p.Id == AcceptanceSeedIds.Br1FundedPositionId, ct);
            if (position is null)
                return;

            var requisition = new RecruitmentRequisition
            {
                Id = AcceptanceSeedIds.CandidateRetentionRequisitionId,
                TenantId = agencyId,
                RequisitionNumber = AcceptanceSeedIds.CandidateRetentionRequisitionNumber,
                FundedPositionId = position.Id,
                JobTitle = "وظيفة ملغاة AC-HCM-30i (احتفاظ)",
                Status = RequisitionStatus.Cancelled,
                RequestedDate = cancelledAt.AddMonths(-2),
                ApprovedDate = cancelledAt.AddMonths(-2).AddDays(5),
                PublishedAt = cancelledAt.AddMonths(-2).AddDays(6),
                CancelledAt = cancelledAt,
                JustificationNotes = "طلب شاغر ملغى منذ أربع سنوات لاختبار الاحتفاظ ببيانات المرشحين (PLAN-30).",
                VacancyCount = 1,
                PublishingChannel = RecruitmentPublishingChannel.PublicOnly,
                CreatedAt = cancelledAt.AddMonths(-2),
                UpdatedAt = cancelledAt
            };
            db.RecruitmentRequisitions.Add(requisition);
            var posting = RecruitmentRequisitionService.MapToPublicJobPosting(requisition, position);
            posting.Id = AcceptanceSeedCatalog.DeterministicGuid(AcceptanceSeedIds.AgencyACode, "candidate-retention-posting");
            posting.IsPublished = false;
            db.PublicJobPostings.Add(posting);
            requisition.PublicJobPosting = posting;
            requisition.PublicJobPostingId = posting.Id;
            await db.SaveChangesAsync(ct);
        }

        var postingId = await All(db.RecruitmentRequisitions)
            .Where(r => r.Id == AcceptanceSeedIds.CandidateRetentionRequisitionId)
            .Select(r => r.PublicJobPostingId)
            .FirstAsync(ct);
        if (postingId is not Guid retentionPostingId)
            return;

        (Guid AppId, Guid CitizenId, string Reference, string Name, bool Hold)[] pairs =
        [
            (AcceptanceSeedIds.CandidateRetentionDueApplicationId, AcceptanceSeedIds.CandidateRetentionDueCitizenId, RetentionDueReference, RetentionDueName, false),
            (AcceptanceSeedIds.CandidateRetentionHeldApplicationId, AcceptanceSeedIds.CandidateRetentionHeldCitizenId, RetentionHeldReference, RetentionHeldName, true)
        ];
        var appliedAt = cancelledAt.AddMonths(-1);
        for (var i = 0; i < pairs.Length; i++)
        {
            var pair = pairs[i];
            if (await All(db.RecruitmentApplications).AnyAsync(a => a.Id == pair.AppId, ct))
                continue;

            var nationalId = $"AC-1-CXRT{i + 1:D2}";
            var application = new RecruitmentApplication
            {
                Id = pair.AppId,
                TenantId = agencyId,
                ApplicantFullName = pair.Name,
                NationalId = nationalId,
                NationalIdHash = PiiMask.Sha256Hex(nationalId),
                RequisitionId = AcceptanceSeedIds.CandidateRetentionRequisitionId,
                AppliedPosition = "وظيفة ملغاة AC-HCM-30i (احتفاظ)",
                AppliedAt = appliedAt,
                Status = RecruitmentApplicationStatus.Rejected,
                RejectionKind = RecruitmentRejectionKind.RequisitionCancelled,
                RejectedAt = cancelledAt,
                ReviewedAt = cancelledAt,
                IntakeChannel = RecruitmentIntakeChannel.Public,
                LegalHold = pair.Hold,
                Notes = $"تقديم عبر البوابة العامة {pair.Reference}",
                CreatedAt = appliedAt,
                UpdatedAt = cancelledAt
            };
            db.RecruitmentApplications.Add(application);
            db.CitizenApplications.Add(new CitizenApplication
            {
                Id = pair.CitizenId,
                TenantId = agencyId,
                PublicJobPostingId = retentionPostingId,
                RequisitionId = AcceptanceSeedIds.CandidateRetentionRequisitionId,
                PromotedRecruitmentApplicationId = application.Id,
                ReferenceNumber = pair.Reference,
                FullName = pair.Name,
                NationalId = nationalId,
                NationalIdHash = PiiMask.Sha256Hex(nationalId),
                Email = $"ac30-retention-{i + 1}@test.sy",
                CoverNote = "رسالة تغطية لاختبار الاحتفاظ",
                Status = CitizenApplicationStatus.Received,
                SubmittedAt = appliedAt,
                CreatedAt = appliedAt,
                UpdatedAt = cancelledAt
            });
            if (!pair.Hold)
            {
                db.CandidateDocuments.Add(new CandidateDocument
                {
                    Id = AcceptanceSeedIds.CandidateRetentionDueDocumentId,
                    TenantId = agencyId,
                    RecruitmentApplicationId = application.Id,
                    CitizenApplicationId = pair.CitizenId,
                    DocumentType = CandidateDocumentTypes.Cv,
                    // Not a File.Storage file: AC-HCM-30i asserts the release intent in the FileReferenceOutbox.
                    FileId = AcceptanceSeedIds.CandidateRetentionDueFileId,
                    OriginalFileName = "cv-retention.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 1024,
                    UploadedAt = appliedAt,
                    UploadedBy = CandidateDocument.UploadedByCandidate,
                    CreatedAt = appliedAt,
                    UpdatedAt = appliedAt
                });
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
