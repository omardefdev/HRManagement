using System.Diagnostics.Metrics;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Privacy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-39 (section 4, D-39-4): the HR.Core key-ring canary on a real PostgreSQL whose Employees table is under tenant
/// row level security (as TEN-002C makes it) and read by a role without BYPASSRLS. A national ID protected with ring A:
/// a start with ring B is refused naming HR.Core and the path; an empty ring is refused without creating a key; the
/// reset switch lets the start go on with Critical and the counter; ring A passes; no protected data passes.
/// </summary>
public sealed class Ops039PiiKeyRingCanaryTests : IAsyncLifetime
{
    private const string Skip = "Set RUN_TESTCONTAINERS=true and start Docker to run this test.";
    private const string AppPassword = "ops039-lab-only-app-password";
    private static readonly Guid Tenant = Guid.Parse("a0000001-0000-4000-8000-000000000001");

    private PostgreSqlContainer? _pg;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ops039-canary-" + Guid.NewGuid().ToString("N"));
    private string RingA => Path.Combine(_root, "ring-a");
    private string RingB => Path.Combine(_root, "ring-b");
    private string EmptyRing => Path.Combine(_root, "ring-empty");

    public async Task InitializeAsync()
    {
        if (!RealInfra.IsEnabled) return;
        Directory.CreateDirectory(EmptyRing);
        _pg = new PostgreSqlBuilder(RealInfra.PostgresImage).WithDatabase("hr_core_db").Build();
        await _pg.StartAsync();

        var protectedId = Provider(RingA).CreateProtector(PiiFieldEncryptionService.Purpose).Protect("09012345678");
        _ = Provider(RingB).CreateProtector(PiiFieldEncryptionService.Purpose).Protect("ring b exists");
        await ExecAsync(Admin(), $"""
            CREATE TABLE "Tenants" ("Id" uuid PRIMARY KEY);
            CREATE TABLE "Employees" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "NationalId" varchar(512));
            ALTER TABLE "Employees" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "Employees" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "Employees"
                USING ("TenantId"::text = NULLIF(current_setting('app.current_tenant', true), ''));
            CREATE ROLE hr_core_app LOGIN NOBYPASSRLS PASSWORD '{AppPassword}';
            GRANT SELECT ON "Tenants", "Employees" TO hr_core_app;
            INSERT INTO "Tenants" VALUES ('{Tenant}');
            INSERT INTO "Employees" VALUES (gen_random_uuid(), '{Tenant}', '{protectedId}');
            """);
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Admin(string database = "hr_core_db") =>
        new NpgsqlConnectionStringBuilder(_pg!.GetConnectionString()) { Database = database, Pooling = false }.ConnectionString;

    private string App() =>
        new NpgsqlConnectionStringBuilder(_pg!.GetConnectionString()) { Username = "hr_core_app", Password = AppPassword, Pooling = false }.ConnectionString;

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static IDataProtectionProvider Provider(string ring) =>
        DataProtectionProvider.Create(new DirectoryInfo(ring), b => b.SetApplicationName("HR.Core"));

    private static EmployeePiiKeyRingCanary Canary(string ring, string connectionString, CapturingLogger logger,
        string environment = "Production", bool allowReset = false, bool migrationsAtStartup = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:HRCoreDb"] = connectionString,
            [DataProtectionExtensions.AllowKeyRingResetKey] = allowReset ? "true" : null,
            ["Database:RunMigrationsAtStartup"] = migrationsAtStartup ? "true" : "false",
        }).Build();
        var provider = new ServiceCollection().AddDataProtection().SetApplicationName("HR.Core")
            .PersistKeysToFileSystem(new DirectoryInfo(ring)).Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();
        return new EmployeePiiKeyRingCanary(configuration, new Host(environment), provider,
            new HrDataProtectionRegistration("HR.Core", ring, null), logger);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_ring_that_protected_the_data_passes_also_for_a_role_under_row_level_security()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var logger = new CapturingLogger();
        await Canary(RingA, App(), logger).RunAsync(CancellationToken.None);
        Assert.Contains(logger.Entries, e => e.Message.Contains("decrypts with the key ring", StringComparison.Ordinal));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Another_ring_is_refused_naming_the_service_and_the_path_never_the_value()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        foreach (var connection in new[] { Admin(), App() })
        {
            var logger = new CapturingLogger();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Canary(RingB, connection, logger).RunAsync(CancellationToken.None));
            Assert.Contains("HR.Core", error.Message);
            Assert.Contains(RingB, error.Message);
            Assert.Contains("حلقة مفاتيح حماية البيانات لا تطابق البيانات المخزنة", error.Message);
            Assert.DoesNotContain("09012345678", error.Message);
            Assert.DoesNotContain("CfDJ8", error.Message);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Critical);
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task An_empty_ring_over_protected_data_is_refused_without_creating_a_key()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Canary(EmptyRing, App(), new CapturingLogger()).RunAsync(CancellationToken.None));
        Assert.Contains("holds no key", error.Message);
        Assert.Empty(Directory.GetFiles(EmptyRing));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_reset_switch_lets_the_start_go_on_with_critical_and_the_counter()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        long resets = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "HRManagement.DataProtection" && instrument.Name == "dataprotection_key_ring_reset_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref resets, value));
        listener.Start();

        var logger = new CapturingLogger();
        await Canary(RingB, App(), logger, allowReset: true).RunAsync(CancellationToken.None);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("AllowKeyRingReset=true", StringComparison.Ordinal));
        Assert.True(Interlocked.Read(ref resets) >= 1);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Outside_production_without_the_switch_a_mismatch_is_logged_not_refused()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var logger = new CapturingLogger();
        await Canary(RingB, App(), logger, environment: "Development").RunAsync(CancellationToken.None);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task No_protected_data_passes_and_a_missing_table_does_not_pass_as_no_data()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        await ExecAsync(Admin("postgres"), "CREATE DATABASE canary_empty");
        await ExecAsync(Admin("canary_empty"), "CREATE TABLE \"Tenants\" (\"Id\" uuid PRIMARY KEY); CREATE TABLE \"Employees\" (\"Id\" uuid, \"TenantId\" uuid, \"NationalId\" varchar(512)); INSERT INTO \"Employees\" VALUES (gen_random_uuid(), gen_random_uuid(), '01234567890');");
        await Canary(RingB, Admin("canary_empty"), new CapturingLogger()).RunAsync(CancellationToken.None);

        await ExecAsync(Admin("postgres"), "CREATE DATABASE canary_nothing");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Canary(RingB, Admin("canary_nothing"), new CapturingLogger()).RunAsync(CancellationToken.None));
        Assert.Contains("Employees does not exist", error.Message);
        // A database that this start migrates is new: nothing to check yet.
        await Canary(RingB, Admin("canary_nothing"), new CapturingLogger(), migrationsAtStartup: true).RunAsync(CancellationToken.None);
    }

    private sealed class Host(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HR.Core";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingLogger : ILogger<EmployeePiiKeyRingCanary>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
