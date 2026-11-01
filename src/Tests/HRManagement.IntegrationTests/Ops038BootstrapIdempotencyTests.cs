using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payroll.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-38 (§1, §1.1): the hrm-bootstrap data steps are idempotent and least-privilege. On a fresh PostgreSQL the
/// bootstrap's own SQL (deploy/bootstrap/ensure-databases.sql, ensure-roles.sql, ensure-backup-role.sql) runs exactly as
/// bootstrap.sh runs it (psql, passwords through the environment), the HR.Core and Payroll migrations run as their
/// <c>*_migrator</c> role followed by TEN-002C, and a second run changes nothing. The last test runs the real hr-migrate
/// image (HRM_MIGRATE_IMAGE, e.g. hr-migrate:local) end to end against PostgreSQL and RabbitMQ, twice.
/// </summary>
public sealed class Ops038BootstrapFixture : IAsyncLifetime
{
    public const string BootstrapDir = "/opt/hrm-bootstrap";
    public const string SqlDir = "/opt/hrm-sql";

    private PostgreSqlContainer? _pg;
    private readonly Dictionary<string, string> _passwords = new(StringComparer.Ordinal);

    public PostgreSqlContainer Pg => _pg ?? throw new InvalidOperationException("Container not started");
    public string RepoRoot { get; } = FindRepoRoot();
    public IReadOnlyList<RoleLine> Roles { get; private set; } = [];
    public BootstrapRun? FirstRun { get; private set; }

    public sealed record RoleLine(string Bundle, string RoleBase, string Database, string PasswordPrefix);

    public sealed record BootstrapRun(IReadOnlyDictionary<string, int> Applied, IReadOnlyDictionary<string, long> PgClass, long Roles);

    public async Task InitializeAsync()
    {
        if (!RealInfra.IsEnabled)
            return;

        Roles = System.IO.File.ReadAllLines(Path.Combine(RepoRoot, "deploy", "bootstrap", "postgres-roles.conf"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('|'))
            .Select(p => new RoleLine(p[0], p[1], p[2], p[3]))
            .ToList();
        foreach (var r in Roles)
        {
            _passwords[$"{r.PasswordPrefix}_APP_PASSWORD"] = NewPassword();
            _passwords[$"{r.PasswordPrefix}_MIGRATOR_PASSWORD"] = NewPassword();
        }
        _passwords["POSTGRES_BACKUP_PASSWORD"] = NewPassword();

        _pg = new PostgreSqlBuilder(RealInfra.PostgresImage)
            .WithResourceMapping(new DirectoryInfo(Path.Combine(RepoRoot, "deploy", "bootstrap")), BootstrapDir)
            .WithResourceMapping(new DirectoryInfo(Path.Combine(RepoRoot, "scripts", "sql")), SqlDir)
            .Build();
        await _pg.StartAsync();
        FirstRun = await RunBootstrapAsync();
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    public string Password(string variable) => _passwords[variable];

    public void SetPassword(string variable, string value) => _passwords[variable] = value;

    public static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    /// <summary>Steps 2-5 of bootstrap.sh; migrations for HR.Core and Payroll (the others are the same code path).</summary>
    public async Task<BootstrapRun> RunBootstrapAsync()
    {
        await PsqlAsync("postgres", $"-f {BootstrapDir}/ensure-databases.sql");
        foreach (var r in Roles)
        {
            await PsqlAsync("postgres", $"-v role_base={r.RoleBase} -v db={r.Database} -f {BootstrapDir}/ensure-roles.sql",
                ("HRM_APP_PASSWORD", Password($"{r.PasswordPrefix}_APP_PASSWORD")),
                ("HRM_MIGRATOR_PASSWORD", Password($"{r.PasswordPrefix}_MIGRATOR_PASSWORD")));
        }

        var applied = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["hr_core_db"] = await MigrateAsync<HRCoreDbContext>("hr_core", "hr_core_db", "POSTGRES_HR_CORE",
                o => new HRCoreDbContext(o)),
            ["payroll_db"] = await MigrateAsync<PayrollDbContext>("payroll", "payroll_db", "POSTGRES_PAYROLL",
                o => new PayrollDbContext(o)),
        };

        await PsqlAsync("postgres", $"-f {BootstrapDir}/ensure-backup-role.sql",
            ("HRM_BACKUP_PASSWORD", Password("POSTGRES_BACKUP_PASSWORD")));

        var pgClass = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var db in new[] { "hr_core_db", "payroll_db" })
            pgClass[db] = await ScalarAsync<long>(AdminConnection(db), "SELECT count(*) FROM pg_class");
        var roles = await ScalarAsync<long>(AdminConnection("postgres"), "SELECT count(*) FROM pg_roles");
        return new BootstrapRun(applied, pgClass, roles);
    }

