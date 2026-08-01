using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>SEC-001: production attack surface — compose ports, NetworkPolicies, no anonymous audit ingest.</summary>
public class Sec001PortScanTests
{
    private static readonly string[] InternalServices =
    [
        "hr-core-web", "hr-contracts-web", "hr-health-web", "hr-movements-web",
        "hr-training-web", "payroll-web", "idcard-web", "records-archive-web",
        "admin-logistics-web", "it-infra-web", "dev-analytics-web", "compliance-web",
        "auth-directory-web", "file-storage-web",
        "postgresql", "rabbitmq", "minio",
        "seq", "tempo", "otel-collector", "prometheus", "alertmanager",
        "alert-sink", "blackbox-exporter", "rabbitmq-exporter", "postgres-exporter"
    ];

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docker-compose.production.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root with docker-compose.production.yml");
    }

    private static string Read(string relative)
    {
        var path = Path.Combine(ResolveRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(System.IO.File.Exists(path), $"missing {relative}");
        return System.IO.File.ReadAllText(path);
    }

    [Fact]
    public void Production_compose_clears_host_ports_for_internal_services()
    {
        var prod = Read("docker-compose.production.yml");
        foreach (var svc in InternalServices)
        {
            var pattern = new Regex(
                $@"(?ms)^\s{{2}}{Regex.Escape(svc)}:\s*.*?ports:\s*!override\s*\[\s*\]",
                RegexOptions.CultureInvariant);
            Assert.True(pattern.IsMatch(prod), $"production override missing ports: !override [] for {svc}");
        }
    }

    [Fact]
    public void Production_compose_isolates_internal_networks()
    {
        var prod = Read("docker-compose.production.yml");
        Assert.Contains("application:", prod);
        Assert.Contains("data:", prod);
        Assert.Contains("observability:", prod);
        Assert.Matches(@"(?ms)application:\s*\r?\n\s*driver:\s*bridge\s*\r?\n\s*internal:\s*true", prod);
        Assert.Matches(@"(?ms)data:\s*\r?\n\s*driver:\s*bridge\s*\r?\n\s*internal:\s*true", prod);
        Assert.Matches(@"(?ms)observability:\s*\r?\n\s*driver:\s*bridge\s*\r?\n\s*internal:\s*true", prod);
    }

    [Fact]
    public void Grafana_is_loopback_only_in_production()
    {
        var prod = Read("docker-compose.production.yml");
        Assert.Matches(@"(?ms)grafana:\s*.*?127\.0\.0\.1:\$\{GRAFANA_HTTP_PORT", prod);
    }

    [Fact]
    public void Gateway_and_keycloak_remain_edge_entrypoints()
    {
        var prod = Read("docker-compose.production.yml");
        Assert.Matches(@"(?ms)hr-gateway:\s*.*?ports:\s*!override\s*\r?\n\s*-\s*""\$\{GATEWAY_HTTP_PORT", prod);
        Assert.Matches(@"(?ms)keycloak:\s*.*?ports:\s*!override\s*\r?\n\s*-\s*""\$\{KEYCLOAK_HTTP_PORT", prod);
    }

    [Fact]
    public void Chaos_compose_is_profile_gated()
    {
        var chaos = Read("docker-compose.chaos.yml");
        Assert.Contains("profiles:", chaos);
        Assert.Contains("\"chaos\"", chaos);
    }

    [Fact]
    public void Security_events_ingest_is_not_anonymous()
    {
        var src = Read("src/Gateway/HRManagement.Gateway/Controllers/AuditSecurityController.cs");
        Assert.DoesNotMatch(
            @"\[HttpPost\(""security-events""\)\]\s*\r?\n\s*\[AllowAnonymous\]",
            src);
        Assert.Contains("[Authorize(Roles = HrManagementRoles.PlatformAdmin)]", src);
    }

    [Fact]
    public void Network_matrix_documents_approved_surface()
    {
        var matrix = Read("docs/NETWORK_MATRIX.md");
        Assert.Contains("SEC-001", matrix);
        Assert.Contains("hr-gateway", matrix);
        Assert.Contains("internal", matrix);
        Assert.Contains("NetworkPolicies", matrix);
    }

    [Fact]
    public void K8s_network_policies_exist_and_default_deny()
    {
        var root = ResolveRepoRoot();
        var npDir = Path.Combine(root, "deploy", "k8s", "base", "network-policies");
        Assert.True(Directory.Exists(npDir));

        foreach (var name in new[]
                 {
                     "deny-all-default.yaml",
                     "allow-dns.yaml",
                     "allow-ingress-to-gateway.yaml",
                     "allow-gateway-to-apps.yaml",
                     "allow-apps-to-data.yaml",
                     "allow-observability-scrape.yaml"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(npDir, name)), $"missing {name}");
        }

        var deny = System.IO.File.ReadAllText(Path.Combine(npDir, "deny-all-default.yaml"));
        Assert.Contains("podSelector: {}", deny);
        Assert.Contains("Ingress", deny);
        Assert.Contains("Egress", deny);

        var kust = Read("deploy/k8s/base/kustomization.yaml");
        Assert.Contains("network-policies", kust);
    }

    [Fact]
    public void Sec001_validation_script_exists()
    {
        var script = Path.Combine(ResolveRepoRoot(), "scripts", "sec001-port-scan.ps1");
        Assert.True(System.IO.File.Exists(script));
    }
}
