using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;

public class Nat008GoNoGoTests
{
    private static string ResolveRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (global::System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    private static string RepoRoot
    {
        get
        {
            var script = ResolveRepoFile("scripts", "nat008-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Decision_card_is_nat008()
    {
        var path = ResolveRepoFile("docs", "nat", "go-no-go-decision.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("NAT-008", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Harness_done_live_decision_blocked()
    {
        var path = ResolveRepoFile("docs", "nat", "go-no-go-decision.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.Equal("Done", doc.RootElement.GetProperty("harnessStatus").GetString());
        Assert.Equal("Blocked", doc.RootElement.GetProperty("liveDecisionStatus").GetString());
    }

    [Fact]
    public void Nat_cert_review_lists_seven_certs()
    {
        var path = ResolveRepoFile("docs", "nat", "go-no-go-decision.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        var certs = doc.RootElement.GetProperty("natCertReview").GetProperty("requiredCerts")
            .EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Equal(7, certs.Count);
        Assert.Contains("NAT-007", certs);
    }

    [Fact]
    public void Authority_signatures_and_certification_fields()
    {
        var path = ResolveRepoFile("docs", "nat", "go-no-go-decision.json");
        using var doc = JsonDocument.Parse(global::System.IO.File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("authoritySignatures").GetArrayLength() >= 7);
        var cert = doc.RootElement.GetProperty("certification");
        Assert.True(cert.GetProperty("releaseDigestRequired").GetBoolean());
        Assert.True(cert.GetProperty("expiryDateRequired").GetBoolean());
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "NAT_008_GO_NO_GO.md")));
        Assert.True(global::System.IO.File.Exists(ResolveRepoFile("docs", "nat", "go-no-go-decision.json")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "nat008-validate.ps1");
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
}
