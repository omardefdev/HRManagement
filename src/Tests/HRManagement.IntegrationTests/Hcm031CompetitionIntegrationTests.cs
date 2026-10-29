using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-031 (PLAN-31, P7) on a migrated PostgreSQL: the competition unique constraints, a double-submitted first mark of
/// one paper (one row), two concurrent finalizations (<c>FOR UPDATE</c> on the competition: one allocation set, the other
/// call sees <c>AlreadyFinalized</c>), and an allocated candidate through offer, acceptance and hire to
/// <see cref="ICandidateAccepted"/> v4 on the allocated requisition. Rows are seeded directly (test data) in a fresh tenant
/// per test; the services under test run unchanged.
/// </summary>
[Trait("Category", "Testcontainers")]
public class Hcm031CompetitionIntegrationTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    private static readonly Guid Actor = Guid.Parse("31310000-0000-4000-8000-0000000000a1");

    [SkippableFact]
    public async Task Unique_constraints_hold_one_application_per_person_one_exam_number_one_published_version()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedPublishedAsync(tenant, [80m, 60m], [1]);

        // One application per person per competition (decision 3).
        await using (var db = fixture.CreateContext(tenant))
        {
            var first = await db.RecruitmentApplications.AsNoTracking().FirstAsync(a => a.Id == seed.ApplicationIds[0]);
            db.RecruitmentApplications.Add(NewApplication(tenant, seed.CompetitionId, first.NationalId!, 99));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(HRManagement.ServiceDefaults.ConcurrencyExtensions.IsUniqueViolation(ex));
        }

        // One exam number per competition and exam type.
        await using (var db = fixture.CreateContext(tenant))
        {
            var paper = await db.ExamCandidates.AsNoTracking().FirstAsync(c => c.CompetitionId == seed.CompetitionId);
            var other = NewApplication(tenant, seed.CompetitionId, $"31{Guid.NewGuid():N}"[..11], 98);
            db.RecruitmentApplications.Add(other);
            db.ExamCandidates.Add(new ExamCandidate
            {
                TenantId = tenant,
                ExamSessionId = paper.ExamSessionId,
                CompetitionId = seed.CompetitionId,
                ExamType = ExamSessionType.Written,
                RecruitmentApplicationId = other.Id,
                ExamNumber = paper.ExamNumber,
                SeatNumber = 500
            });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(HRManagement.ServiceDefaults.ConcurrencyExtensions.IsUniqueViolation(ex));
        }

        // At most one published results version per competition (filtered unique index).
        await using (var db = fixture.CreateContext(tenant))
        {
            db.CompetitionResultVersions.Add(new CompetitionResultVersion
            {
                TenantId = tenant,
                CompetitionId = seed.CompetitionId,
                VersionNo = 2,
                Status = CompetitionResultVersionStatus.Published,
                ComputedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow
            });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(HRManagement.ServiceDefaults.ConcurrencyExtensions.IsUniqueViolation(ex));
        }
    }

    [SkippableFact]
    public async Task A_first_mark_submitted_twice_at_the_same_moment_stores_one_row()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedPublishedAsync(tenant, [80m], [1]);
        var marker = new ExamMarkerActor(Guid.NewGuid(), Guid.NewGuid(), false);
        Guid paperId;
        await using (var db = fixture.CreateContext(tenant))
        {
            // A second session, held and open for marking, with this marker as first marker.
            var session = new ExamSession
            {
                TenantId = tenant,
                CompetitionId = seed.CompetitionId,
                Type = ExamSessionType.Practical,
                Title = "الاختبار العملي",
                ScheduledAt = DateTime.UtcNow.AddDays(-2),
                DurationMinutes = 60,
                Venue = "المخبر",
                Capacity = 10,
                MaxScore = 100m,
                Status = ExamSessionStatus.Held
            };
            db.ExamSessions.Add(session);
            db.Employees.Add(new Employee
            {
                Id = marker.EmployeeId!.Value,
                TenantId = tenant,
                FullName = "مصحح أول",
                NationalId = $"9{Guid.NewGuid():N}"[..11],
                CurrentStatus = EmploymentStatus.Active,
                BirthDate = new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            db.ExamMarkerAssignments.Add(new ExamMarkerAssignment
            {
                TenantId = tenant,
                ExamSessionId = session.Id,
                MarkerEmployeeId = marker.EmployeeId,
                MarkerUserId = marker.UserId,
                Role = ExamMarkerRole.First
            });
            var paper = new ExamCandidate
            {
                TenantId = tenant,
                ExamSessionId = session.Id,
                CompetitionId = seed.CompetitionId,
                ExamType = ExamSessionType.Practical,
                RecruitmentApplicationId = seed.ApplicationIds[0],
                ExamNumber = "77777777",
                SeatNumber = 1,
                Attendance = ExamAttendance.Present
            };
            db.ExamCandidates.Add(paper);
            await db.SaveChangesAsync();
            paperId = paper.Id;
        }

        using var barrier = new Barrier(2);
        await using var dbA = CreateContext(tenant, new FirstSaveBarrier(barrier));
        await using var dbB = CreateContext(tenant, new FirstSaveBarrier(barrier));
        var results = await Task.WhenAll(
            Task.Run(() => new ExamMarkingService(dbA).SubmitAsync(paperId, 70m, null, marker)),
            Task.Run(() => new ExamMarkingService(dbB).SubmitAsync(paperId, 72m, null, marker)));

        Assert.Contains(results, r => r.Success);
        await using var check = fixture.CreateContext(tenant);
        var rows = await check.ExamScores.AsNoTracking().Where(s => s.ExamCandidateId == paperId).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(ExamScoreRound.First, row.Round);
        Assert.Equal(marker.UserId, row.MarkerUserId);
    }

    [SkippableFact]
    public async Task Two_concurrent_finalizations_allocate_once()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedPublishedAsync(tenant, [95m, 90m, 85m, 80m, 75m, 70m, 40m], [2, 2]);

        await using var dbA = fixture.CreateContext(tenant);
        await using var dbB = fixture.CreateContext(tenant);
        var results = await Task.WhenAll(
            Task.Run(() => CreateAllocation(dbA).FinalizeAsync(seed.CompetitionId, Actor)),
            Task.Run(() => CreateAllocation(dbB).FinalizeAsync(seed.CompetitionId, Actor)));

        Assert.All(results, r => Assert.True(r.Success, r.Error));
        var done = Assert.Single(results, r => !r.AlreadyFinalized);
        Assert.Single(results, r => r.AlreadyFinalized);
        Assert.Equal(4, done.Allocated);
        Assert.Equal(2, done.Reserve);
        Assert.Equal(1, done.NotSuccessful);

        await using var check = fixture.CreateContext(tenant);
        var allocations = await check.CompetitionAllocations.AsNoTracking().Where(a => a.CompetitionId == seed.CompetitionId).ToListAsync();
        Assert.Equal(4, allocations.Count);
        Assert.Equal(4, allocations.Select(a => a.AllocatedRequisitionId).Distinct().Count());
        Assert.Equal(4, allocations.Select(a => a.RecruitmentApplicationId).Distinct().Count());
        var competition = await check.RecruitmentCompetitions.AsNoTracking().SingleAsync(c => c.Id == seed.CompetitionId);
        Assert.Equal(RecruitmentCompetitionStatus.Finalized, competition.Status);
        Assert.Equal(4, await check.RecruitmentApplicationEvents.AsNoTracking()
            .CountAsync(e => e.EventType == RecruitmentApplicationEventTypes.CompetitionAllocated));
    }

    [SkippableFact]
    public async Task An_allocated_candidate_goes_through_offer_acceptance_and_hire_on_the_allocated_requisition()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedPublishedAsync(tenant, [90m], [1]);
        await using var db = CreateContext(tenant);
        Assert.True((await CreateAllocation(db).FinalizeAsync(seed.CompetitionId, Actor)).Success);
        var allocation = await db.CompetitionAllocations.AsNoTracking().SingleAsync(a => a.CompetitionId == seed.CompetitionId);
        var requisition = await db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == allocation.AllocatedRequisitionId);

        var config = new ConfigurationBuilder().Build();
        var offers = await CreateOfferServiceAsync(db, config);
        var (draft, draftError) = await offers.CreateDraftAsync(seed.ApplicationIds[0], 600_000m, null, DateTime.UtcNow.Date.AddDays(20), null, Actor);
        Assert.True(draft is not null, draftError);
        Assert.Equal(requisition.FundedPositionId, draft!.FundedPositionId);
        var offer = await db.OfferLetters.SingleAsync(o => o.Id == draft.Id);
        offer.Status = OfferStatus.Issued; // the per-offer approval itself is covered by the PLAN-27 tests
        offer.IssuedDate = DateTime.UtcNow;
        offer.ExpiryDate = DateTime.UtcNow.AddDays(14);
        await db.SaveChangesAsync();
        var (accepted, acceptError) = await offers.AcceptAsync(offer.Id, OfferAcceptedBy.Candidate, null, null, null);
        Assert.True(accepted, acceptError);

        var winner = await db.RecruitmentApplications.SingleAsync(a => a.Id == seed.ApplicationIds[0]);
        Assert.Equal(requisition.Id, winner.RequisitionId);
        var publish = new Mock<IPublishEndpoint>();
        var closure = await RecruitmentLegalSourceTestSupport.CreateHireClosureServiceAsync(db, publish.Object, config);
        var employee = await closure.HireAndCreateEmployeeAsync(
            winner, winner.NationalId!, winner.ApplicantFullName, null, "خارجي", DateTime.UtcNow.AddYears(-30), Gender.Male, null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "PERMANENT");

        var values = publish.Invocations
            .Where(i => i.Method.Name == nameof(IPublishEndpoint.Publish) && i.Method.IsGenericMethod
                && i.Method.GetGenericArguments()[0] == typeof(ICandidateAccepted))
            .Select(i => i.Arguments[0])
            .Single();
        Assert.Equal(employee.Id, Prop(values, nameof(ICandidateAccepted.EmployeeId)));
        Assert.Equal(winner.Id, Prop(values, nameof(ICandidateAccepted.RecruitmentApplicationId)));
        Assert.Equal(offer.Id, Prop(values, nameof(ICandidateAccepted.OfferLetterId)));
        Assert.Equal(requisition.FundedPositionId, Prop(values, nameof(ICandidateAccepted.FundedPositionId)));
        await using var check = fixture.CreateContext(tenant);
        Assert.Equal(RecruitmentApplicationStatus.Hired, (await check.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == winner.Id)).Status);
    }

    [SkippableFact]
    public async Task Two_concurrent_recalls_on_one_line_take_two_distinct_reserves()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedPublishedAsync(tenant, [95m, 90m, 85m, 80m, 75m], [2]);
        List<(Guid AppId, Guid RequisitionId)> vacated;
        await using (var db = fixture.CreateContext(tenant))
        {
            Assert.True((await CreateAllocation(db).FinalizeAsync(seed.CompetitionId, Actor)).Success);
            // Both allocated candidates leave at the same moment (test data: status set directly).
            var allocated = await db.RecruitmentApplications.Where(a => a.CompetitionId == seed.CompetitionId && a.Status == RecruitmentApplicationStatus.Ranked).ToListAsync();
            Assert.Equal(2, allocated.Count);
            vacated = allocated.Select(a => (a.Id, a.RequisitionId!.Value)).ToList();
            foreach (var app in allocated)
                app.Status = RecruitmentApplicationStatus.Withdrawn;
            await db.SaveChangesAsync();
        }

        await using var dbA = fixture.CreateContext(tenant);
        await using var dbB = fixture.CreateContext(tenant);
        var results = await Task.WhenAll(
            Task.Run(() => new CompetitionReserveService(dbA).RecallForVacancyAsync(vacated[0].RequisitionId, Actor)),
            Task.Run(() => new CompetitionReserveService(dbB).RecallForVacancyAsync(vacated[1].RequisitionId, Actor)));

        Assert.All(results, r => Assert.True(r.Recalled, r.Notice));
        Assert.NotEqual(results[0].RecalledApplicationId, results[1].RecalledApplicationId);
        await using var check = fixture.CreateContext(tenant);
        for (var i = 0; i < 2; i++)
            Assert.Equal(vacated[i].RequisitionId,
                (await check.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == results[i].RecalledApplicationId)).RequisitionId);
        var fromReserve = await check.CompetitionAllocations.AsNoTracking().Where(a => a.CompetitionId == seed.CompetitionId && a.FromReserve).ToListAsync();
        Assert.Equal(2, fromReserve.Select(a => a.RecruitmentApplicationId).Distinct().Count());
    }

    // ------------------------------------------------------------------ helpers

    private sealed record Seeded(Guid CompetitionId, IReadOnlyList<Guid> LineIds, IReadOnlyList<Guid> ApplicationIds);

    /// <summary>
    /// A competition at <c>ResultsPublished</c> whose objection window closed 3 days ago: lines of
    /// <paramref name="vacancies"/> published requisitions each, one marked written session, and one published version
    /// ranking the applicants by <paramref name="scores"/> (pass mark 50). Every applicant prefers every line, rotated.
    /// </summary>
    private async Task<Seeded> SeedPublishedAsync(Guid tenant, decimal[] scores, int[] vacancies)
    {
        await using var db = fixture.CreateContext(tenant);
        await RecruitmentLegalSourceTestSupport.SeedLegislationAndRegimesAsync(db);
        var now = DateTime.UtcNow;
        var competition = new RecruitmentCompetition
        {
            TenantId = tenant,
            Number = $"CMP-IT-{Guid.NewGuid():N}"[..15],
            Title = "مسابقة اختبار التكامل",
            LegalSourceId = Syr001LegislationIds.Law50_2004,
            Status = RecruitmentCompetitionStatus.ResultsPublished,
            ApplicationClosingDate = DateOnly.FromDateTime(now).AddDays(-40),
            AnnouncedAt = now.AddDays(-60),
            ApprovedAt = now.AddDays(-61),
            IntakeClosedAt = now.AddDays(-40),
            ObjectionWindowDays = 7,
            ReserveValidityMonths = 6,
            MaxLinesPerCandidate = 2,
            PassMark = 50m,
            ReconcileThreshold = 10m,
            ReconcileRule = CompetitionReconcileRule.ReconcilerDecides,
            TieBreakOrder = "ExamScore,AppliedAt",
            ResultsFirstPublishedAt = now.AddDays(-10)
        };
        db.RecruitmentCompetitions.Add(competition);
        var lines = new List<CompetitionLine>();
        for (var i = 0; i < vacancies.Length; i++)
        {
            var line = new CompetitionLine
            {
                TenantId = tenant,
                CompetitionId = competition.Id,
                LineNo = i + 1,
                Title = $"محاسب — السطر {i + 1}",
                GradeCode = "G5",
                RequiredCount = vacancies[i]
            };
            lines.Add(line);
            db.CompetitionLines.Add(line);
        }
        await db.SaveChangesAsync();
        foreach (var line in lines)
            for (var v = 0; v < line.RequiredCount; v++)
            {
                var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenant);
                var requisition = Hcm026PostgresFixture.NewRequisition(tenant, position.Id, RequisitionStatus.Published);
                requisition.CompetitionLineId = line.Id;
                requisition.LegalSourceId = Syr001LegislationIds.Law50_2004;
                requisition.ApprovedDate = now.AddDays(-70);
                db.RecruitmentRequisitions.Add(requisition);
            }

        var session = new ExamSession
        {
            TenantId = tenant,
            CompetitionId = competition.Id,
            Type = ExamSessionType.Written,
            Title = "القاعة 1",
            ScheduledAt = now.AddDays(-20),
            DurationMinutes = 120,
            Venue = "المبنى أ — القاعة 1",
            Capacity = scores.Length + 10,
            MaxScore = 100m,
            Status = ExamSessionStatus.MarkingClosed,
            MarkingClosedAt = now.AddDays(-15)
        };
        db.ExamSessions.Add(session);
        var version = new CompetitionResultVersion
        {
            TenantId = tenant,
            CompetitionId = competition.Id,
            VersionNo = 1,
            Status = CompetitionResultVersionStatus.Published,
            ComputedAt = now.AddDays(-12),
            PublishedAt = now.AddDays(-10)
        };
        db.CompetitionResultVersions.Add(version);
        competition.CurrentResultVersionId = version.Id;

        var ranked = scores.Select((s, i) => (Score: s, Index: i)).OrderByDescending(x => x.Score).Select((x, r) => (x.Index, Rank: r + 1))
            .ToDictionary(x => x.Index, x => x.Rank);
        var applicationIds = new List<Guid>();
        for (var i = 0; i < scores.Length; i++)
        {
            var app = NewApplication(tenant, competition.Id, $"31{Guid.NewGuid():N}"[..11], i);
            db.RecruitmentApplications.Add(app);
            applicationIds.Add(app.Id);
            for (var p = 0; p < lines.Count; p++)
                db.CompetitionPreferences.Add(new CompetitionPreference
                {
                    TenantId = tenant,
                    RecruitmentApplicationId = app.Id,
                    CompetitionLineId = lines[(i + p) % lines.Count].Id,
                    Order = p + 1
                });
            var examNumber = (10_000_000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            db.ExamCandidates.Add(new ExamCandidate
            {
                TenantId = tenant,
                ExamSessionId = session.Id,
                CompetitionId = competition.Id,
                ExamType = ExamSessionType.Written,
                RecruitmentApplicationId = app.Id,
                ExamNumber = examNumber,
                SeatNumber = i + 1,
                Attendance = ExamAttendance.Present,
                FinalMark = scores[i]
            });
            db.CompetitionResults.Add(new CompetitionResult
            {
                TenantId = tenant,
                ResultVersionId = version.Id,
                RecruitmentApplicationId = app.Id,
                ExamNumber = examNumber,
                ExamScore = scores[i],
                FinalScore = scores[i],
                OverallRank = ranked[i],
                Outcome = scores[i] >= competition.PassMark ? CompetitionResultOutcome.Passed : CompetitionResultOutcome.Failed
            });
        }
        await db.SaveChangesAsync();
        return new Seeded(competition.Id, lines.Select(l => l.Id).ToList(), applicationIds);
    }

    private static RecruitmentApplication NewApplication(Guid tenant, Guid competitionId, string nationalId, int i) => new()
    {
        TenantId = tenant,
        ApplicantFullName = $"متقدم {i + 1}",
        NationalId = nationalId,
        NationalIdHash = PiiMask.Sha256Hex(nationalId),
        CompetitionId = competitionId,
        AppliedPosition = "محاسب",
        AppliedAt = DateTime.UtcNow.AddDays(-50).AddMinutes(i),
        Status = RecruitmentApplicationStatus.Shortlisted,
        ConflictOfInterestDeclared = true,
        IntakeChannel = RecruitmentIntakeChannel.Public
    };

    /// <summary>Finalize reads only the pending-objection count from the objection service.</summary>
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

    private static async Task<RecruitmentOfferService> CreateOfferServiceAsync(HRCoreDbContext db, IConfiguration config)
    {
        var validator = await RecruitmentLegalSourceTestSupport.CreateValidatorAsync(db);
        var codes = new Mock<ICodeGenerator>();
        codes.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CodeGenerationResult { Code = $"OFF-IT-{Guid.NewGuid():N}"[..14] });
        var grades = new Mock<IPayrollGradeLookup>();
        grades.Setup(g => g.GetAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayrollGradeLookupResult.Found(500_000m, 900_000m, 600_000m, "درجة"));
        return new RecruitmentOfferService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            codes.Object,
            validator,
            Mock.Of<IWorkflowStarter>(),
            config,
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
            grades.Object,
            new CompetitionReserveService(db));
    }

    private static object? Prop(object values, string name) => values.GetType().GetProperty(name)?.GetValue(values);

    /// <summary>A context as the host builds it (tenant stamping), plus <paramref name="interceptors"/>.</summary>
    private HRCoreDbContext CreateContext(Guid tenant, params IInterceptor[] interceptors)
    {
        var plain = fixture.CreateContext(tenant);
        var connection = plain.Database.GetConnectionString();
        plain.Dispose();
        var tenantContext = new FixedTenant(tenant);
        return new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
                .AddInterceptors([new HRManagement.ServiceDefaults.TenantIdStampInterceptor(tenantContext), .. interceptors])
                .Options,
            tenantContext);
    }

    /// <summary>Holds the first save of each context until both have read the paper (both see no first mark yet).</summary>
    private sealed class FirstSaveBarrier(Barrier barrier) : SaveChangesInterceptor
    {
        private int _done;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FixedTenant(Guid tenantId) : HRManagement.SharedKernel.Tenancy.ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
