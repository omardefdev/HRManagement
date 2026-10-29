using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-31 (AC-HCM-31): agency A fixtures for the competition acceptance tests, applied after the seed pack like the other
/// acceptance patches; every fixture is keyed by a fixed <see cref="AcceptanceSeedIds"/> id and added only when absent.
/// <list type="bullet">
/// <item>AC-HCM-31a: three Approved, unpublished requisitions with exam-only criteria (two of grade A, one of grade B) that
/// the test groups into a new competition through the UI.</item>
/// <item>AC-HCM-31b–g: competition <see cref="AcceptanceSeedIds.CompetitionNumber"/> at <c>IntakeClosed</c> — line 1
/// (grade A, 3 published requisitions) and line 2 (grade B, 2), frozen exam-only criteria, two planned written sessions
/// (60 seats, max 100), 120 admitted (Shortlisted) applicants with ordered preferences, its posting, and the first two
/// applicants linked to citizen rows (e-mail + consent) for <c>/careers/my</c>.</item>
/// </list>
/// The positions are dedicated (<c>AC31-POS-*</c>, never <c>VAC-*</c>), so the other journeys keep their free vacancies.
/// The legal inputs are acceptance test data, not legal values. Application events are suppressed: seeded history is
/// never notified.
/// </summary>
public static class AcceptanceCompetitionPatch
{
    public const string PortalCitizen1Reference = "AC31-CMP-0001";
    public const string PortalCitizen2Reference = "AC31-CMP-0002";

