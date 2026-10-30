using System.Text.Json;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Consumers;
using Dev.Analytics.Infrastructure.Data;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.MessageContracts.Events;
using HRManagement.TestSupport;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static HRManagement.UnitTests.Hcm032MetricsFixture;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-032 (PLAN-32, P6): <see cref="IRecruitmentRequisitionClosed"/> — aggregate-only payload computed on the outbox save,
/// every close path stages it, Dev.Analytics stores one fact per requisition (idempotent) and suppresses small cells.
/// </summary>
public class Hcm032AnalyticsPublicationTests
{
    private static async Task<HRCoreDbContext> SeededAsync()
    {
        var db = Hcm027TestSupport.CreateContext();
        await Hcm032MetricsFixture.SeedAsync(db);
        db.ChangeTracker.Clear();
        return db;
    }

    // R3 (closed 2026-10-01): B1–B10 → Public B1 B2 B5 B7 B9 B10 = 6, Internal B3 B8 = 2, HrEntered B4 = 1, Legacy B6 = 1;
    // hired B1 (event 2026-10-01) → time to fill 211; offers O3 accepted, O7 declined, O4 expired, O5 revoked.
    [Fact]
    public async Task Closed_requisition_aggregates_match_the_fixture()
    {
        await using var db = await SeededAsync();
        var r3 = await db.RecruitmentRequisitions.Include(r => r.FundedPosition).SingleAsync(r => r.Id == R3);

        var m = await RecruitmentRequisitionClosedEvent.BuildAsync(db, r3, Now, CancellationToken.None);

        Assert.Equal((R3, UnitBranch, "Closed", Utc(2026, 10, 1)), (m.RequisitionId, m.OrgUnitId, m.Outcome, m.ClosedAt));
        Assert.Equal((10, 1, 211), (m.Applicants, m.HiredCount, m.TimeToFillDays!.Value));
        Assert.Equal((6, 2, 1, 1), (m.ApplicantsPublic, m.ApplicantsInternal, m.ApplicantsHrEntered, m.ApplicantsLegacy));
        Assert.Equal((1, 1, 1, 1), (m.OffersAccepted, m.OffersDeclined, m.OffersExpired, m.OffersRevoked));
        Assert.Equal("HR.Core", m.SourceSystem);
        Assert.Equal(1, ((IRecruitmentRequisitionClosed)m).SchemaVersion);
    }

    // R4 (approved 2026-07-05): D1 hired in this unsaved change → counts from now (2026-10-29) = 116 days; D1, D6 public.
    [Fact]
    public async Task Unsaved_hire_of_the_same_save_is_counted_from_now()
    {
        await using var db = await SeededAsync();
        var r4 = await db.RecruitmentRequisitions.SingleAsync(r => r.Id == R4);
        var d1 = await db.RecruitmentApplications.SingleAsync(a => a.ApplicantFullName == CandidateNamePrefix + "D1");
        d1.Status = RecruitmentApplicationStatus.Hired;

        var m = await RecruitmentRequisitionClosedEvent.BuildAsync(db, r4, Now, CancellationToken.None);

        Assert.Equal((2, 1, 116, 2), (m.Applicants, m.HiredCount, m.TimeToFillDays!.Value, m.ApplicantsPublic));
        Assert.Equal(UnitBranch, m.OrgUnitId);
    }

