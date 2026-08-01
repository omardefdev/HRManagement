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
            new AssetAssignment { AssetId = assetId, EmployeeId = Guid.NewGuid(), AssignedAt = DateTime.UtcNow }
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
    public void Lost_status_blocks_assign()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "L1", Name = "Phone", Status = AssetStatus.Lost };
        Assert.False(AssetAssignmentRules.CanAssign(asset, Array.Empty<AssetAssignment>()));
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