    /// <summary>National id of the <paramref name="index"/>-th seeded applicant (test data; 11 digits, prefix 31).</summary>
    public static string ApplicantNationalId(int index) =>
        "31" + (700_000_000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static Guid ApplicantId(int index) => AcceptanceSeedIds.CompetitionApplicantId(index);

    public static async Task ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var agencyId = AcceptanceSeedIds.AgencyAId;
        if (!await All(db.Tenants).AnyAsync(t => t.Id == agencyId, ct))
            return;

        var previousTenant = TenantIdFlow.Get();
        TenantIdFlow.Set(agencyId);
        try
        {
            using (db.SuppressApplicationEvents())
            {
                var jobs = await PickJobsAsync(db, agencyId, ct);
                if (jobs is null)
                {
                    logger?.LogWarning("PLAN-31 acceptance patch: agency A has no two active job catalog entries of different grades.");
                    return;
                }
                await EnsureDraftRequisitionsAsync(db, jobs.Value, ct);
                await EnsureCompetitionAsync(db, jobs.Value, ct);
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

    private static async Task<(JobCatalogEntry A, JobCatalogEntry B)?> PickJobsAsync(HRCoreDbContext db, Guid agencyId, CancellationToken ct)
    {
        // Payroll grades G1–G10 first, so the bulk offers (AC-HCM-31g) find the grade bounds.
        var jobs = (await All(db.JobCatalogEntries)
                .Where(j => j.TenantId == agencyId && j.IsActive && j.GradeCode != "")
                .OrderBy(j => j.JobCode)
                .ToListAsync(ct))
            .OrderBy(j => System.Text.RegularExpressions.Regex.IsMatch(j.GradeCode, "^G([1-9]|10)$") ? 0 : 1)
            .ToList();
        var a = jobs.FirstOrDefault();
        var b = jobs.FirstOrDefault(j => a is not null && !string.Equals(j.GradeCode, a.GradeCode, StringComparison.OrdinalIgnoreCase));
        return a is null || b is null ? null : (a, b);
    }

    /// <summary>
    /// A leaf unit of the RIF branch (br1-mgr scope, like the PLAN-30 requisition): the offer approval checks the approver's
    /// org scope against the position, and br1-mgr takes the first step of the bulk offers (AC-HCM-31g).
    /// </summary>
    private static Guid OrgUnitId()
    {
        var agency = AcceptanceSeedCatalog.Agencies.First(a => a.Id == AcceptanceSeedIds.AgencyAId);
        var site = AcceptanceSeedCatalog.GetOrgUnit(AcceptanceSeedIds.Br1OrgUnitId).SiteCode;
        return agency.OrgUnits.Where(u => u.SiteCode == site && !u.IsSiteRoot).OrderBy(u => u.Code, StringComparer.Ordinal).First().Id;
    }

    private static string CriteriaJson() =>
        AssessmentCriteriaSerializer.Serialize([new AssessmentCriteriaItem("EXAM", "الامتحان الكتابي", 100m, 100m, AssessmentStage.Exam)]);

    /// <summary>A dedicated vacant position and a requisition on it (+ its active exam-only criteria version).</summary>
    private static RecruitmentRequisition AddRequisition(
        HRCoreDbContext db, Guid id, string number, string positionCode, JobCatalogEntry job, RequisitionStatus status, Guid? lineId)
    {
        var reference = AcceptanceSeedCatalog.ReferenceDate;
        var position = new FundedPosition
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", $"competition-position-{positionCode}"),
            TenantId = AcceptanceSeedIds.AgencyAId,
            PositionCode = positionCode,
            GradeCode = job.GradeCode,
            FTE = 1m,
            BudgetYear = reference.Year,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = OrgUnitId(),
            JobCatalogEntryId = job.Id,
            CostCenter = "CC-AC31",
            CreatedAt = reference.AddDays(-30),
            UpdatedAt = reference
        };
        db.FundedPositions.Add(position);
        var published = status == RequisitionStatus.Published;
        var requisition = new RecruitmentRequisition
        {
            Id = id,
            TenantId = AcceptanceSeedIds.AgencyAId,
            RequisitionNumber = number,
            FundedPositionId = position.Id,
            JobTitle = job.TitleAr,
            Status = status,
            RequestedDate = reference.AddDays(-30),
            ApprovedDate = reference.AddDays(-25),
            PublishedAt = published ? reference.AddDays(-20) : null,
            IntakeClosedAt = published ? reference.AddDays(-2) : null,
            JustificationNotes = "طلب شاغر ثابت لاختبارات قبول المسابقات (PLAN-31).",
            VacancyCount = 1,
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
            PublishingChannel = RecruitmentPublishingChannel.PublicOnly,
            ApplicationClosingDate = AcceptanceDataSeeder.AcceptanceSeedApplicationClosingDate,
            CompetitionLineId = lineId,
            CreatedAt = reference.AddDays(-30),
            UpdatedAt = reference
        };
        db.RecruitmentRequisitions.Add(requisition);
        db.RecruitmentAssessmentCriteria.Add(new RecruitmentAssessmentCriteria
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", $"competition-criteria-{number}"),
            TenantId = AcceptanceSeedIds.AgencyAId,
            RequisitionId = requisition.Id,
            VersionNumber = 1,
            Title = "معايير المسابقة v1",
            CriteriaJson = CriteriaJson(),
            IsActive = true,
            EffectiveFrom = reference.AddDays(-30),
            CreatedAt = reference,
            UpdatedAt = reference
        });
        return requisition;
    }

    private static async Task EnsureDraftRequisitionsAsync(HRCoreDbContext db, (JobCatalogEntry A, JobCatalogEntry B) jobs, CancellationToken ct)
    {
        (Guid Id, string Number, string Position, JobCatalogEntry Job)[] rows =
        [
            (AcceptanceSeedIds.CompetitionDraftRequisition1Id, "REQ-2026-A-AC31D1", "AC31-POS-D1", jobs.A),
            (AcceptanceSeedIds.CompetitionDraftRequisition2Id, "REQ-2026-A-AC31D2", "AC31-POS-D2", jobs.A),
            (AcceptanceSeedIds.CompetitionDraftRequisition3Id, "REQ-2026-A-AC31D3", "AC31-POS-D3", jobs.B)
        ];
        var ids = rows.Select(r => r.Id).ToList();
        var existing = await All(db.RecruitmentRequisitions).Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        foreach (var row in rows.Where(r => !existing.Contains(r.Id)))
            AddRequisition(db, row.Id, row.Number, row.Position, row.Job, RequisitionStatus.Approved, null);
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureCompetitionAsync(HRCoreDbContext db, (JobCatalogEntry A, JobCatalogEntry B) jobs, CancellationToken ct)
    {
        if (await All(db.RecruitmentCompetitions).AnyAsync(c => c.Id == AcceptanceSeedIds.CompetitionId, ct))
            return;

        var reference = AcceptanceSeedCatalog.ReferenceDate;
        var tenant = AcceptanceSeedIds.AgencyAId;
        var competition = new RecruitmentCompetition
        {
            Id = AcceptanceSeedIds.CompetitionId,
            TenantId = tenant,
            Number = AcceptanceSeedIds.CompetitionNumber,
            Title = "مسابقة AC-HCM-31 للتعيين",
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
            ApplicationConditions = "شروط التقديم وفق الإعلان (بيانات اختبار القبول).",
            PublishingChannel = RecruitmentPublishingChannel.PublicOnly,
            Status = RecruitmentCompetitionStatus.IntakeClosed,
            AnnouncementDate = DateOnly.FromDateTime(reference.AddDays(-20)),
            ApplicationClosingDate = DateOnly.FromDateTime(reference.AddDays(-2)),
            AnnouncedAt = reference.AddDays(-20),
            IntakeClosedAt = reference.AddDays(-2),
            ApprovedAt = reference.AddDays(-21),
            // Acceptance test data (decision 1: per-competition inputs; no legal value is asserted here).
            ObjectionWindowDays = 7,
            ReserveValidityMonths = 6,
            MaxLinesPerCandidate = 2,
            PassMark = 50m,
            ReconcileThreshold = 10m,
            ReconcileRule = CompetitionReconcileRule.ReconcilerDecides,
            TieBreakOrder = "ExamScore,AppliedAt",
            CreatedAt = reference.AddDays(-22),
            UpdatedAt = reference
        };
        db.RecruitmentCompetitions.Add(competition);
        var posting = new PublicJobPosting
        {
            Id = AcceptanceSeedIds.CompetitionPostingId,
            TenantId = tenant,
            Title = competition.Title,
            Description = "السطر 1 (3 شواغر)، السطر 2 (شاغران)",
            Requirements = competition.ApplicationConditions,
            ClosingDate = competition.ApplicationClosingDate,
            IsPublished = true,
            RequisitionId = null
        };
        db.PublicJobPostings.Add(posting);
        competition.PublicJobPostingId = posting.Id;

        var line1 = new CompetitionLine
        {
            Id = AcceptanceSeedIds.CompetitionLine1Id, TenantId = tenant, CompetitionId = competition.Id, LineNo = 1,
            Title = $"{jobs.A.TitleAr} — الإدارة المركزية", GradeCode = jobs.A.GradeCode, JobCatalogEntryId = jobs.A.Id, RequiredCount = 3
        };
        var line2 = new CompetitionLine
        {
            Id = AcceptanceSeedIds.CompetitionLine2Id, TenantId = tenant, CompetitionId = competition.Id, LineNo = 2,
            Title = $"{jobs.B.TitleAr} — الإدارة المركزية", GradeCode = jobs.B.GradeCode, JobCatalogEntryId = jobs.B.Id, RequiredCount = 2
        };
        db.CompetitionLines.AddRange(line1, line2);
        var frozenCriteria = new Dictionary<CompetitionLine, Guid>();
        for (var i = 0; i < 5; i++)
        {
            var line = i < 3 ? line1 : line2;
            var requisition = AddRequisition(db, AcceptanceSeedIds.CompetitionRequisitionId(i), $"REQ-2026-A-AC31-{i + 1}",
                $"AC31-POS-{i + 1:D2}", i < 3 ? jobs.A : jobs.B, RequisitionStatus.Published, line.Id);
            // Frozen at submit in the UI flow: each line takes the criteria of its first requisition (J13). Set after the
            // first save: line → criteria → requisition → line is a cycle within one insert batch.
            if (i is 0 or 3)
                frozenCriteria[line] = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", $"competition-criteria-{requisition.RequisitionNumber}");
        }

        foreach (var (id, n) in new[] { (AcceptanceSeedIds.CompetitionSession1Id, 1), (AcceptanceSeedIds.CompetitionSession2Id, 2) })
            db.ExamSessions.Add(new ExamSession
            {
                Id = id,
                TenantId = tenant,
                CompetitionId = competition.Id,
                Type = ExamSessionType.Written,
                Title = $"الامتحان الكتابي — القاعة {n}",
                ScheduledAt = reference.AddDays(5).AddHours(8 + n),
                DurationMinutes = 120,
                Venue = $"المبنى الرئيسي — القاعة {n}",
                Capacity = 60,
                MaxScore = 100m,
                Status = ExamSessionStatus.Planned
            });
        await db.SaveChangesAsync(ct);
        foreach (var (line, criteriaId) in frozenCriteria)
            line.CriteriaVersionId = criteriaId;
        await db.SaveChangesAsync(ct);

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (var i = 0; i < AcceptanceSeedIds.CompetitionApplicantCount; i++)
            {
                var nationalId = ApplicantNationalId(i);
                var application = new RecruitmentApplication
                {
                    Id = ApplicantId(i),
                    TenantId = tenant,
                    ApplicantFullName = $"متقدم المسابقة {i + 1:D3}",
                    NationalId = nationalId,
                    NationalIdHash = PiiMask.Sha256Hex(nationalId),
                    CompetitionId = competition.Id,
                    AppliedPosition = competition.Title,
                    AppliedAt = reference.AddDays(-15).AddMinutes(i),
                    Status = RecruitmentApplicationStatus.Shortlisted,
                    ConflictOfInterestDeclared = true,
                    IntakeChannel = RecruitmentIntakeChannel.Public,
                    EducationLevelCode = "BACH",
                    YearsOfExperience = 3m,
                    CreatedAt = reference.AddDays(-15),
                    UpdatedAt = reference
                };
                db.RecruitmentApplications.Add(application);
                // Even applicants prefer line 1 then 2, odd ones line 2 then 1; every tenth applies to line 1 only.
                Guid[] order = i % 10 == 9 ? [line1.Id] : i % 2 == 0 ? [line1.Id, line2.Id] : [line2.Id, line1.Id];
                for (var p = 0; p < order.Length; p++)
                    db.CompetitionPreferences.Add(new CompetitionPreference
                    {
                        Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", $"competition-preference-{i:D4}-{p}"),
                        TenantId = tenant,
                        RecruitmentApplicationId = application.Id,
                        CompetitionLineId = order[p],
                        Order = p + 1
                    });
                if (i < 2)
                    db.CitizenApplications.Add(new CitizenApplication
                    {
                        Id = i == 0 ? AcceptanceSeedIds.CompetitionPortalCitizen1Id : AcceptanceSeedIds.CompetitionPortalCitizen2Id,
                        TenantId = tenant,
                        PublicJobPostingId = posting.Id,
                        PromotedRecruitmentApplicationId = application.Id,
                        ReferenceNumber = i == 0 ? PortalCitizen1Reference : PortalCitizen2Reference,
                        FullName = application.ApplicantFullName,
                        NationalId = nationalId,
                        NationalIdHash = application.NationalIdHash,
                        Email = $"ac31-portal-{i + 1}@test.sy",
                        NotificationChannel = NotificationChannel.Email,
                        NotificationConsentAt = reference.AddDays(-15),
                        SubmittedAt = application.AppliedAt,
                        Status = CitizenApplicationStatus.UnderReview,
                        ConflictOfInterestDeclared = true,
                        ConflictOfInterestDeclaredAt = application.AppliedAt,
                        CreatedAt = reference.AddDays(-15),
                        UpdatedAt = reference
                    });
            }
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }
}
