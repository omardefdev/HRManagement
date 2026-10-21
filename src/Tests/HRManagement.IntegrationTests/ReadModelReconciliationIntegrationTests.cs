using Dev.Analytics.Application.Services;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>AC-19: service-level reconciliation pipeline (in-memory EF).</summary>
public class ReadModelReconciliationIntegrationTests
{
    [Fact]
    public async Task Reconcile_pipeline_detects_drift_then_idempotent()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(TenantDefaults.DefaultTenantId);
        await using var db = new AnalyticsDbContext(options, tenant.Object);

        var readModelId = Guid.NewGuid();
        db.ReadModels.Add(new ReadModel
        {
            Id = readModelId,
            Name = "headcount",
            LastFingerprint = "STALE",
            Schema = "{}",
            SourceEventName = "EmployeeCreated"
        });
        db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            Id = Guid.NewGuid(),
            EventName = "EmployeeCreated",
            OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new ReadModelReconciliationService(db, new InMemoryReadModelReconciliationStatusStore());

        var dry = await svc.ReconcileAsync(readModelId, null, dryRun: true);
        Assert.True(dry.DriftDetected);
        Assert.False(dry.Corrected);

        var live = await svc.ReconcileAsync(readModelId, null, dryRun: false);
        Assert.True(live.Corrected);

        var second = await svc.ReconcileAsync(readModelId, null, dryRun: false);
        Assert.False(second.DriftDetected);

        var status = new InMemoryReadModelReconciliationStatusStore();
        status.Save(second);
        Assert.NotNull(status.GetLast(readModelId));
    }
}
