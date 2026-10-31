using System.Text.Json;
using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-36 (G-OPS-01): the production compose chain boots by itself. Text-based checks on the compose files; the
/// rendered chains are checked by scripts/vps/lint-compose.ps1.
/// </summary>
public class Ops036ComposeLintTests
{
    private static readonly string[] Modules =
    [
        "hr-core", "hr-contracts", "hr-health", "hr-movements", "hr-training", "payroll", "idcard",
        "records-archive", "admin-logistics", "it-infra", "dev-analytics", "compliance", "auth-directory", "file-storage"
    ];

    private static readonly string[] ObservabilityServices =
    [
        "seq", "tempo", "otel-collector", "prometheus", "alertmanager", "alert-sink", "blackbox-exporter", "grafana",
        "rabbitmq-exporter", "postgres-exporter"
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
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(ResolveRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The text of one top-level service block (two-space indented key up to the next one).</summary>
    private static string ServiceBlock(string compose, string service)
    {
        var match = Regex.Match(
            compose,
            $@"(?ms)^  {Regex.Escape(service)}:\r?\n(.*?)(?=^  [a-z][a-z0-9-]*:\r?\n|^[a-z])",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"service {service} not found");
        return match.Groups[1].Value;
    }

    [Fact]
    public void Production_file_defines_no_observability_service()
    {
        var prod = Read("docker-compose.production.yml");
        foreach (var svc in ObservabilityServices)
            Assert.DoesNotMatch($@"(?m)^  {Regex.Escape(svc)}:\s*$", prod);

        var overrides = Read("docker-compose.observability.production.yml");
        foreach (var svc in ObservabilityServices)
            Assert.Matches($@"(?m)^  {Regex.Escape(svc)}:\s*$", overrides);
    }

    [Fact]
    public void Gateway_sets_public_settings_explicitly()
    {
        var gateway = ServiceBlock(Read("docker-compose.production.yml"), "hr-gateway");
        Assert.Contains("Keycloak__PublicAuthority: https://${SSO_DOMAIN:?", gateway);
        Assert.Contains("HrAuth__BehindGateway: \"false\"", gateway);
        Assert.Contains("AllowedHosts: ${APP_DOMAIN:?", gateway);
        for (var i = 0; i < Modules.Length; i++)
            Assert.Contains($"Gateway__EnabledModules__{i}: {Modules[i]}", gateway);
        Assert.DoesNotContain($"Gateway__EnabledModules__{Modules.Length}:", gateway);
    }

    [Fact]
    public void Application_anchor_trusts_internal_authority_and_requires_caller_proof()
    {
        var prod = Read("docker-compose.production.yml");
        var anchor = Regex.Match(prod, @"(?ms)^x-app-production-env: &app-production-env\r?\n(.*?)(?=^\S)").Groups[1].Value;
        Assert.Contains("Keycloak__RequireHttpsMetadata: \"false\"", anchor);
        Assert.Contains("Keycloak__AdditionalValidIssuers: https://${SSO_DOMAIN:?", anchor);
        Assert.Contains("AllowedHosts: \"*\"", anchor);
        Assert.Contains("GatewayCallerProof__Secret: ${GATEWAY_CALLER_PROOF_SECRET:?", anchor);
        Assert.Contains("DataProtection__KeyRingPath: /var/hrm/dpkeys", anchor);
        Assert.Contains("Deployment__RequireLicenseKey: ${HRM_REQUIRE_LICENSE:-false}", anchor);
    }

    [Theory]
    [InlineData("localhost:8088")]
    [InlineData("ASPNETCORE_ENVIRONMENT: Development")]
    [InlineData("YOUR_")]
    [InlineData("USE_ENV")]
    [InlineData("dev-svc-secret-local-only")]
    public void Production_file_carries_no_development_value(string forbidden)
    {
        Assert.DoesNotContain(forbidden, Read("docker-compose.production.yml"));
    }

    [Fact]
    public void Every_application_restarts_and_waits_for_volume_ownership()
    {
        var prod = Read("docker-compose.production.yml");
        foreach (var service in Modules.Select(m => m + "-web").Append("hr-gateway"))
        {
            var block = ServiceBlock(prod, service);
            Assert.Contains("*app-runtime", block);
            Assert.Matches(@"(?ms)depends_on:.*?volume-init:\s*\r?\n\s*condition: service_completed_successfully", block);
            Assert.Matches(@"(?ms)depends_on:.*?postgresql:\s*\r?\n\s*condition: service_healthy", block);
            Assert.DoesNotMatch(@"(?m)^      minio:", block);
        }

        Assert.Matches(@"(?ms)^x-app-runtime: &app-runtime\r?\n  restart: unless-stopped", prod);
        var init = ServiceBlock(prod, "volume-init");
        Assert.Contains("chown -R 1654:1654", init);
        Assert.Contains("network_mode: none", init);
    }

    [Fact]
    public void File_storage_runs_on_the_local_provider_and_minio_is_optional()
    {
        var prod = Read("docker-compose.production.yml");
        var fileStorage = ServiceBlock(prod, "file-storage-web");
        Assert.Contains("FileStorage__Provider: ${HRM_FILE_PROVIDER:-Local}", fileStorage);
        Assert.Contains("file_storage_data:/var/hrm/files", fileStorage);

        var minio = ServiceBlock(prod, "minio");
        Assert.Contains("profiles: !override [\"minio\"]", minio);
        Assert.Contains("image: ${MINIO_IMAGE:-}", minio);
        Assert.Contains("profiles: [\"minio\"]", ServiceBlock(prod, "minio-backup"));
    }

    [Fact]
    public void Every_module_mounts_its_own_data_protection_volume()
    {
        var prod = Read("docker-compose.production.yml");
        foreach (var module in Modules)
        {
            var volume = "dp_" + module.Replace('-', '_');
            Assert.Contains($"- {volume}:/var/hrm/dpkeys", ServiceBlock(prod, module + "-web"));
            Assert.Matches($@"(?m)^  {volume}:\s*$", prod);
        }
    }

    [Fact]
    public void File_storage_has_production_settings_without_placeholders()
    {
        var json = Read("src/Services/FileStorage/File.Storage.Web/appsettings.Production.json");
        Assert.DoesNotMatch("YOUR_|USE_ENV|USE_SECRET_MANAGER|CHANGE_ME", json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("GatewayCallerProof").GetProperty("Enforce").GetBoolean());
        Assert.False(root.GetProperty("GatewayCallerProof").TryGetProperty("Secret", out _));
        Assert.False(root.GetProperty("Database").GetProperty("RunMigrationsAtStartup").GetBoolean());
        Assert.True(root.GetProperty("FileStorage").GetProperty("ClamAvRequired").GetBoolean());
    }

    [Fact]
    public void Single_host_publishes_only_the_edge_and_seq_on_loopback()
    {
        var vps = Read("docker-compose.vps.yml");
        var edge = ServiceBlock(vps, "edge");
        Assert.Matches(@"image: caddy:\d+\.\d+\.\d+", edge);
        Assert.Contains("- \"443:443\"", edge);
        Assert.Contains("- \"443:443/udp\"", edge);
        Assert.Contains("ipv4_address: 172.28.0.10", edge);
        Assert.Contains("- \"127.0.0.1:5380:80\"", ServiceBlock(vps, "seq"));
        Assert.Contains("ports: !override []", ServiceBlock(vps, "hr-gateway"));
        Assert.Contains("ports: !override []", ServiceBlock(vps, "keycloak"));
        Assert.Equal(3, Regex.Matches(vps, @"^\s+- ""[^""]*:\d+(/udp)?""", RegexOptions.Multiline).Count);

        var gateway = ServiceBlock(vps, "hr-gateway");
        Assert.Contains("ForwardedHeaders__KnownProxies: 172.28.0.10", gateway);
        Assert.Contains("ForwardedHeaders__ForwardLimit: \"1\"", gateway);
        Assert.Contains("RateLimiting__PermitLimitPerMinute: \"300\"", gateway);
        Assert.Contains("RateLimiting__AuthenticatedPermitLimitPerMinute: \"600\"", gateway);
        Assert.Contains("ip_range: 172.28.0.128/25", vps);
    }

    [Theory]
    [InlineData("hr-core-web", "1024M")]
    [InlineData("payroll-web", "768M")]
    [InlineData("hr-contracts-web", "640M")]
    [InlineData("hr-gateway", "512M")]
    [InlineData("file-storage-web", "512M")]
    [InlineData("auth-directory-web", "384M")]
    [InlineData("postgresql", "2048M")]
    [InlineData("keycloak", "1024M")]
    [InlineData("clamav", "2048M")]
    [InlineData("rabbitmq", "512M")]
    [InlineData("seq", "768M")]
    [InlineData("edge", "128M")]
    public void Single_host_memory_budget(string service, string limit)
    {
        Assert.Matches($@"(?ms)limits:\s*\r?\n\s*memory: {limit}", ServiceBlock(Read("docker-compose.vps.yml"), service));
    }

    [Fact]
    public void Single_host_turns_telemetry_off_and_allows_slow_migrations()
    {
        var vps = Read("docker-compose.vps.yml");
        Assert.Contains("DOTNET_gcServer: \"0\"", vps);
        Assert.Contains("OpenTelemetry__Enabled: ${HRM_OTEL_ENABLED:-false}", vps);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT: ${HRM_OTEL_ENDPOINT:-}", vps);
        Assert.Contains("start_period: 240s", vps);
        Assert.Contains("./deploy/vps/clamav/clamd.conf:/etc/clamav/clamd.conf:ro", vps);
        Assert.Contains("ConcurrentDatabaseReload no", Read("deploy/vps/clamav/clamd.conf"));
    }

    [Fact]
    public void Keycloak_runs_behind_the_edge_with_proxy_headers()
    {
        var keycloak = ServiceBlock(Read("docker-compose.production.yml"), "keycloak");
        Assert.DoesNotContain("KC_PROXY:", keycloak);
        Assert.Contains("KC_PROXY_HEADERS: xforwarded", keycloak);
        Assert.Contains("KC_HOSTNAME: ${SSO_DOMAIN:?", keycloak);
        Assert.Contains("KC_HOSTNAME_STRICT_BACKCHANNEL: \"false\"", keycloak);
        Assert.Contains("command: [\"start\", \"--import-realm\"]", keycloak);
        Assert.Contains("restart: unless-stopped", keycloak);
    }

    [Fact]
    public void Postgresql_readiness_needs_the_final_server_on_tcp()
    {
        var postgres = ServiceBlock(Read("docker-compose.production.yml"), "postgresql");
        Assert.Contains("cat /proc/1/comm", postgres);
        Assert.Contains("pg_isready -h 127.0.0.1", postgres);
    }

    [Fact]
    public void Caddyfile_uses_tls_alpn_only_and_guards_operations_paths()
    {
        var caddy = Read("deploy/vps/Caddyfile");
        Assert.Contains("auto_https disable_redirects", caddy);
        Assert.Contains("disable_http_challenge", caddy);
        Assert.DoesNotContain("disable_tlsalpn_challenge", caddy);
        Assert.Contains("{$HRM_ACME_CA", caddy);
        Assert.Contains("max_size 20MB", caddy);
        Assert.Contains("path /health* /metrics* /alive* /ready*", caddy);
        Assert.Contains("respond @ops 404", caddy);
        Assert.Contains("path /admin* /realms/master*", caddy);
        Assert.Contains("respond @kcadmin 403", caddy);
        Assert.Contains("not remote_ip {$ADMIN_ALLOWED_CIDRS}", caddy);
        Assert.Contains("reverse_proxy hr-gateway:8080", caddy);
        Assert.Contains("reverse_proxy keycloak:8080", caddy);
        Assert.Contains("Strict-Transport-Security", caddy);
        // No trusted_proxies directive: a client-supplied X-Forwarded-For is never passed on as the client address.
        Assert.DoesNotMatch(@"(?m)^\s*trusted_proxies\b", caddy);
    }

    [Fact]
    public void Demo_mode_and_local_variant_files_exist()
    {
        var demo = Read("docker-compose.vps-demo.yml");
        Assert.Contains("Deployment__Mode: demo", demo);
        Assert.Contains("Seed__Profile: Demo", demo);
        Assert.Contains("Database__RunMigrationsAtStartup: \"true\"", demo);
        Assert.Equal(15, Regex.Matches(demo, @"ConnectionStrings__\w+: Host=postgresql").Count);
        Assert.Contains("CADDY_SITE_TLS: tls internal", Read("docker-compose.vps-local.yml"));
        Assert.Contains("_DeploymentBanner", Read("src/Shared/HRManagement.Web.Shared/Views/Shared/_HrSharedLayout.cshtml"));
        Assert.Contains("_DeploymentBanner", Read("src/Gateway/HRManagement.Gateway/Views/Shared/_Layout.cshtml"));
    }

    [Fact]
    public void Lint_script_is_ascii_and_covers_the_rules()
    {
        var lint = Read("scripts/vps/lint-compose.ps1");
        Assert.All(lint, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4} in lint-compose.ps1"));
        Assert.Contains("no memory limit", lint);
        Assert.Contains("latest", lint);
        Assert.Contains("no restart policy", lint);
        Assert.Contains("MINIO_ROOT_PASSWORD", lint);
        Assert.Contains("single host allows only edge 443 and Seq on loopback", lint);
    }

    [Fact]
    public void Deploy_workflow_enables_the_full_stack_profile()
    {
        var deploy = Read(".github/workflows/deploy.yml");
        var invocations = Regex.Matches(deploy, @"docker compose \\\r?\n");
        Assert.NotEmpty(invocations);
        Assert.Equal(invocations.Count, Regex.Matches(deploy, @"docker compose \\\r?\n\s*--profile full-stack \\").Count);
    }
}
