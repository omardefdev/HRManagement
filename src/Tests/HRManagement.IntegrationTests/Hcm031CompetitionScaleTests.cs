using System.Diagnostics;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-031 (PLAN-31, P7): scale of one competition on a migrated PostgreSQL — 10,000 admitted applicants, 4 lines,
/// 20 written sessions. Seating, results computation and finalization (allocation) together must stay under 60 s.
/// Opt-in (<c>HCM031_PERF=1</c>, see <c>perf/hcm031-competition-scale.ps1</c>); the timings are written to
/// <c>artifacts/hcm031-perf/</c>. Marking and the approval workflow are not timed: marks are stored directly (test data)
/// and the version is published directly once computed.
/// </summary>
[Trait("Category", "Hcm031Perf")]
public class Hcm031CompetitionScaleTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    private const int Applicants = 10_000;
    private const int Lines = 4;
    private const int Sessions = 20;
    private const int VacanciesPerLine = 25;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    private static readonly Guid Actor = Guid.Parse("31310000-0000-4000-8000-0000000000b1");

    [SkippableFact]
    public async Task Seating_results_and_allocation_of_10000_applicants_stay_under_60_seconds()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HCM031_PERF") == "1", "Set HCM031_PERF=1 (perf/hcm031-competition-scale.ps1).");
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var competitionId = await SeedAsync(tenant);

        var seating = Stopwatch.StartNew();
        await using (var db = fixture.CreateContext(tenant))
        {
            var cards = new ExamAdmissionCardDocumentService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));
            var seated = await new ExamSeatingService(db, cards).SeatAsync(competitionId, Actor);
            Assert.True(seated.Success, seated.Error);
            Assert.Equal(Applicants, seated.Seated);
        }
        seating.Stop();

        await MarkAsync(tenant, competitionId);

        var results = Stopwatch.StartNew();
        Guid versionId;
        await using (var db = fixture.CreateContext(tenant))
        {
            var (version, error) = await new CompetitionResultsService(db, Mock.Of<IWorkflowStarter>()).ComputeDraftAsync(competitionId, null, Actor);
            Assert.True(version is not null, error);
            versionId = version!.Id;
        }
        results.Stop();

        await PublishAsync(tenant, competitionId, versionId);

        var allocation = Stopwatch.StartNew();
        CompetitionFinalizeResult finalized;
        await using (var db = fixture.CreateContext(tenant))
            finalized = await CreateAllocation(db).FinalizeAsync(competitionId, Actor);
        allocation.Stop();
        Assert.True(finalized.Success, finalized.Error);
        Assert.Equal(Lines * VacanciesPerLine, finalized.Allocated);
        Assert.Equal(Applicants, finalized.Allocated + finalized.Reserve + finalized.NotSuccessful);

        var total = seating.Elapsed + results.Elapsed + allocation.Elapsed;
        WriteTimings(new
        {
            scenario = "hcm031-competition-scale",
            measuredAtUtc = DateTime.UtcNow,
            database = "PostgreSQL 16 (Testcontainers, postgres:16-alpine)",
            applicants = Applicants,
            lines = Lines,
            sessions = Sessions,
            vacancies = Lines * VacanciesPerLine,
            seatingMs = (long)seating.Elapsed.TotalMilliseconds,
            resultsMs = (long)results.Elapsed.TotalMilliseconds,
            allocationMs = (long)allocation.Elapsed.TotalMilliseconds,
            totalMs = (long)total.TotalMilliseconds,
            budgetMs = (long)Budget.TotalMilliseconds,
            allocated = finalized.Allocated,
            reserve = finalized.Reserve,
            notSuccessful = finalized.NotSuccessful,
            passed = total < Budget
        });
        Assert.True(total < Budget, $"seating {seating.Elapsed}, results {results.Elapsed}, allocation {allocation.Elapsed}");
    }

    /// <summary>Announced-and-closed competition (Examining is reached by seating): 4 lines × 25 published requisitions,
    /// 20 planned sessions of 500 seats, 10,000 Shortlisted applicants with 2 preferences each, exam-only criteria.</summary>
    private async Task<Guid> SeedAsync(Guid tenant)
    {
        var now = DateTime.UtcNow;
        var competition = new RecruitmentCompetition
        {
            TenantId = tenant,
            Number = $"CMP-PERF-{Guid.NewGuid():N}"[..17],
            Title = "مسابقة اختبار الحجم",
            LegalSourceId = Syr001LegislationIds.Law50_2004,
            Status = RecruitmentCompetitionStatus.IntakeClosed,
            ApplicationClosingDate = DateOnly.FromDateTime(now).AddDays(-5),
            AnnouncedAt = now.AddDays(-40),
            ApprovedAt = now.AddDays(-41),
            IntakeClosedAt = now.AddDays(-5),
            ObjectionWindowDays = 7,
            ReserveValidityMonths = 6,
            MaxLinesPerCandidate = 2,
            PassMark = 50m,
            ReconcileThreshold = 10m,
            ReconcileRule = CompetitionReconcileRule.ReconcilerDecides,
            TieBreakOrder = "ExamScore,AppliedAt"
        };
        var lineIds = new List<Guid>();
        await using (var db = fixture.CreateContext(tenant))
        {
            await RecruitmentLegalSourceTestSupport.SeedLegislationAndRegimesAsync(db);
            db.RecruitmentCompetitions.Add(competition);
            var lines = Enumerable.Range(1, Lines).Select(n => new CompetitionLine
            {
                TenantId = tenant,
                CompetitionId = competition.Id,
                LineNo = n,
                Title = $"السطر {n}",
                GradeCode = "G5",
                RequiredCount = VacanciesPerLine
            }).ToList();
            db.CompetitionLines.AddRange(lines);
            lineIds.AddRange(lines.Select(l => l.Id));
            await db.SaveChangesAsync();

            RecruitmentRequisition? first = null;
            foreach (var line in lines)
                for (var v = 0; v < VacanciesPerLine; v++)
                {
                    var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenant);
                    var requisition = Hcm026PostgresFixture.NewRequisition(tenant, position.Id, RequisitionStatus.Published);
                    requisition.CompetitionLineId = line.Id;
                    requisition.LegalSourceId = Syr001LegislationIds.Law50_2004;
                    db.RecruitmentRequisitions.Add(requisition);
                    first ??= requisition;
                }
            var criteria = new RecruitmentAssessmentCriteria
            {
                TenantId = tenant,
                RequisitionId = first!.Id,
                Title = "معايير المسابقة",
                CriteriaJson = AssessmentCriteriaSerializer.Serialize([new AssessmentCriteriaItem("EXAM", "الامتحان الكتابي", 100m, 100m, AssessmentStage.Exam)]),
                IsActive = true
            };
            db.RecruitmentAssessmentCriteria.Add(criteria);
            foreach (var line in lines)
                line.CriteriaVersionId = criteria.Id;
            db.ExamSessions.AddRange(Enumerable.Range(1, Sessions).Select(n => new ExamSession
            {
                TenantId = tenant,
                CompetitionId = competition.Id,
                Type = ExamSessionType.Written,
                Title = $"القاعة {n}",
                ScheduledAt = now.AddDays(3).AddHours(n),
                DurationMinutes = 120,
                Venue = $"المبنى أ — القاعة {n}",
                Capacity = Applicants / Sessions,
                MaxScore = 100m
            }));
            await db.SaveChangesAsync();
        }

        // Applicants in chunks (own context each, no change tracking growth).
        foreach (var chunk in Enumerable.Range(0, Applicants).Chunk(2_000))
        {
            await using var db = fixture.CreateContext(tenant);
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            foreach (var i in chunk)
            {
                var nationalId = $"P31{i:D8}";
                var app = new RecruitmentApplication
                {
                    TenantId = tenant,
                    ApplicantFullName = $"متقدم {i + 1}",
                    NationalId = nationalId,
                    NationalIdHash = PiiMask.Sha256Hex(nationalId + tenant.ToString("N")),
                    CompetitionId = competition.Id,
                    AppliedPosition = competition.Title,
                    AppliedAt = now.AddDays(-30).AddSeconds(i),
                    Status = RecruitmentApplicationStatus.Shortlisted,
                    ConflictOfInterestDeclared = true,
                    IntakeChannel = RecruitmentIntakeChannel.Public
                };
                db.RecruitmentApplications.Add(app);
                for (var p = 0; p < 2; p++)
                    db.CompetitionPreferences.Add(new CompetitionPreference
                    {
                        TenantId = tenant,
                        RecruitmentApplicationId = app.Id,
                        CompetitionLineId = lineIds[(i + p) % Lines],
                        Order = p + 1
                    });
            }
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync();
        }
        return competition.Id;
    }

    /// <summary>Test data: attendance and a deterministic final mark per paper (every 50th absent), marking closed.</summary>
    private async Task MarkAsync(Guid tenant, Guid competitionId)
    {
        await using var db = fixture.CreateContext(tenant);
        var papers = await db.ExamCandidates.Where(c => c.CompetitionId == competitionId).OrderBy(c => c.ExamNumber).ToListAsync();
        for (var i = 0; i < papers.Count; i++)
        {
            var absent = i % 50 == 49;
            papers[i].Attendance = absent ? ExamAttendance.Absent : ExamAttendance.Present;
            papers[i].FinalMark = absent ? null : (i * 37 % 1001) / 10m;
        }
        await db.SaveChangesAsync();
        await db.ExamSessions.Where(s => s.CompetitionId == competitionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.Status, ExamSessionStatus.MarkingClosed)
            .SetProperty(s => s.MarkingClosedAt, DateTime.UtcNow));
    }

    /// <summary>Test data: the computed version published directly, objection window already closed.</summary>
    private async Task PublishAsync(Guid tenant, Guid competitionId, Guid versionId)
    {
        await using var db = fixture.CreateContext(tenant);
        var version = await db.CompetitionResultVersions.SingleAsync(v => v.Id == versionId);
        version.Status = CompetitionResultVersionStatus.Published;
        version.PublishedAt = DateTime.UtcNow.AddDays(-10);
        var competition = await db.RecruitmentCompetitions.SingleAsync(c => c.Id == competitionId);
        competition.Status = RecruitmentCompetitionStatus.ResultsPublished;
        competition.CurrentResultVersionId = versionId;
        competition.ResultsFirstPublishedAt = DateTime.UtcNow.AddDays(-10);
        await db.SaveChangesAsync();
    }

    private static CompetitionAllocationService CreateAllocation(HRCoreDbContext db)
    {
        var appeals = new RecruitmentAppealService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            Mock.Of<IWorkflowStarter>(),
            new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
            Mock.Of<IWorkflowInstanceActionService>(),
            new SelectionCommitteeService(db),
            new RecruitmentAppealWorkflowHandler(db));
        return new CompetitionAllocationService(db,
            new CompetitionObjectionService(db, appeals, new CompetitionResultsService(db, Mock.Of<IWorkflowStarter>())));
    }

    private static void WriteTimings(object timings)
    {
        var dir = Environment.GetEnvironmentVariable("HCM031_PERF_OUT");
        if (string.IsNullOrWhiteSpace(dir))
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !System.IO.File.Exists(Path.Combine(root.FullName, "HRManagement.slnx")))
                root = root.Parent;
            dir = Path.Combine(root?.FullName ?? AppContext.BaseDirectory, "artifacts", "hcm031-perf");
        }
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(timings, new JsonSerializerOptions { WriteIndented = true });
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        System.IO.File.WriteAllText(Path.Combine(dir, $"hcm031-competition-scale-{stamp}.json"), json);
        System.IO.File.WriteAllText(Path.Combine(dir, "latest.json"), json);
    }
}
