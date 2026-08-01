namespace HRManagement.UnitTests;

public class Hard001HardeningTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "HARD_001_VERIFICATION.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(params string[] parts) =>
        System.IO.File.ReadAllText(Path.Combine(new[] { ResolveRepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void Hard001_baseline_docs_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "docs/hardening/HARD_001_POSTGRESQL.md",
                     "docs/hardening/HARD_001_RABBITMQ.md",
                     "docs/hardening/HARD_001_KEYCLOAK.md",
                     "docs/hardening/HARD_001_MINIO.md",
                     "docs/hardening/HARD_001_CONTAINERS.md",
                     "docs/hardening/HARD_001_KUBERNETES.md",
                     "docs/hardening/HARD_001_GATEWAY.md",
                     "docs/hardening/HARD_001_EXCEPTION_REGISTER.md",
                     "docs/HARD_001_VERIFICATION.md"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }
    }

    [Fact]
    public void Exception_register_has_expiry_template()
    {
        var text = Read("docs", "hardening", "HARD_001_EXCEPTION_REGISTER.md");
        Assert.Contains("Expires (UTC)", text);
        Assert.Contains("HARD-001-EX-", text);
        Assert.Contains("≤ 90 days", text);
    }

    [Fact]
    public void Psa_and_security_context_artifacts_exist()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in new[]
                 {
                     "deploy/k8s/base/pod-security/restricted-psa.yaml",
                     "deploy/k8s/base/pod-security/security-context-defaults.yaml",
                     "deploy/k8s/base/pod-security/kustomization.yaml"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))), rel);
        }

        var psa = Read("deploy", "k8s", "base", "pod-security", "restricted-psa.yaml");
        Assert.Contains("pod-security.kubernetes.io/enforce: restricted", psa);

        var kust = Read("deploy", "k8s", "base", "kustomization.yaml");
        Assert.Contains("pod-security", kust);
        Assert.Contains("restricted-psa.yaml", kust);
    }

    [Fact]
    public void Compliance_script_and_workflow_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "hard001-compliance-scan.ps1")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, ".github", "workflows", "hardening-check.yml")));

        var compose = Read("docker-compose.production.yml");
        Assert.Contains("x-app-hardening", compose);
        Assert.Contains("no-new-privileges", compose);
        Assert.Contains("cap_drop", compose);
    }
}
