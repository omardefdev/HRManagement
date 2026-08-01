using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Infrastructure.Consumers;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Records.Archive.Domain.Entities;
using Records.Archive.Infrastructure.Consumers;
using Records.Archive.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class DfdGapClosureConsumerTests
{
    private static async Task<(MassTransitTestScope Scope, SqliteConnection AnalyticsConn, SqliteConnection ArchiveConn)> CreateScopeAsync()
    {
        SqliteConnection? analyticsConn = null;
        SqliteConnection? archiveConn = null;
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                analyticsConn = new SqliteConnection("Data Source=:memory:");
                analyticsConn.Open();
                connections.Add(analyticsConn);
                IntegrationTestHelpers.AddSqliteDbContext<AnalyticsDbContext>(services, analyticsConn);

                archiveConn = new SqliteConnection("Data Source=:memory:");
                archiveConn.Open();
                connections.Add(archiveConn);
                IntegrationTestHelpers.AddSqliteDbContext<ArchiveDbContext>(services, archiveConn);
            },
            cfg =>
            {
                cfg.AddConsumer<ComplaintResolvedConsumer>();
                cfg.AddConsumer<CardIssuedAnalyticsConsumer>();
                cfg.AddConsumer<ComplaintResolvedArchiveConsumer>();
            });

        await scope.GetDb<AnalyticsDbContext>().Database.EnsureCreatedAsync();
        await scope.GetDb<ArchiveDbContext>().Database.EnsureCreatedAsync();
        return (scope, analyticsConn!, archiveConn!);
    }

    [Fact]
    public async Task ComplaintResolved_Records_AnalyticsEvent()
    {
        var setup = await CreateScopeAsync();
        await using var _ = setup.Scope;
        var complaintId = Guid.NewGuid();
        await setup.Scope.Harness.Bus.Publish<IComplaintResolved>(new
        {
            ComplaintId = complaintId,
            Subject = "test",
            Resolution = "closed",
            ResolvedAt = DateTime.UtcNow,
            CorrelationId = (Guid?)null,
            SourceSystem = "Test"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IComplaintResolved>(setup.Scope.Harness);
        var db = setup.Scope.GetDb<AnalyticsDbContext>();
        db.ChangeTracker.Clear();
        var events = await db.AnalyticsEvents.Where(e => e.EventName == "ComplaintResolved").ToListAsync();
        Assert.Single(events);
        Assert.Contains(complaintId.ToString("N"), events[0].Payload);
    }

    [Fact]
    public async Task CardIssued_Records_AnalyticsEvent()
    {
        var setup = await CreateScopeAsync();
        await using var _ = setup.Scope;
        var cardId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        await setup.Scope.Harness.Bus.Publish<ICardIssued>(new
        {
            EmployeeId = empId,
            IssuedCardId = cardId,
            SerialNumber = "SN-1",
            IssuedAt = DateTime.UtcNow
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ICardIssued>(setup.Scope.Harness);
        var db = setup.Scope.GetDb<AnalyticsDbContext>();
        db.ChangeTracker.Clear();
        var events = await db.AnalyticsEvents.Where(e => e.EventName == "CardIssued").ToListAsync();
        Assert.Single(events);
    }

    [Fact]
    public async Task ComplaintResolved_Creates_ArchiveItem()
    {
        var setup = await CreateScopeAsync();
        await using var _ = setup.Scope;
        var complaintId = Guid.NewGuid();
        await setup.Scope.Harness.Bus.Publish<IComplaintResolved>(new
        {
            ComplaintId = complaintId,
            Subject = "archive test",
            Resolution = "done",
            ResolvedAt = DateTime.UtcNow,
            CorrelationId = (Guid?)null,
            SourceSystem = "Test"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IComplaintResolved>(setup.Scope.Harness, minimumCount: 1);
        var db = setup.Scope.GetDb<ArchiveDbContext>();
        db.ChangeTracker.Clear();
        var items = await db.ArchiveItems.Where(x => x.ReferenceNumber == $"CMP-RES-{complaintId:N}").ToListAsync();
        Assert.Single(items);
    }
}
