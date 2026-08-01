using System.Diagnostics;
using System.Xml.Linq;

namespace HRManagement.UnitTests;

/// <summary>BLD-001: build provenance, NuGet mirror, Kyverno signed-image admission.</summary>
public class Bld001BuildProvenanceTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docker-compose.production.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(ResolveRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Bld001_docs_scripts_admission_and_nuget_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/BLD_001_BUILD_PROVENANCE.md",
                     "NuGet.config",
                     "deploy/k8s/base/admission/require-signed-images.yaml",
                     "deploy/k8s/base/admission/kustomization.yaml",
                     "scripts/bld001-validate.ps1",
                     "scripts/bld001-verify-provenance.ps1"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void NuGet_config_defines_sovereign_mirror_placeholder()
    {
        var path = Path.Combine(ResolveRepoRoot(), "NuGet.config");
        var doc = XDocument.Load(path);
        var sources = doc.Descendants("packageSources")
            .Descendants("add")
            .Where(e => e.Attribute("key")?.Value == "sovereign-mirror")
            .ToList();
        Assert.Single(sources);
        var value = sources[0].Attribute("value")?.Value ?? "";
        Assert.Contains("SOVEREIGN_NUGET_MIRROR", value);
    }

    [Fact]
    public void Admission_policy_requires_cosign_verification()
    {
        var yaml = Read("deploy/k8s/base/admission/require-signed-images.yaml");
        Assert.Contains("require-signed-images", yaml);
        Assert.Contains("verifyImages", yaml);
        Assert.Contains("ghcr.io/OWNER/hrmanagement", yaml);
        Assert.Contains("ClusterPolicy", yaml);
    }

    [Fact]
    public void Base_kustomization_includes_admission()
    {
        var kust = Read("deploy/k8s/base/kustomization.yaml");
        Assert.Contains("admission", kust);
    }

    [Fact]
    public void Deploy_workflow_signs_and_attests_provenance()
    {
        var deploy = Read(".github/workflows/deploy.yml");
        Assert.Contains("cosign sign", deploy);
        Assert.Contains("attest-build-provenance", deploy);
    }

    [Fact]
    public void Bld001_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var script = Path.Combine(root, "scripts", "bld001-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{root}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"bld001-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}