    private async Task<int> MigrateAsync<TContext>(string roleBase, string db, string prefix,
        Func<DbContextOptions<TContext>, TContext> create) where TContext : DbContext
    {
        var cs = Connection(db, $"{roleBase}_migrator", Password($"{prefix}_MIGRATOR_PASSWORD"));
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(TContext).Assembly.GetName().Name))
            .Options;
        await using var context = create(options);
        var before = (await context.Database.GetAppliedMigrationsAsync()).Count();
        await context.Database.MigrateAsync();
        var after = (await context.Database.GetAppliedMigrationsAsync()).Count();

        // TEN-002C as the table owner, exactly as bootstrap.sh does after each bundle.
        await PsqlAsync(db, $"-f {SqlDir}/ten002c-harden-tenant-rls.sql", user: $"{roleBase}_migrator");
        return after - before;
    }

    public async Task PsqlAsync(string database, string arguments, params (string Name, string Value)[] environment) =>
        await PsqlAsync(database, arguments, "postgres", environment);

    public async Task PsqlAsync(string database, string arguments, string user, params (string Name, string Value)[] environment)
    {
        // Lab passwords of this throw-away container only; bootstrap.sh passes them to psql the same way (environment).
        var env = string.Join(' ', environment.Select(e => $"{e.Name}='{e.Value}'"));
        var result = await Pg.ExecAsync(
            ["sh", "-c", $"{env} psql -X -q -v ON_ERROR_STOP=1 -U {user} -d {database} {arguments}"]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"psql {arguments} as {user} on {database} failed: {result.Stderr}");
    }

    public string Connection(string database, string user, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = Pg.Hostname,
            Port = Pg.GetMappedPublicPort(5432),
            Database = database,
            Username = user,
            Password = password,
            Pooling = false,
        }.ConnectionString;

    public string AdminConnection(string database) =>
        new NpgsqlConnectionStringBuilder(Pg.GetConnectionString()) { Database = database, Pooling = false }.ConnectionString;

    public static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (HRManagement.slnx) not found.");
    }
}

public sealed class Ops038BootstrapIdempotencyTests : IClassFixture<Ops038BootstrapFixture>
{
    private const string Skip = "Set RUN_TESTCONTAINERS=true and start Docker to run this test.";
    private readonly Ops038BootstrapFixture _f;

    public Ops038BootstrapIdempotencyTests(Ops038BootstrapFixture fixture) => _f = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task First_run_creates_the_16_databases_and_every_role_and_migrates_as_the_migrator()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        Assert.True(_f.FirstRun!.Applied["hr_core_db"] > 0);
        Assert.True(_f.FirstRun.Applied["payroll_db"] > 0);
        Assert.Equal(16L, await Ops038BootstrapFixture.ScalarAsync<long>(_f.AdminConnection("postgres"),
            "SELECT count(*) FROM pg_database WHERE datname IN ('keycloak_db','gateway_audit_db','hr_core_db','hr_contracts_db','hr_health_db','hr_movements_db','hr_training_db','payroll_db','idcard_db','archive_db','admin_db','it_infra_db','analytics_db','compliance_db','auth_directory_db','file_storage_db')"));
        foreach (var r in _f.Roles)
        {
            foreach (var role in new[] { $"{r.RoleBase}_app", $"{r.RoleBase}_migrator" })
            {
                Assert.Equal(1L, await Ops038BootstrapFixture.ScalarAsync<long>(_f.AdminConnection("postgres"),
                    $"SELECT count(*) FROM pg_roles WHERE rolname = '{role}' AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole"));
            }
        }

