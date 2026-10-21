using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Web.Services;

namespace HRManagement.UnitTests;

public class AssetAssignmentDisplayTests
{
    private static Asset PrivilegedAsset() => new()
    {
        Id = Guid.NewGuid(),
        TagNumber = "P1",
        Name = "Privileged",
        IsPrivileged = true,
        Status = AssetStatus.InStorage
    };

    private static Asset StandardAsset() => new()
    {
        Id = Guid.NewGuid(),
        TagNumber = "S1",
        Name = "Standard",
        IsPrivileged = false,
        Status = AssetStatus.InStorage
    };

    [Fact]
    public void FormatEvidenceSummary_non_privileged_without_evidence_returns_not_required()
    {
        var asset = StandardAsset();
        var assignment = new AssetAssignment { AssetId = asset.Id, EvidenceRef = null };

        Assert.Equal("غير مطلوب", AssetAssignmentDisplay.FormatEvidenceSummary(assignment, asset));
    }

    [Fact]
    public void FormatEvidenceSummary_privileged_text_only_returns_text()
    {
        var asset = PrivilegedAsset();
        var assignment = new AssetAssignment { AssetId = asset.Id, EvidenceRef = "EVD-2026-001" };

        Assert.Equal("EVD-2026-001", AssetAssignmentDisplay.FormatEvidenceSummary(assignment, asset));
    }

    [Fact]
    public void FormatEvidenceSummary_privileged_file_only_returns_file_label()
    {
        var asset = PrivilegedAsset();
        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            HandoverFileId = Guid.NewGuid()
        };

        Assert.Equal("ملف مرفق", AssetAssignmentDisplay.FormatEvidenceSummary(assignment, asset));
    }

    [Fact]
    public void FormatEvidenceSummary_privileged_text_and_file_returns_combined()
    {
        var asset = PrivilegedAsset();
        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            EvidenceRef = "EVD-001",
            HandoverFileId = Guid.NewGuid()
        };

        Assert.Equal("EVD-001 · ملف مرفق", AssetAssignmentDisplay.FormatEvidenceSummary(assignment, asset));
    }

    [Fact]
    public void FormatEvidenceSummary_privileged_missing_proof_returns_dash()
    {
        var asset = PrivilegedAsset();
        var assignment = new AssetAssignment { AssetId = asset.Id };

        Assert.Equal("—", AssetAssignmentDisplay.FormatEvidenceSummary(assignment, asset));
    }

    [Fact]
    public void FormatEvidenceSummary_signed_only_text_is_not_valid_proof()
    {
        var asset = PrivilegedAsset();
        var assignment = new AssetAssignment { AssetId = asset.Id, EvidenceRef = "موقّع" };

        Assert.Equal("—", AssetAssignmentDisplay.FormatEvidenceSummary(assignment, asset));
    }
}
