using Dev.Analytics.Application.Services;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class ReadModelReconciliationServiceTests
{
    private static AnalyticsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AnalyticsDbContext(options);
    }

    private static ReadModelReconciliationService CreateSvc(AnalyticsDbContext db) =>
        new(db, new InMemoryReadModelReconciliationStatusStore());

    [Fact]
    public async Task Detects_drift_when_fingerprint_stale()
    {
        await using var db = CreateDb();
        var org = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        db.ReadModels.Add(new ReadModel
        {
            Id = modelId,
            Name = "headcount",
            LastFingerprint = "STALE",
            Schema = "{}",
            SourceEventName = "EmployeeCreated"
        });
        db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            Id = Guid.NewGuid(),
            EventName = "EmployeeCreated",
            OccurredAt = DateTime.UtcNow,
            OrgUnitId = org
        });
        await db.SaveChangesAsync();

        var report = await CreateSvc(db).ReconcileAsync(modelId, org, dryRun: true);

        Assert.True(report.DriftDetected);
        Assert.True(report.DryRun);
        Assert.False(report.Corrected);
        Assert.NotEqual(report.BeforeFingerprint, report.AfterFingerprint);
    }

    [Fact]
    public async Task Corrects_and_documents_changes()
    {
        await using var db = CreateDb();
        var org = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        db.ReadModels.Add(new ReadModel
        {
            Id = modelId,
            Name = "headcount",
            LastFingerprint = "STALE",
            Schema = "{}",
            SourceEventName = "EmployeeCreated"
        });
        db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            Id = Guid.NewGuid(),
            EventName = "EmployeeCreated",
            OccurredAt = DateTime.UtcNow,
            OrgUnitId = org
        });
        await db.SaveChangesAsync();

        var report = await CreateSvc(db).ReconcileAsync(modelId, org, dryRun: false);

        Assert.True(report.DriftDetected);
        Assert.True(report.Corrected);
        Assert.Contains("LastFingerprint", report.Changes);

        var model = await db.ReadModels.FindAsync(modelId);
        Assert.Equal(report.AfterFingerprint, model!.LastFingerprint);
        Assert.NotEqual("{}", model.Schema);
    }

    [Fact]
    public async Task DryRun_does_not_persist()
    {
        await using var db = CreateDb();
        var modelId = Guid.NewGuid();
        db.ReadModels.Add(new ReadModel
        {
            Id = modelId,
            Name = "headcount",
            LastFingerprint = "STALE",
            Schema = "{}"
        });
        await db.SaveChangesAsync();

        await CreateSvc(db).ReconcileAsync(modelId, null, dryRun: true);

        var model = await db.ReadModels.FindAsync(modelId);
        Assert.Equal("STALE", model!.LastFingerprint);
        Assert.Equal("{}", model.Schema);
    }

    [Fact]
    public async Task Second_run_no_drift()
    {
        await using var db = CreateDb();
        var org = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        db.ReadModels.Add(new ReadModel
        {
            Id = modelId,
            Name = "headcount",
            LastFingerprint = "STALE",
            Schema = "{}",
            SourceEventName = "EmployeeCreated"
        });
        db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            Id = Guid.NewGuid(),
            EventName = "EmployeeCreated",
            OccurredAt = DateTime.UtcNow,
            OrgUnitId = org
        });
        await db.SaveChangesAsync();

        var svc = CreateSvc(db);
        await svc.ReconcileAsync(modelId, org, dryRun: false);
        var second = await svc.ReconcileAsync(modelId, org, dryRun: false);

        Assert.False(second.DriftDetected);
        Assert.False(second.Corrected);
    }
}