        // Every relation of the migrated databases belongs to the migrator: the application role owns nothing (TEN-002C).
        Assert.Equal(0L, await Ops038BootstrapFixture.ScalarAsync<long>(_f.AdminConnection("hr_core_db"),
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) <> 'hr_core_migrator'"));
        Assert.Equal(0L, await Ops038BootstrapFixture.ScalarAsync<long>(_f.AdminConnection("payroll_db"),
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND pg_get_userbyid(c.relowner) <> 'payroll_migrator'"));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Second_run_applies_no_migration_and_changes_no_relation_or_role()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        var second = await _f.RunBootstrapAsync();

        Assert.Equal(0, second.Applied["hr_core_db"]);
        Assert.Equal(0, second.Applied["payroll_db"]);
        Assert.Equal(_f.FirstRun!.PgClass["hr_core_db"], second.PgClass["hr_core_db"]);
        Assert.Equal(_f.FirstRun.PgClass["payroll_db"], second.PgClass["payroll_db"]);
        Assert.Equal(_f.FirstRun.Roles, second.Roles);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Rotating_a_password_and_rerunning_takes_effect_and_the_old_password_is_refused()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        const string variable = "POSTGRES_PAYROLL_APP_PASSWORD";
        var old = _f.Password(variable);
        var rotated = Ops038BootstrapFixture.NewPassword();
        _f.SetPassword(variable, rotated);
        await _f.RunBootstrapAsync();

        Assert.Equal(1, await Ops038BootstrapFixture.ScalarAsync<int>(_f.Connection("payroll_db", "payroll_app", rotated), "SELECT 1"));
        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            Ops038BootstrapFixture.ScalarAsync<int>(_f.Connection("payroll_db", "payroll_app", old), "SELECT 1"));
        Assert.Equal("28P01", refused.SqlState);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Application_role_has_no_ddl_and_cannot_reach_another_service_database()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        var app = _f.Connection("hr_core_db", "hr_core_app", _f.Password("POSTGRES_HR_CORE_APP_PASSWORD"));
        var ddl = await Assert.ThrowsAsync<PostgresException>(() =>
            Ops038BootstrapFixture.ScalarAsync<int>(app, "CREATE TABLE ops038_probe (id int); SELECT 1"));
        Assert.Equal("42501", ddl.SqlState);

        // DML on its own tables works (the default privileges of the migrator reached every migrated table).
        Assert.True(await Ops038BootstrapFixture.ScalarAsync<long>(app, "SELECT count(*) FROM \"__EFMigrationsHistory\"") > 0);

        var crossDb = await Assert.ThrowsAsync<PostgresException>(() =>
            Ops038BootstrapFixture.ScalarAsync<int>(
                _f.Connection("payroll_db", "hr_core_app", _f.Password("POSTGRES_HR_CORE_APP_PASSWORD")), "SELECT 1"));
        Assert.Equal("42501", crossDb.SqlState);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Backup_role_reads_every_database_but_cannot_write()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        var password = _f.Password("POSTGRES_BACKUP_PASSWORD");
        Assert.True(await Ops038BootstrapFixture.ScalarAsync<long>(
            _f.Connection("hr_core_db", "hrm_backup", password), "SELECT count(*) FROM \"__EFMigrationsHistory\"") > 0);
        Assert.Equal(1, await Ops038BootstrapFixture.ScalarAsync<int>(_f.Connection("keycloak_db", "hrm_backup", password), "SELECT 1"));
        Assert.Equal(1L, await Ops038BootstrapFixture.ScalarAsync<long>(_f.AdminConnection("postgres"),
            "SELECT count(*) FROM pg_roles WHERE rolname = 'hrm_backup' AND rolbypassrls AND NOT rolsuper"));

        var write = await Assert.ThrowsAsync<PostgresException>(() =>
            Ops038BootstrapFixture.ScalarAsync<int>(_f.Connection("hr_core_db", "hrm_backup", password),
                "DELETE FROM \"__EFMigrationsHistory\" WHERE false; SELECT 1"));
        Assert.Equal("42501", write.SqlState);
    }

    /// <summary>
    /// The real image: bootstrap.sh with the 15 self-contained bundles, against fresh PostgreSQL and RabbitMQ, twice. The
    /// second run reports 0 migrations applied. Needs HRM_MIGRATE_IMAGE (the image is built by
    /// <c>docker build -f docker/Migrator.Dockerfile</c>; CI does not build it for the test run).
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_hr_migrate_image_bootstraps_empty_volumes_and_a_second_run_applies_nothing()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var image = Environment.GetEnvironmentVariable("HRM_MIGRATE_IMAGE");
        Xunit.Skip.If(string.IsNullOrWhiteSpace(image), "Set HRM_MIGRATE_IMAGE (e.g. hr-migrate:local) to run the image end to end.");

        await using var network = new NetworkBuilder().Build();
        await network.CreateAsync();
        await using var pg = new PostgreSqlBuilder(RealInfra.PostgresImage)
            .WithNetwork(network).WithNetworkAliases("postgresql")
            .Build();
        var rabbitImage = Environment.GetEnvironmentVariable("HRM_RABBITMQ_IMAGE") is { Length: > 0 } r ? r : RealInfra.RabbitImage;
        await using var mq = new RabbitMqBuilder(rabbitImage)
            .WithNetwork(network).WithNetworkAliases("rabbitmq")
            .WithUsername("ops038_admin").WithPassword(Ops038BootstrapFixture.NewPassword())
            .Build();
        await Task.WhenAll(pg.StartAsync(), mq.StartAsync());

        var env = new Dictionary<string, string>
        {
            ["PGHOST"] = "postgresql",
            ["PGPORT"] = "5432",
            ["POSTGRES_USER"] = "postgres",
            ["POSTGRES_PASSWORD"] = "postgres",
            ["RABBITMQ_DEFAULT_USER"] = "ops038_admin",
            ["RABBITMQ_DEFAULT_PASS"] = new RabbitMqConnection(mq).Password,
            ["POSTGRES_BACKUP_PASSWORD"] = Ops038BootstrapFixture.NewPassword(),
        };
        foreach (var line in _f.Roles)
        {
            env[$"{line.PasswordPrefix}_APP_PASSWORD"] = Ops038BootstrapFixture.NewPassword();
            env[$"{line.PasswordPrefix}_MIGRATOR_PASSWORD"] = Ops038BootstrapFixture.NewPassword();
        }
        foreach (var variable in Ops038RabbitUsers.PasswordVariables(_f.RepoRoot))
            env[variable] = Ops038BootstrapFixture.NewPassword();

        var first = await RunImageAsync(image!, network, env);
        Assert.True(first.ExitCode == 0, first.Logs);
        Assert.Contains("step 8: result", first.Logs);
        Assert.DoesNotContain(env["POSTGRES_HR_CORE_APP_PASSWORD"], first.Logs);
        var pgClass = await Ops038BootstrapFixture.ScalarAsync<long>(
            new NpgsqlConnectionStringBuilder(pg.GetConnectionString()) { Database = "hr_core_db", Pooling = false }.ConnectionString,
            "SELECT count(*) FROM pg_class");

        var second = await RunImageAsync(image!, network, env);
        Assert.True(second.ExitCode == 0, second.Logs);
        Assert.Contains("\"migrationsAppliedTotal\": 0", second.Logs);
        Assert.Equal(15, System.Text.RegularExpressions.Regex.Matches(second.Logs, "migrated by [a-z_]+_migrator: 0 migrations applied").Count);
        Assert.Equal(pgClass, await Ops038BootstrapFixture.ScalarAsync<long>(
            new NpgsqlConnectionStringBuilder(pg.GetConnectionString()) { Database = "hr_core_db", Pooling = false }.ConnectionString,
            "SELECT count(*) FROM pg_class"));
        // Every generated secret (the fixed "postgres" administrator password of the test container is a common word).
        foreach (var (name, value) in env.Where(e => (e.Key.EndsWith("PASSWORD", StringComparison.Ordinal) || e.Key.EndsWith("_PASS", StringComparison.Ordinal)) && e.Value.Length >= 16))
        {
            Assert.False(first.Logs.Contains(value, StringComparison.Ordinal), $"the value of {name} was printed");
            Assert.False(second.Logs.Contains(value, StringComparison.Ordinal), $"the value of {name} was printed");
        }
    }

