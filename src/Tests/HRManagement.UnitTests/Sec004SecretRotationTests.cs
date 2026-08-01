using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>SEC-004: production fail-closed secrets, MinIO least-privilege, build context.</summary>
public class Sec004SecretRotationTests
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
    public void Sec004_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "SEC_004_SECRET_ROTATION.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "sec004-secret-scan.ps1")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "sec004-validate.ps1")));
    }

    [Fact]
    public void Production_compose_uses_fail_closed_for_critical_secrets()
    {
        var prod = Read("docker-compose.production.yml");
        Assert.Contains("MINIO_ROOT_USER:?MINIO_ROOT_USER required", prod);
        Assert.Contains("MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD required", prod);
        Assert.Contains("GRAFANA_ADMIN_PASSWORD:?GRAFANA_ADMIN_PASSWORD required", prod);
        Assert.Contains("RABBITMQ_DEFAULT_PASS:?RABBITMQ_DEFAULT_PASS required", prod);
        Assert.Contains("KEYCLOAK_HR_GATEWAY_CLIENT_SECRET:?KEYCLOAK_HR_GATEWAY_CLIENT_SECRET required", prod);
        Assert.Contains("MINIO_FILESTORAGE_ACCESS_KEY:?MINIO_FILESTORAGE_ACCESS_KEY required", prod);
    }

    [Fact]
    public void Production_compose_has_no_known_minio_defaults()
    {
        var prod = Read("docker-compose.production.yml");
        Assert.DoesNotContain("hrminio", prod, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hrminio_secret_change_me", prod, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"FileStorage__AccessKey:.*MINIO_ROOT", prod);
    }

    [Fact]
    public void Dockerignore_excludes_env_and_secrets()
    {
        var ignore = Read(".dockerignore");
        Assert.Contains(".env", ignore);
        Assert.Contains(".env.*", ignore);
        Assert.Contains("secrets/", ignore);
        Assert.Contains("*.secret", ignore);
    }

    [Fact]
    public void Rotation_runbook_covers_db_rabbitmq_keycloak_minio()
    {
        var doc = Read("docs/SEC_004_SECRET_ROTATION.md");
        Assert.Contains("PostgreSQL", doc);
        Assert.Contains("RabbitMQ", doc);
        Assert.Contains("Keycloak", doc);
        Assert.Contains("MinIO", doc);
        Assert.Contains("zero-downtime", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Service_identity_secrets_fail_closed_in_production()
    {
        var prod = Read("docker-compose.production.yml");
        Assert.DoesNotMatch(@"ServiceIdentity__ClientSecret: \$\{SVC_[A-Z_]+:-\}", prod);
        Assert.Contains("SVC_HR_CORE_CLIENT_SECRET:?SVC_HR_CORE_CLIENT_SECRET required", prod);
    }
}
