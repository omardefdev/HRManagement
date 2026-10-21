using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Ac33AssetLossWorkflowTests
{
    private sealed class AssetLossApprovedMessage : IAssetLossApproved
    {
        public Guid LossReportId { get; init; }
        public Guid AssetId { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public string SourceSystem { get; init; } = "workflow-test";
        public Guid? TenantId { get; init; }
    }

    private sealed class AssetLossRejectedMessage : IAssetLossRejected
    {
        public Guid LossReportId { get; init; }
        public Guid AssetId { get; init; }
        public string? Reason { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public string SourceSystem { get; init; } = "workflow-test";
        public Guid? TenantId { get; init; }
    }

    [Fact]
    public async Task AssetLossApproved_confirms_report_and_snapshots_after_state()
    {
        await using var db = await CreatePendingLossDbAsync();
        var report = await db.AssetLossReports.Include(r => r.Asset).SingleAsync();
        var lookup = new Mock<IEmployeeLookup>();
        lookup.Setup(l => l.GetEmployeeDisplayNamesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>
            {
                [AcceptanceSeedIds.EmpBr1Id] = "موظف فرع 1 — قبول"
            });
        var consumer = new AssetLossApprovedConsumer(
            db,
            lookup.Object,
            Mock.Of<ILogger<AssetLossApprovedConsumer>>());

        var message = new AssetLossApprovedMessage
        {
            LossReportId = report.Id,
            AssetId = report.AssetId,
            TenantId = AcceptanceSeedIds.AgencyAId
        };
        var context = Mock.Of<ConsumeContext<IAssetLossApproved>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        var updated = await db.AssetLossReports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.Equal(AssetLossReportStatus.Confirmed, updated.Status);
        Assert.Equal(AssetStatus.Lost, updated.AfterAssetStatus);
        Assert.NotNull(updated.AfterStateJson);
        Assert.NotNull(updated.SettlementApprovedAt);
        Assert.Equal("workflow-test", updated.SettlementApprovedBy);
        var asset = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == report.AssetId);
        Assert.Equal(AssetStatus.Lost, asset.Status);
    }

    [Fact]
    public async Task AssetLossRejected_leaves_before_snapshot_without_after()
    {
        await using var db = await CreatePendingLossDbAsync();
        var report = await db.AssetLossReports.SingleAsync();
        var consumer = new AssetLossRejectedConsumer(db, Mock.Of<ILogger<AssetLossRejectedConsumer>>());

        var message = new AssetLossRejectedMessage
        {
            LossReportId = report.Id,
            AssetId = report.AssetId,
            Reason = "أدلة غير كافية"
        };
        var context = Mock.Of<ConsumeContext<IAssetLossRejected>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        var updated = await db.AssetLossReports.AsNoTracking().SingleAsync(r => r.Id == report.Id);
        Assert.Equal(AssetLossReportStatus.Rejected, updated.Status);
        Assert.Equal(AssetStatus.InUse, updated.BeforeAssetStatus);
        Assert.NotNull(updated.BeforeStateJson);
        Assert.Null(updated.AfterAssetStatus);
        Assert.Null(updated.AfterStateJson);
        var asset = await db.Assets.AsNoTracking().SingleAsync(a => a.Id == report.AssetId);
        Assert.Equal(AssetStatus.InUse, asset.Status);
    }

    private static async Task<AdminLogisticsDbContext> CreatePendingLossDbAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await AcceptanceLogisticsSeeder.SeedAsync(db);

        var asset = await db.Assets.SingleAsync(a => a.Id == AcceptanceSeedIds.AcceptanceAssetBr1Id);
        var open = await db.AssetAssignments
            .Where(a => a.AssetId == asset.Id && a.ReturnedAt == null)
            .ToListAsync();
        var report = new AssetLossReport
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            TenantId = AcceptanceSeedIds.AgencyAId,
            Status = AssetLossReportStatus.PendingApproval,
            Reason = AssetLossReason.Misplacement,
            Context = AssetLossContext.WithOpenAssignment,
            EvidenceRef = "AC-33-EVIDENCE"
        };
        AssetLossStateCapture.CaptureBefore(asset, open, report);
        db.AssetLossReports.Add(report);
        await db.SaveChangesAsync();
        return db;
    }
}
