using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace HRManagement.IntegrationTests;

/// <summary>Shared gate for all QA-003 Testcontainers suites.</summary>
public static class RealInfra
{
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS"), "true",
            StringComparison.OrdinalIgnoreCase);

    public const string PostgresImage = "postgres:16-alpine";
    public const string RabbitImage = "rabbitmq:3.13-management-alpine";
    public const string KeycloakImage = "quay.io/keycloak/keycloak:24.0";
    /// <summary>
    /// QA-034 (D-QA-7): the former pin minio/minio:RELEASE.2025-04-22T22-12-26Z can no longer be pulled. A vetted,
    /// digest-pinned image is supplied through HRM_MINIO_IMAGE; the tests that need it carry Category=ObjectStorage and
    /// are excluded from CI until PLAN-38 picks the image (tests/ci-exclusions.json, a dated obligation).
    /// </summary>
    public static string MinioImage =>
        Environment.GetEnvironmentVariable("HRM_MINIO_IMAGE") is { Length: > 0 } image
            ? image
            : throw new InvalidOperationException(
                "Set HRM_MINIO_IMAGE to a vetted, digest-pinned MinIO image to run Category=ObjectStorage tests (D-QA-7, PLAN-38).");
}

/// <summary>Safe DB/object names — avoids C# parsing `{Guid:N[..n]}` as range on the format specifier.</summary>
public static class TestDatabaseNames
{
    public static string Unique(string prefix, int maxLength = 63)
    {
        var name = prefix + Guid.NewGuid().ToString("N");
        return name.Length <= maxLength ? name : name[..maxLength];
    }

    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Container not started");

    public PostgreSqlContainer Container => _container
        ?? throw new InvalidOperationException("Container not started");

    public static bool IsEnabled => RealInfra.IsEnabled;

    /// <summary>Login role used by <see cref="CreateAppConnectionAsync"/>; never a superuser or owner.</summary>
    public const string AppRole = "hrm_rls_app";
    private const string AppRolePassword = "hrm_rls_app_pwd";

