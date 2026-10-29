using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-030 (PLAN-30 H2, DECISIONS H3 "database counters remain the real guard"): on a migrated PostgreSQL, concurrent
/// OTP requests and verifies of one reference that all read the same rows before any of them writes still count every
/// wrong code (the lock fires and is audited once), consume a code once, and pass the resend window once.
/// </summary>
[Trait("Category", "Testcontainers")]
public class Hcm030CandidateOtpConcurrencyTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("integration-otp-hmac-key-0123456789abcdef");
    private static readonly Regex SixDigits = new(@"\b\d{6}\b", RegexOptions.None, TimeSpan.FromSeconds(1));

    [SkippableFact]
    public async Task Concurrent_wrong_codes_all_count_and_lock_the_portal_once()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var citizen = await SeedCitizenAsync(tenant);
        var sender = new CapturingSender();
        await using (var db = fixture.CreateContext(tenant))
            Assert.Equal(CandidateOtpRequestOutcome.Sent, (await Service(db, sender).RequestAsync(citizen.Reference, citizen.NationalId, "203.0.113.7")).Outcome);
        var code = SixDigits.Match(sender.Messages.Single().Body).Value;
        var wrong = code == "000000" ? "000001" : "000000";

        const int parallel = 5;
        var results = await RunConcurrentlyAsync(tenant, parallel, db => Service(db, sender).VerifyAsync(citizen.Reference, wrong, "203.0.113.7"));

        Assert.All(results, r => Assert.False(r.Succeeded));
        Assert.Contains(results, r => r.Outcome == CandidateOtpVerifyOutcome.Locked);
        await using var check = fixture.CreateContext(tenant);
        var challenge = await check.CandidateOtpChallenges.AsNoTracking().SingleAsync(c => c.CitizenApplicationId == citizen.Id);
        Assert.Equal(parallel, challenge.Attempts);
        Assert.NotNull((await check.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == citizen.Id)).PortalLockedUntil);
        Assert.Single(await check.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == citizen.Id && a.Action == CandidateOtpService.LockedAuditAction)
            .ToListAsync());
    }

    [SkippableFact]
    public async Task Concurrent_correct_codes_sign_in_once()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var citizen = await SeedCitizenAsync(tenant);
        var sender = new CapturingSender();
        await using (var db = fixture.CreateContext(tenant))
            await Service(db, sender).RequestAsync(citizen.Reference, citizen.NationalId, "203.0.113.7");
        var code = SixDigits.Match(sender.Messages.Single().Body).Value;

        var results = await RunConcurrentlyAsync(tenant, 3, db => Service(db, sender).VerifyAsync(citizen.Reference, code, "203.0.113.7"));

        Assert.Single(results, r => r.Succeeded);
        Assert.Equal(2, results.Count(r => r.Outcome == CandidateOtpVerifyOutcome.Invalid));
    }

    [SkippableFact]
    public async Task Concurrent_requests_pass_the_resend_window_once()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var citizen = await SeedCitizenAsync(tenant);
        var sender = new CapturingSender();

        var results = await RunConcurrentlyAsync(tenant, 2, db => Service(db, sender).RequestAsync(citizen.Reference, citizen.NationalId, "203.0.113.7"));

        Assert.Single(results, r => r.Outcome == CandidateOtpRequestOutcome.Sent);
        Assert.Single(results, r => r.Outcome == CandidateOtpRequestOutcome.ResendTooSoon);
        Assert.Single(sender.Messages);
        await using var check = fixture.CreateContext(tenant);
        Assert.Single(await check.CandidateOtpChallenges.AsNoTracking().Where(c => c.CitizenApplicationId == citizen.Id).ToListAsync());
    }

    private sealed record Citizen(Guid Id, string Reference, string NationalId);

    private async Task<Citizen> SeedCitizenAsync(Guid tenant)
    {
        await using var db = fixture.CreateContext(tenant);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenant);
        var requisition = Hcm026PostgresFixture.NewRequisition(tenant, position.Id, RequisitionStatus.Published);
        db.RecruitmentRequisitions.Add(requisition);
        var posting = new PublicJobPosting { TenantId = tenant, Title = "محلل", IsPublished = true, RequisitionId = requisition.Id };
        db.PublicJobPostings.Add(posting);
        var citizen = new CitizenApplication
        {
            TenantId = tenant,
            PublicJobPostingId = posting.Id,
            RequisitionId = requisition.Id,
            ReferenceNumber = $"CA-{Guid.NewGuid():N}"[..15],
            FullName = "متقدّم",
            NationalId = "05" + Guid.NewGuid().ToString("N")[..9],
            Email = "otp@test.sy",
            NotificationChannel = NotificationChannel.Email,
            NotificationConsentAt = DateTime.UtcNow
        };
        db.CitizenApplications.Add(citizen);
        await db.SaveChangesAsync();
        return new Citizen(citizen.Id, citizen.ReferenceNumber, citizen.NationalId!);
    }

    /// <summary>
    /// Runs <paramref name="count"/> calls on separate contexts; each holds its first write command (UPDATE) until every
    /// call reached it, so all of them have read the same rows first.
    /// </summary>
    private async Task<T[]> RunConcurrentlyAsync<T>(Guid tenant, int count, Func<HRCoreDbContext, Task<T>> call)
    {
        string connectionString;
        using (var probe = fixture.CreateContext(tenant))
            connectionString = probe.Database.GetConnectionString()!;
        using var barrier = new Barrier(count);
        var contexts = Enumerable.Range(0, count)
            .Select(_ => new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(new FirstWriteBarrier(barrier))
                .Options))
            .ToList();
        try
        {
            return await Task.WhenAll(contexts.Select(db => Task.Run(() => call(db))));
        }
        finally
        {
            foreach (var db in contexts)
                await db.DisposeAsync();
        }
    }

    private static CandidateOtpService Service(HRCoreDbContext db, CapturingSender sender) =>
        new(db,
            new NotificationChannelSenderRegistry([sender], Options.Create(new CandidateNotificationOptions { Provider = "FileDrop" })),
            new CandidateOtpKeyRing(Key),
            Options.Create(new RecruitmentOptions()),
            NullLogger<CandidateOtpService>.Instance);

    private sealed class FirstWriteBarrier(Barrier barrier) : DbCommandInterceptor
    {
        private int writes;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref writes) == 1)
                await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(15)), cancellationToken);
            return result;
        }
    }

    private sealed class CapturingSender : INotificationChannelSender
    {
        private readonly ConcurrentQueue<OutboundNotification> messages = new();

        public NotificationChannel Channel => NotificationChannel.Email;
        public IReadOnlyList<OutboundNotification> Messages => messages.ToArray();

        public Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)
        {
            messages.Enqueue(message);
            return Task.FromResult<ChannelSendResult>(new ChannelSendResult.Sent("fake-1"));
        }
    }
}
