using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

public class Ac33AssetLossStateTests
{
    [Fact]
    public void CaptureBefore_records_asset_status_and_open_assignments()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "P1", Name = "Laptop", Status = AssetStatus.InUse };
        var assignment = new AssetAssignment
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            EmployeeId = AcceptanceSeedIds.EmpBr1Id,
            Status = AssetAssignmentStatus.Active
        };
        var report = new AssetLossReport { AssetId = asset.Id };

        AssetLossStateCapture.CaptureBefore(asset, [assignment], report);

        Assert.Equal(AssetStatus.InUse, report.BeforeAssetStatus);
        Assert.NotNull(report.BeforeStateJson);
        Assert.Contains("InUse", report.BeforeStateJson);
        Assert.Contains(AcceptanceSeedIds.EmpBr1Id.ToString("D"), report.BeforeStateJson);
        Assert.Null(report.AfterAssetStatus);
        Assert.Null(report.AfterStateJson);
    }

    [Fact]
    public void ApplyLost_fills_after_snapshot()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "P1", Name = "Laptop", Status = AssetStatus.InUse, IsPrivileged = true };
        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            EmployeeId = Guid.NewGuid(),
            AssignedAt = DateTime.UtcNow,
            Status = AssetAssignmentStatus.Active
        };
        var report = new AssetLossReport
        {
            AssetId = asset.Id,
            Reason = AssetLossReason.Theft,
            Context = AssetLossContext.WithOpenAssignment,
            EvidenceRef = "LOSS-EVIDENCE"
        };
        AssetLossStateCapture.CaptureBefore(asset, [assignment], report);

        AssetLostRules.ApplyLost(asset, [assignment], report, "حائز تجريبي");

        Assert.Equal(AssetStatus.Lost, report.AfterAssetStatus);
        Assert.NotNull(report.AfterStateJson);
        Assert.Contains("Lost", report.AfterStateJson);
        Assert.Equal(AssetLossReportStatus.Confirmed, report.Status);
    }

    [Fact]
    public void Recover_updates_after_snapshot_to_in_storage()
    {
        var asset = new Asset { TagNumber = "P1", Name = "Laptop", Status = AssetStatus.Lost };
        var report = new AssetLossReport
        {
            AssetId = asset.Id,
            Status = AssetLossReportStatus.Confirmed,
            BeforeAssetStatus = AssetStatus.InUse,
            BeforeStateJson = "{\"assetStatus\":\"InUse\"}",
            AfterAssetStatus = AssetStatus.Lost,
            AfterStateJson = "{\"assetStatus\":\"Lost\"}"
        };

        AssetLostRules.Recover(asset, report, "وُجد في المستودع");

        Assert.Equal(AssetStatus.InStorage, asset.Status);
        Assert.Equal(AssetLossReportStatus.Recovered, report.Status);
        Assert.Equal(AssetStatus.InStorage, report.AfterAssetStatus);
        Assert.Contains("InStorage", report.AfterStateJson);
    }

    [Fact]
    public void Reject_leaves_before_without_after()
    {
        var report = new AssetLossReport
        {
            Status = AssetLossReportStatus.PendingApproval,
            BeforeAssetStatus = AssetStatus.InUse,
            BeforeStateJson = "{\"assetStatus\":\"InUse\"}"
        };

        report.Status = AssetLossReportStatus.Rejected;

        Assert.Equal(AssetStatus.InUse, report.BeforeAssetStatus);
        Assert.Null(report.AfterAssetStatus);
        Assert.Null(report.AfterStateJson);
    }
}
