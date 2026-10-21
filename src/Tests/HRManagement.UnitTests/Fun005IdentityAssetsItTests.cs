using System.Text.Json;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using IT.Infrastructure.Domain.Enums;

namespace HRManagement.UnitTests;

public class Fun005JourneyMatrixTests
{
    private static string ResolveMatrixPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "fun-identity", "journey-matrix.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/fun-identity/journey-matrix.json");
    }

    [Fact]
    public void Matrix_includes_idcard_assets_it()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveMatrixPath()));
        var ids = doc.RootElement.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("IDCard", ids);
        Assert.Contains("Assets", ids);
        Assert.Contains("ITProvision", ids);
    }
}

public class Fun005CardLifecycleTests
{
    [Fact]
    public void Checklist_defaults_include_deliver_and_revoke()
    {
        var id = Guid.NewGuid();
        var steps = CardChecklistDefaults.CreateFor(id).Select(s => s.Step).ToHashSet();
        Assert.Contains(CardChecklistStep.Delivered, steps);
        Assert.Contains(CardChecklistStep.Revoked, steps);
        Assert.Equal(48, CardChecklistDefaults.DefaultRevokeSlaHours);
    }

    [Fact]
    public void Delivery_proof_rejects_blank_signature_placeholder()
    {
        Assert.False(CardDeliveryRules.HasDeliveryProof(null, null));
        Assert.False(CardDeliveryRules.HasDeliveryProof("موقّع", null));
        Assert.True(CardDeliveryRules.HasDeliveryProof("توقيع مسحوب", null));
        Assert.True(CardDeliveryRules.HasDeliveryProof(null, Guid.NewGuid()));
    }

    [Fact]
    public void Lost_and_revoke_and_sla()
    {
        Assert.True(CardIssuanceWorkflow.CanMarkLost(CardStatus.Active));
        Assert.True(CardIssuanceWorkflow.CanRevoke(CardStatus.Lost));
        Assert.True(CardIssuanceWorkflow.CanRenew(CardStatus.Lost, null));
        var started = DateTime.UtcNow.AddHours(-50);
        Assert.True(CardIssuanceWorkflow.IsRevokeSlaBreached(null, started, 48));
        Assert.False(CardIssuanceWorkflow.IsRevokeSlaBreached(DateTime.UtcNow, started, 48));
    }

    [Fact]
    public void Soft_cancel_marks_request()
    {
        var req = new IDCard.Domain.Entities.CardRequest
        {
            EmployeeId = Guid.NewGuid(),
            Status = CardRequestStatus.Pending
        };
        CardRequestRules.SoftCancel(req, "اختبار");
        Assert.True(req.IsCancelled);
        Assert.Equal(CardRequestStatus.Rejected, req.Status);
    }
}

public class Fun005AssetSingleHolderTests
{
    [Fact]
    public void Rejects_second_open_assignment()
    {
        var assetId = Guid.NewGuid();
        var asset = new Asset { Id = assetId, TagNumber = "T1", Name = "Laptop", Status = AssetStatus.InStorage };
        var open = new[]
        {
            new AssetAssignment { AssetId = assetId, EmployeeId = Guid.NewGuid(), AssignedAt = DateTime.UtcNow, Status = AssetAssignmentStatus.Active }
        };
        Assert.False(AssetAssignmentRules.CanAssign(asset, open));
        Assert.True(AssetAssignmentRules.CanAssign(asset, Array.Empty<AssetAssignment>()));
    }

    [Fact]
    public void Privileged_requires_evidence()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "P1", Name = "Badge", IsPrivileged = true };
        Assert.True(AssetAssignmentRules.RequiresEvidence(asset));
    }

    [Fact]
    public void Handover_proof_accepts_text_or_file()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "P1", Name = "Badge", IsPrivileged = true };
        Assert.False(AssetAssignmentRules.RequiresHandoverProof(asset, null, null));
        Assert.False(AssetAssignmentRules.RequiresHandoverProof(asset, "موقّع", null));
        Assert.True(AssetAssignmentRules.RequiresHandoverProof(asset, "محضر 123", null));
        Assert.True(AssetAssignmentRules.RequiresHandoverProof(asset, null, Guid.NewGuid()));
    }

    [Fact]
    public void Rejected_assignment_does_not_block_new_assign()
    {
        var assetId = Guid.NewGuid();
        var asset = new Asset { Id = assetId, TagNumber = "T2", Name = "Tablet", Status = AssetStatus.InStorage };
        var rejectedOpen = new[]
        {
            new AssetAssignment
            {
                AssetId = assetId,
                EmployeeId = Guid.NewGuid(),
                AssignedAt = DateTime.UtcNow,
                Status = AssetAssignmentStatus.Rejected,
                ReturnedAt = null
            }
        };
        Assert.True(AssetAssignmentRules.CanAssign(asset, rejectedOpen));
    }

    [Fact]
    public void Approval_required_for_privileged_classification_only()
    {
        var privileged = new Asset { IsPrivileged = true };
        var expensiveUnclassified = new Asset { Value = 15_000m, IsPrivileged = false };
        var cheap = new Asset { Value = 100m };
        Assert.True(AssetApprovalRules.RequiresApproval(privileged));
        Assert.False(AssetApprovalRules.RequiresApproval(expensiveUnclassified));
        Assert.False(AssetApprovalRules.RequiresApproval(cheap));
    }

    [Fact]
    public void Supply_order_approver_must_differ_from_requester()
    {
        var requester = Guid.NewGuid();
        Assert.False(SupplyOrderWorkflowRules.ApproverIsDistinctFromRequester(requester, requester));
        Assert.True(SupplyOrderWorkflowRules.ApproverIsDistinctFromRequester(requester, Guid.NewGuid()));
    }

    [Fact]
    public void Lost_status_blocks_assign()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "L1", Name = "Phone", Status = AssetStatus.Lost };
        Assert.False(AssetAssignmentRules.CanAssign(asset, Array.Empty<AssetAssignment>()));
    }

    [Fact]
    public void ApplyLost_does_not_overwrite_handover_evidence()
    {
        var assignment = new AssetAssignment
        {
            EvidenceRef = "EVD-HANDOVER",
            Status = AssetAssignmentStatus.Active
        };
        var asset = new Asset { TagNumber = "L1", Name = "Phone", Status = AssetStatus.InUse };
        var report = new AssetLossReport
        {
            Reason = AssetLossReason.Misplacement,
            Context = AssetLossContext.WithOpenAssignment,
            EvidenceRef = "EVD-LOSS"
        };
        AssetLostRules.ApplyLost(asset, [assignment], report);
        Assert.Equal("EVD-HANDOVER", assignment.EvidenceRef);
        Assert.Contains("[فقدان]", assignment.ReturnNotes);
    }
}

public class Fun005ItProvisioningTests
{
    [Fact]
    public void Provisioning_status_values_exist()
    {
        Assert.Equal(0, (int)ProvisioningStatus.Pending);
        Assert.Equal(1, (int)ProvisioningStatus.Succeeded);
        Assert.Equal(2, (int)ProvisioningStatus.Failed);
    }
}
