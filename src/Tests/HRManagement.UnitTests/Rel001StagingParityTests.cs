using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Rel001ParityMatrixTests
{

    [Fact]
    public void Matrix_schema_has_journeys_and_comparisons()
    {
        var path = RepoPaths.Resolve("docs", "rel-staging", "parity-matrix.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-001", root.GetProperty("card").GetString());
        var journeys = root.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TopologyParity", journeys);
        Assert.Contains("SyntheticReset", journeys);
        Assert.Contains("ReleaseRehearsal", journeys);

        foreach (var c in root.GetProperty("comparisons").EnumerateArray())
        {
            var status = c.GetProperty("status").GetString();
            Assert.True(status is "match" or "approved",
                $"comparison {c.GetProperty("id").GetString()} status must be match|approved, got {status}");
        }
    }

    [Fact]
    public void Synthetic_manifest_forbids_prod_dump()
    {
        var path = RepoPaths.Resolve("testdata", "rel001", "manifest.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var policy = doc.RootElement.GetProperty("policy");
        Assert.True(policy.GetProperty("forbidProductionDump").GetBoolean());
        Assert.True(policy.GetProperty("forbidRealPii").GetBoolean());
        Assert.True(policy.GetProperty("maxEmployeesWithoutApproval").GetInt32() >= 1000);
    }

    [Fact]
    public void Required_scripts_and_realm_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "rel001-parity-diff.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "rel001-seed-synthetic.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "rel001-reset-staging.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docker", "keycloak", "realm-rel001-staging.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("deploy", "k8s", "overlays", "staging-ha", "ingress.yaml")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("deploy", "k8s", "data", "overlays", "staging-data-ha", "tls-stub.yaml")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_001_FEATURE_FLAGS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_001_ENV_OWNERSHIP.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_001_REHEARSAL_CHECKLIST.md")));
    }

    [Fact]
    public void Staging_ingress_has_tls_block()
    {
        var text = System.IO.File.ReadAllText(RepoPaths.Resolve("deploy", "k8s", "overlays", "staging-ha", "ingress.yaml"));
        Assert.Contains("tls:", text);
        Assert.Contains("hr-gateway-tls", text);
        Assert.Contains("hr.staging.example.com", text);
        Assert.Contains("ssl-redirect: \"true\"", text);
    }
}
