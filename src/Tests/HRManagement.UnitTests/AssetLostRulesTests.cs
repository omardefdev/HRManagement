using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;

namespace HRManagement.UnitTests;

public class AssetLostRulesTests
{
    [Fact]
    public void ApplyLost_preserves_EvidenceRef_and_sets_ReturnNotes()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "A1", Name = "Laptop", Status = AssetStatus.InUse };
        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            EmployeeId = Guid.NewGuid(),
            AssignedAt = DateTime.UtcNow,
            EvidenceRef = "HANDOVER-001",
            Status = AssetAssignmentStatus.Active
        };
        var report = new AssetLossReport
        {
            AssetId = asset.Id,
            Reason = AssetLossReason.Theft,
            Context = AssetLossContext.WithOpenAssignment,
            EvidenceRef = "LOSS-REF-99",
            Notes = "سرقة من المكتب"
        };

        AssetLostRules.ApplyLost(asset, [assignment], report, "أحمد علي — دائرة الموارد");

        Assert.Equal(AssetStatus.Lost, asset.Status);
        Assert.Equal(AssetLossReportStatus.Confirmed, report.Status);
        Assert.NotNull(assignment.ReturnedAt);
        Assert.Equal(AssetAssignmentStatus.Returned, assignment.Status);
        Assert.Equal("HANDOVER-001", assignment.EvidenceRef);
        Assert.Contains("[فقدان]", assignment.ReturnNotes);
        Assert.Contains("LOSS-REF-99", assignment.ReturnNotes);
        Assert.Contains("حائز: أحمد علي — دائرة الموارد", assignment.ReturnNotes);
    }

    [Fact]
    public void FormatReturnNotes_uses_holder_display_name_not_guid()
    {
        var report = new AssetLossReport
        {
            Reason = AssetLossReason.Other,
            Context = AssetLossContext.WithOpenAssignment
        };
        var notes = AssetLostRules.FormatReturnNotes(report, "سارة محمود — قسم التقنية");
        Assert.Contains("حائز: سارة محمود — قسم التقنية", notes);
        Assert.DoesNotContain("00000000", notes);
    }

    [Fact]
    public void CanRequestLoss_false_when_pending_report()
    {
        var asset = new Asset { TagNumber = "A1", Name = "X", Status = AssetStatus.InUse };
        var pending = new AssetLossReport { Status = AssetLossReportStatus.PendingApproval };
        Assert.False(AssetLostRules.CanRequestLoss(asset, pending));
    }

    [Fact]
    public void CanRecover_only_when_lost()
    {
        var lost = new Asset { TagNumber = "A1", Name = "X", Status = AssetStatus.Lost };
        var inUse = new Asset { TagNumber = "A2", Name = "Y", Status = AssetStatus.InUse };
        Assert.True(AssetLostRules.CanRecover(lost));
        Assert.False(AssetLostRules.CanRecover(inUse));
    }

    [Fact]
    public void RequiresLossApproval_follows_privileged_classification()
    {
        var privileged = new Asset { TagNumber = "P", Name = "P", IsPrivileged = true, Value = 100m };
        var unclassifiedHighValue = new Asset { TagNumber = "H", Name = "H", IsPrivileged = false, Value = 50_000m };
        var normal = new Asset { TagNumber = "N", Name = "N", IsPrivileged = false, Value = 100m };
        Assert.True(AssetLostRules.RequiresLossApproval(privileged));
        Assert.False(AssetLostRules.RequiresLossApproval(unclassifiedHighValue));
        Assert.False(AssetLostRules.RequiresLossApproval(normal));
    }

    [Fact]
    public void InferContext_uses_open_assignment()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "A1", Name = "X", Status = AssetStatus.InUse };
        var open = new AssetAssignment { AssetId = asset.Id, ReturnedAt = null };
        Assert.Equal(AssetLossContext.WithOpenAssignment, AssetLostRules.InferContext(asset, [open]));
        Assert.Equal(AssetLossContext.InStorage, AssetLostRules.InferContext(asset, []));
    }
}
