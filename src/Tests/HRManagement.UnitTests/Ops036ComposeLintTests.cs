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
    public void Deploy_workflow_enables_the_full_stack_profile()
    {
        var deploy = Read(".github/workflows/deploy.yml");
        var invocations = Regex.Matches(deploy, @"docker compose \\\r?\n");
        Assert.NotEmpty(invocations);
        Assert.Equal(invocations.Count, Regex.Matches(deploy, @"docker compose \\\r?\n\s*--profile full-stack \\").Count);
    }
}