    public async Task<string> CreateDatabaseAsync(string databaseName)
    {
        if (_container is null)
            throw new InvalidOperationException("Container not started");

        await _container.ExecAsync(["psql", "-U", "postgres", "-c", $"CREATE DATABASE \"{databaseName}\";"]);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Connection string for a non-superuser, non-owner role on <paramref name="databaseName"/>.
    /// <para>
    /// RLS proofs must not run as <c>postgres</c>: superusers bypass row level security
    /// unconditionally, and a table's owner bypasses it unless FORCE is set. Asserting isolation
    /// on the default fixture connection would pass no matter what the policies say.
    /// </para>
    /// Call after the schema exists so the DML grants cover the migrated tables.
    /// </summary>
    public async Task<string> CreateAppConnectionAsync(string databaseName)
    {
        if (_container is null)
            throw new InvalidOperationException("Container not started");

        await _container.ExecAsync(
        [
            "psql", "-U", "postgres", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c",
            $"""
             DO $$ BEGIN
               IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                 CREATE ROLE {AppRole} LOGIN PASSWORD '{AppRolePassword}';
               END IF;
             END $$;
             ALTER ROLE {AppRole} NOSUPERUSER NOBYPASSRLS;
             """
        ]);

        await _container.ExecAsync(
        [
            "psql", "-U", "postgres", "-d", databaseName, "-v", "ON_ERROR_STOP=1", "-c",
            $"""
             GRANT CONNECT ON DATABASE "{databaseName}" TO {AppRole};
             GRANT USAGE ON SCHEMA public TO {AppRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole};
             """
        ]);

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName,
            Username = AppRole,
            Password = AppRolePassword
        };
        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        if (!IsEnabled) return;
        _container = new PostgreSqlBuilder(RealInfra.PostgresImage).Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public DbContextOptions<T> CreateOptions<T>(string? connectionString = null) where T : DbContext
    {
        return new DbContextOptionsBuilder<T>()
            .UseNpgsql(connectionString ?? ConnectionString)
            .Options;
    }
}

[CollectionDefinition("PostgreSql")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture> { }

/// <summary>RabbitMQ broker for outbox/inbox real-infra proofs (QA-003-C).</summary>
public class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    public static bool IsEnabled => RealInfra.IsEnabled;

    public string Hostname => _container?.Hostname
        ?? throw new InvalidOperationException("RabbitMQ not started");

    public int Port => _container is null
        ? throw new InvalidOperationException("RabbitMQ not started")
        : _container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

    public const string Username = "hrm_test";
    public const string Password = "hrm_test_pwd";

    public RabbitMqContainer Container => _container
        ?? throw new InvalidOperationException("RabbitMQ not started");

    public async Task InitializeAsync()
    {
        if (!IsEnabled) return;
        _container = new RabbitMqBuilder(RealInfra.RabbitImage)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("RabbitMq")]
public class RabbitMqCollection : ICollectionFixture<RabbitMqFixture> { }

/// <summary>PG + Rabbit shared for outbox/inbox suites.</summary>
public class PgRabbitFixture : IAsyncLifetime
{
    public PostgreSqlFixture Postgres { get; } = new();
    public RabbitMqFixture Rabbit { get; } = new();

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        await Rabbit.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await Rabbit.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}

[CollectionDefinition("PgRabbit")]
public class PgRabbitCollection : ICollectionFixture<PgRabbitFixture> { }

/// <summary>Keycloak with QA-003 test realm import.</summary>
public class KeycloakFixture : IAsyncLifetime
{
    private IContainer? _container;
    private string? _realmFileHostPath;

    public static bool IsEnabled => RealInfra.IsEnabled;

    public string BaseUrl => _container is null
        ? throw new InvalidOperationException("Keycloak not started")
        : $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}";

    public string Authority => $"{BaseUrl}/realms/hrmanagement-qa003";

    public string ClientId => "qa003-service";
    public string ClientSecret => "qa003-service-secret";
    public string WrongAudienceClientId => "qa003-wrong-audience";
    public string WrongAudienceClientSecret => "qa003-wrong-secret";

    public IContainer Container => _container
        ?? throw new InvalidOperationException("Keycloak not started");

    public async Task InitializeAsync()
    {
        if (!IsEnabled) return;

        var repoRoot = FindRepoRoot();
        var sourceRealm = Path.Combine(repoRoot, "docker", "keycloak", "realm-qa003-test.json");
        if (!System.IO.File.Exists(sourceRealm))
            throw new FileNotFoundException("QA-003 Keycloak realm file missing", sourceRealm);

        _realmFileHostPath = Path.Combine(Path.GetTempPath(), $"qa003-realm-{Guid.NewGuid():N}.json");
        System.IO.File.Copy(sourceRealm, _realmFileHostPath, overwrite: true);

        _container = new ContainerBuilder(RealInfra.KeycloakImage)
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
            .WithPortBinding(8080, true)
            .WithBindMount(_realmFileHostPath, "/opt/keycloak/data/import/realm-qa003-test.json")
            .WithCommand("start-dev", "--import-realm", "--http-port=8080")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPort(8080).ForPath("/realms/hrmanagement-qa003")))
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
        if (_realmFileHostPath is not null && System.IO.File.Exists(_realmFileHostPath))
            System.IO.File.Delete(_realmFileHostPath);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}

[CollectionDefinition("Keycloak")]
public class KeycloakCollection : ICollectionFixture<KeycloakFixture> { }

/// <summary>MinIO object storage for FILE-002 real path.</summary>
public class MinioFixture : IAsyncLifetime
{
    private MinioContainer? _container;

    public static bool IsEnabled => RealInfra.IsEnabled;

    public const string AccessKey = "hrminio";
    public const string SecretKey = "hrminio_secret_change_me";

    public string Endpoint => _container is null
        ? throw new InvalidOperationException("MinIO not started")
        : $"{_container.Hostname}:{_container.GetMappedPublicPort(MinioBuilder.MinioPort)}";

    public MinioContainer Container => _container
        ?? throw new InvalidOperationException("MinIO not started");

    public async Task InitializeAsync()
    {
        if (!IsEnabled) return;
        _container = new MinioBuilder(RealInfra.MinioImage)
            .WithUsername(AccessKey)
            .WithPassword(SecretKey)
            .Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("Minio")]
public class MinioCollection : ICollectionFixture<MinioFixture> { }

/// <summary>PG + MinIO for file scan suites.</summary>
public class PgMinioFixture : IAsyncLifetime
{
    public PostgreSqlFixture Postgres { get; } = new();
    public MinioFixture Minio { get; } = new();

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        await Minio.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await Minio.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}

[CollectionDefinition("PgMinio")]
public class PgMinioCollection : ICollectionFixture<PgMinioFixture> { }
