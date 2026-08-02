using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Syr008HostingTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "syr008-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Hosting_topology_has_primary_and_dr()
    {
        var path = RepoPaths.Resolve("docs", "syr", "hosting-topology.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("SYR-008", root.GetProperty("card").GetString());

        var sites = root.GetProperty("sites").EnumerateArray().ToList();
        Assert.True(sites.Count >= 2, "must have at least primary + DR");

        var primary = sites.First(s => s.GetProperty("role").GetString() == "primary");
        Assert.True(primary.GetProperty("sovereignCustody").GetBoolean());
        Assert.Equal("deny-by-default", primary.GetProperty("networkEgress").GetString());

        var dr = sites.First(s => s.GetProperty("role").GetString() == "disaster-recovery");
        Assert.True(dr.GetProperty("sovereignCustody").GetBoolean());
    }

    [Fact]
    public void DNS_uses_gov_sy_domain()
    {
        var path = RepoPaths.Resolve("docs", "syr", "hosting-topology.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var dns = doc.RootElement.GetProperty("dns");

        Assert.Contains(".gov.sy", dns.GetProperty("primaryDomain").GetString());
        Assert.True(dns.GetProperty("dnssec").GetBoolean());
    }

    [Fact]
    public void Egress_default_deny()
    {
        var path = RepoPaths.Resolve("docs", "syr", "hosting-topology.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));

        var rules = doc.RootElement.GetProperty("egressRules").EnumerateArray().ToList();
        var defaultRule = rules.Last();
        Assert.Equal("*", defaultRule.GetProperty("destination").GetString());
        Assert.Equal("deny", defaultRule.GetProperty("action").GetString());
    }

    [Fact]
    public void Certificate_config_uses_national_ca()
    {
        var path = RepoPaths.Resolve("docs", "syr", "hosting-topology.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var certs = doc.RootElement.GetProperty("certificates");

        var serverCa = certs.GetProperty("serverTls").GetProperty("ca").GetString()!;
        Assert.Contains("Syrian", serverCa, StringComparison.OrdinalIgnoreCase);
        Assert.True(certs.GetProperty("serverTls").GetProperty("ocsp").GetBoolean());
    }

    [Fact]
    public void Certificate_lifecycle_doc_covers_key_topics()
    {
        var path = RepoPaths.Resolve("docs", "syr", "certificate-lifecycle.md");
        var content = System.IO.File.ReadAllText(path);

        Assert.Contains("HSM", content);
        Assert.Contains("Revocation", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCSP", content);
        Assert.Contains("CRL", content);
        Assert.Contains("Key Ceremony", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Renewal", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Support_access_is_jit_and_logged()
    {
        var path = RepoPaths.Resolve("docs", "syr", "hosting-topology.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var support = doc.RootElement.GetProperty("supportAccess");

        Assert.Contains("JIT", support.GetProperty("method").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mandatory", support.GetProperty("logging").GetString());
        Assert.False(support.GetProperty("vendorSecretAccess").GetBoolean());
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "syr008-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SYR_008_NATIONAL_HOSTING.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "hosting-topology.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "syr", "certificate-lifecycle.md")));
    }
}