    private static async Task<(long ExitCode, string Logs)> RunImageAsync(string image, INetwork network, IReadOnlyDictionary<string, string> env)
    {
        var builder = new ContainerBuilder(image).WithNetwork(network).WithTmpfsMount("/tmp");
        foreach (var (k, v) in env)
            builder = builder.WithEnvironment(k, v);
        await using var container = builder.Build();
        await container.StartAsync();
        // Waits for the one-shot container to exit (the bootstrap ends by itself, with 0 or an error).
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var exit = await container.GetExitCodeAsync(timeout.Token);
        var (stdout, stderr) = await container.GetLogsAsync();
        return (exit, stdout + stderr);
    }

    private sealed class RabbitMqConnection(RabbitMqContainer container)
    {
        public string Password { get; } = new Uri(container.GetConnectionString()).UserInfo.Split(':')[1];
    }
}

/// <summary>Password variables of deploy/bootstrap/rabbitmq-users.json (PLAN-38 §2), when the matrix exists.</summary>
public static class Ops038RabbitUsers
{
    public static IReadOnlyList<string> PasswordVariables(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "deploy", "bootstrap", "rabbitmq-users.json");
        if (!System.IO.File.Exists(path))
            return [];
        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
        return doc.RootElement.GetProperty("users").EnumerateArray()
            .Select(u => u.GetProperty("passwordEnv").GetString()!)
            .ToList();
    }
}