    [Fact]
    public async Task Cancelling_a_requisition_publishes_the_event_in_the_same_save()
    {
        await using var db = await SeededAsync();
        var publish = new Mock<IPublishEndpoint>();
        IRecruitmentRequisitionClosed? published = null;
        publish.Setup(p => p.Publish(It.IsAny<IRecruitmentRequisitionClosed>(), It.IsAny<CancellationToken>()))
            .Callback<IRecruitmentRequisitionClosed, CancellationToken>((m, _) => published = m)
            .Returns(Task.CompletedTask);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" }).Build();
        var lifecycle = new RecruitmentRequisitionLifecycleService(
            db, Mock.Of<IPolicyOrchestrator>(), new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance), config,
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()), publish.Object);

        var (success, error) = await lifecycle.CancelAsync(R5, null, Guid.NewGuid(), actorCanApprove: false);

        Assert.True(success, error);
        Assert.NotNull(published);
        Assert.Equal((R5, UnitHq, "Cancelled", 0, 0), (published!.RequisitionId, published.OrgUnitId, published.Outcome, published.Applicants, published.HiredCount));
        Assert.Null(published.TimeToFillDays);
    }

    [Fact]
    public void Every_close_path_stages_the_event_before_its_save()
    {
        var root = Path.Combine(RepoPaths.Root, "src", "Services", "HRCore", "HR.Core.Infrastructure", "Services");
        var paths = new (string File, int Expected)[]
        {
            (Path.Combine(root, "Recruitment", "RecruitmentHireClosureService.cs"), 1),
            (Path.Combine(root, "Recruitment", "RecruitmentRequisitionLifecycleService.cs"), 2),
            (Path.Combine(root, "Establishment", "EstablishmentChangeApplier.cs"), 1)
        };
        foreach (var (file, expected) in paths)
            Assert.Equal(expected, System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(file), "RecruitmentRequisitionClosedEvent\\.StageAsync").Count);
    }

    [Fact]
    public void Contract_is_aggregate_only_and_versioned()
    {
        var properties = typeof(IRecruitmentRequisitionClosed).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(
            ["TenantId", "RequisitionId", "OrgUnitId", "Outcome", "ClosedAt", "Applicants", "HiredCount", "TimeToFillDays",
             "ApplicantsPublic", "ApplicantsInternal", "ApplicantsHrEntered", "ApplicantsLegacy", "OffersAccepted", "OffersDeclined",
             "OffersExpired", "OffersRevoked", "CorrelationId", "SourceSystem", "SchemaVersion"],
            properties);
        Assert.DoesNotContain(properties, p => p.Contains("Name") || p.Contains("National") || p.Contains("ApplicationId") || p.Contains("EmployeeId"));

        using var inventory = JsonDocument.Parse(System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "rel-compat", "event-schema-inventory.json")));
        var entry = inventory.RootElement.GetProperty("contracts").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == nameof(IRecruitmentRequisitionClosed));
        Assert.Equal(1, entry.GetProperty("schemaVersion").GetInt32());
        Assert.True(entry.GetProperty("outbox").GetBoolean());
        Assert.Contains(nameof(IRecruitmentRequisitionClosed), System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "MESSAGE_EVENT_MATRIX.md")));
    }

    // ------------------------------------------------------------------ Dev.Analytics

    private static AnalyticsDbContext AnalyticsDb() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Task ConsumeAsync(AnalyticsDbContext db, IRecruitmentRequisitionClosed message)
    {
        var context = new Mock<ConsumeContext<IRecruitmentRequisitionClosed>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MessageId).Returns(NewId.NextGuid());
        return new RecruitmentRequisitionClosedConsumer(db, NullLogger<RecruitmentRequisitionClosedConsumer>.Instance).Consume(context.Object);
    }

    private static RecruitmentRequisitionClosedMessage Message(Guid tenant, Guid requisition, int hired = 1, int? days = 30) => new()
    {
        TenantId = tenant,
        RequisitionId = requisition,
        OrgUnitId = UnitHq,
        Outcome = "Closed",
        ClosedAt = Utc(2026, 10, 1),
        Applicants = 12,
        HiredCount = hired,
        TimeToFillDays = days,
        ApplicantsPublic = 7,
        ApplicantsInternal = 3,
        ApplicantsHrEntered = 2,
        OffersAccepted = 1,
        OffersDeclined = 2,
        SourceSystem = "HR.Core"
    };

    [Fact]
    public async Task Consumer_is_idempotent_per_requisition()
    {
        await using var db = AnalyticsDb();
        var tenant = Guid.NewGuid();

        await ConsumeAsync(db, Message(tenant, R1));
        await ConsumeAsync(db, Message(tenant, R1));
        await ConsumeAsync(db, Message(tenant, R2, hired: 0, days: null));

        var facts = await db.RecruitmentRequisitionFacts.OrderBy(f => f.ClosedAt).ThenBy(f => f.RequisitionId).ToListAsync();
        Assert.Equal(2, facts.Count);
        var r1 = facts.Single(f => f.RequisitionId == R1);
        Assert.Equal((tenant, 12, 1, 30, 7, "Closed"), (r1.TenantId, r1.Applicants, r1.HiredCount, r1.TimeToFillDays!.Value, r1.ApplicantsPublic, r1.Outcome));
        Assert.Empty(await db.AnalyticsEvents.ToListAsync());
    }

    [Fact]
    public void Card_suppresses_small_cells_and_their_median()
    {
        RecruitmentRequisitionFact Fact(int hired, int? days, int pub, int internalCount, int accepted, string outcome = "Closed") => new()
        {
            RequisitionId = Guid.NewGuid(),
            Outcome = outcome,
            HiredCount = hired,
            TimeToFillDays = days,
            ApplicantsPublic = pub,
            ApplicantsInternal = internalCount,
            OffersAccepted = accepted
        };

        var small = RecruitmentFactRules.BuildCard([Fact(1, 20, 3, 0, 1), Fact(1, 40, 4, 0, 1), Fact(0, null, 2, 6, 0, "Cancelled")]);
        Assert.Equal((2, 1, "<5", "<5"), (small.RequisitionsClosed, small.RequisitionsCancelled, small.Filled, small.MedianTimeToFillDays));
        Assert.Equal(["9", "6", "0", "0"], small.Channels.Select(c => c.Value));
        Assert.Equal(["<5", "0", "0", "0"], small.OfferOutcomes.Select(c => c.Value));

        var large = RecruitmentFactRules.BuildCard(Enumerable.Range(1, 6).Select(i => Fact(1, i * 10, 1, 0, 1)).ToList());
        // Filled 6; days 10..60 → median (30 + 40) / 2 = 35.
        Assert.Equal(("6", "35", "6"), (large.Filled, large.MedianTimeToFillDays, large.OfferOutcomes[0].Value));
    }

    [Fact]
    public void Dashboard_card_is_rendered_with_accessible_tables()
    {
        var view = System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Services", "DevAnalytics", "Dev.Analytics.Web", "Views", "Dashboards", "Index.cshtml"));
        Assert.Contains("id=\"recruitment-card\"", view);
        Assert.Contains("التوظيف", view);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(view, "<caption>[^<]*محجوبة").Count);
    }
}
