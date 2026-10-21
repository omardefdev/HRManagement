using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>PLAN-00.1: tenant IDs aligned across seed, personas, and e2e manifest.</summary>
public class AcceptanceTenantConsistencyTests
{
    [Fact]
    public void Acceptance_seed_ids_match_personas_and_manifest()
    {
        var manifestPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "e2e", "manifest.json");
        var personasPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "personas.json");
        Assert.True(File.Exists(manifestPath), manifestPath);
        Assert.True(File.Exists(personasPath), personasPath);

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var tenants = manifest.RootElement.GetProperty("tenants").EnumerateArray().ToList();
        var agencyA = tenants.First(t => t.GetProperty("code").GetString() == AcceptanceSeedIds.AgencyACode);
        var agencyB = tenants.First(t => t.GetProperty("code").GetString() == AcceptanceSeedIds.AgencyBCode);

        Assert.Equal(AcceptanceSeedIds.AgencyAId, Guid.Parse(agencyA.GetProperty("id").GetString()!));
        Assert.Equal(AcceptanceSeedIds.AgencyBId, Guid.Parse(agencyB.GetProperty("id").GetString()!));
        Assert.Equal(AcceptanceSeedIds.Br1OrgUnitId, Guid.Parse(agencyA.GetProperty("branchOrgUnitId").GetString()!));

        var personasFile = JsonSerializer.Deserialize<AcceptancePersonaFile>(
            File.ReadAllText(personasPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(personasFile);

        var br1Mgr = personasFile!.Personas.First(p => p.Id == "br1-mgr");
        Assert.Equal(AcceptanceSeedIds.AgencyAId.ToString(), br1Mgr.TenantId);
        Assert.Equal(AcceptanceSeedIds.Br1OrgUnitId.ToString(), br1Mgr.ScopeOrgUnitId);

        var outsider = personasFile.Personas.First(p => p.Id == "outsider-b");
        Assert.Equal(AcceptanceSeedIds.AgencyBId.ToString(), outsider.TenantId);
    }

    [Fact]
    public void Legacy_conflicting_tenant_ids_removed_from_manifest()
    {
        var manifestPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "e2e", "manifest.json");
        var json = File.ReadAllText(manifestPath);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", json);
        Assert.DoesNotContain("33333333-3333-3333-3333-333333333333", json);
    }
}
