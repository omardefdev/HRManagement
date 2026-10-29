using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>
/// COM-030 (PLAN-30 F4, AC-HCM-30e negative): on a migrated PostgreSQL, two dispatchers that plan the same events at the
/// same moment (both read the unplanned events before either saves) leave exactly one message per event — the unique
/// index <c>(TenantId, IdempotencyKey)</c> refuses the second insert and the loser only marks the event planned — and two
/// concurrent reminder passes record one <c>OfferExpiryReminder</c> per offer (deterministic event id).
/// </summary>
[Trait("Category", "Testcontainers")]
public class Hcm030CandidateNotificationConcurrencyTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    [SkippableFact]
    public async Task Two_concurrent_dispatchers_plan_one_message_per_event()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedConsentingApplicationsAsync(tenant, count: 3);
        var now = DateTime.UtcNow;
        // The planner reads unplanned events of every tenant and the class shares one database: mark the events other
        // tests left behind as planned so only this test's events take part (independent of test order).
        await using (var db = fixture.CreateContext(tenant))
        {
            var ids = seed.ApplicationIds.ToList();
            await db.RecruitmentApplicationEvents
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                .Where(e => e.NotificationsPlannedAt == null && !ids.Contains(e.RecruitmentApplicationId))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NotificationsPlannedAt, now));
        }

        using var barrier = new Barrier(2);
        await using var dbA = CreateContext(tenant, new FirstSaveBarrier(barrier));
        await using var dbB = CreateContext(tenant, new FirstSaveBarrier(barrier));

        var results = await Task.WhenAll(
            Task.Run(() => Dispatcher(dbA).PlanAsync(now, default)),
            Task.Run(() => Dispatcher(dbB).PlanAsync(now, default)));

        await using var check = fixture.CreateContext(tenant);
        var rows = await check.CandidateNotifications.AsNoTracking()
            .Where(n => seed.ApplicationIds.Contains(n.RecruitmentApplicationId))
            .ToListAsync();
        Assert.Equal(seed.ApplicationIds.Count, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.IdempotencyKey).Distinct().Count());
        Assert.Equal(rows.Count, results.Sum());
        Assert.All(rows, r => Assert.Equal(tenant, r.TenantId));
        var events = await check.RecruitmentApplicationEvents.AsNoTracking()
            .Where(e => seed.ApplicationIds.Contains(e.RecruitmentApplicationId))
            .ToListAsync();
        Assert.All(events, e => Assert.NotNull(e.NotificationsPlannedAt));

        // A direct duplicate is refused by the index itself.
        var first = rows[0];
        check.CandidateNotifications.Add(new CandidateNotification
        {
            TenantId = tenant,
            RecruitmentApplicationId = first.RecruitmentApplicationId,
            EventId = first.EventId,
            EventType = first.EventType,
            Channel = first.Channel,
            TemplateCode = first.TemplateCode,
            RecipientMasked = first.RecipientMasked,
            Body = first.Body,
            IdempotencyKey = first.IdempotencyKey
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
        Assert.True(HRManagement.ServiceDefaults.ConcurrencyExtensions.IsUniqueViolation(ex));
    }

    [SkippableFact]
    public async Task Two_concurrent_reminder_passes_record_one_reminder_per_offer()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedConsentingApplicationsAsync(tenant, count: 1);
        var now = DateTime.UtcNow;
        Guid offerId;
        await using (var db = fixture.CreateContext(tenant))
        {
            var offer = new OfferLetter
            {
                TenantId = tenant,
                RecruitmentApplicationId = seed.ApplicationIds[0],
                FundedPositionId = seed.FundedPositionId,
                OfferNumber = "OFF-" + Guid.NewGuid().ToString("N")[..10],
                Status = OfferStatus.Issued,
                IssuedDate = now.AddDays(-12),
                ExpiryDate = now.AddHours(20),
                OfferedSalary = 1000m,
                ProposedStartDate = now.AddDays(30)
            };
            db.OfferLetters.Add(offer);
            await db.SaveChangesAsync();
            offerId = offer.Id;
        }

        using var barrier = new Barrier(2);
        await using var dbA = CreateContext(tenant, new FirstSaveBarrier(barrier));
        await using var dbB = CreateContext(tenant, new FirstSaveBarrier(barrier));

        var results = await Task.WhenAll(
            Task.Run(() => Dispatcher(dbA).RemindDueOffersAsync(now, default)),
            Task.Run(() => Dispatcher(dbB).RemindDueOffersAsync(now, default)));

        await using var check = fixture.CreateContext(tenant);
        var reminder = await check.RecruitmentApplicationEvents.AsNoTracking()
            .SingleAsync(e => e.EventType == RecruitmentApplicationEventTypes.OfferExpiryReminder && e.RelatedEntityId == offerId);
        Assert.Equal(CandidateNotificationDispatcher.ReminderEventIdFor(offerId), reminder.Id);
        Assert.Equal(1, results.Sum());
    }

    private sealed record Seed(IReadOnlyList<Guid> ApplicationIds, Guid FundedPositionId);

    private async Task<Seed> SeedConsentingApplicationsAsync(Guid tenant, int count)
    {
        await using var db = fixture.CreateContext(tenant);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenant);
        var requisition = Hcm026PostgresFixture.NewRequisition(tenant, position.Id, RequisitionStatus.Published);
        db.RecruitmentRequisitions.Add(requisition);
        var posting = new PublicJobPosting { TenantId = tenant, Title = "محلل", IsPublished = true, RequisitionId = requisition.Id };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();

        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var app = new RecruitmentApplication
            {
                TenantId = tenant,
                ApplicantFullName = "متقدّم " + i,
                AppliedPosition = "محلل",
                RequisitionId = requisition.Id,
                IntakeChannel = RecruitmentIntakeChannel.Public,
                Status = RecruitmentApplicationStatus.Submitted,
                AppliedAt = DateTime.UtcNow
            };
            db.RecruitmentApplications.Add(app); // the context records its Received event in the same save
            db.CitizenApplications.Add(new CitizenApplication
            {
                TenantId = tenant,
                PublicJobPostingId = posting.Id,
                RequisitionId = requisition.Id,
                PromotedRecruitmentApplicationId = app.Id,
                ReferenceNumber = $"CA-{Guid.NewGuid():N}"[..15],
                FullName = "متقدّم " + i,
                NationalId = $"{i:D2}{Guid.NewGuid().ToString("N")[..9]}",
                Email = $"c{i}@test.sy",
                Phone = "+963999000" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                NotificationChannel = NotificationChannel.Email,
                NotificationConsentAt = DateTime.UtcNow
            });
            ids.Add(app.Id);
        }

        await db.SaveChangesAsync();
        return new Seed(ids, position.Id);
    }

    private HRCoreDbContext CreateContext(Guid tenant, IInterceptor interceptor)
    {
        string connectionString;
        using (var probe = fixture.CreateContext(tenant))
            connectionString = probe.Database.GetConnectionString()!;
        return new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options);
    }

    private static CandidateNotificationDispatcher Dispatcher(HRCoreDbContext db) =>
        new(db,
            new NotificationChannelSenderRegistry([], Options.Create(new CandidateNotificationOptions())),
            Options.Create(new RecruitmentOptions()),
            NullLogger<CandidateNotificationDispatcher>.Instance)
        { DisplayZone = TimeZoneInfo.Utc };

    /// <summary>Holds the first save of each context until both dispatchers reach it (both have read the same rows).</summary>
    private sealed class FirstSaveBarrier(Barrier barrier) : SaveChangesInterceptor
    {
        private int saves;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saves) == 1)
                await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(15)), cancellationToken);
            return result;
        }
    }
}
