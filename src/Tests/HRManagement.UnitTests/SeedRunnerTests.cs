using System.Diagnostics;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-38 (section 4, D-38-6): a failed mandatory seed stops a Production start (named, no data), the emergency switch
/// logs Critical and goes on, Development warns, the Acceptance profile keeps throwing; optional seeds only warn.
/// </summary>
public class SeedRunnerTests
{
    private const string RowValue = "national-id-0123456789";

    private static Exception Failure() =>
        new InvalidOperationException("insert failed", new PostgresException(
            $"new row violates row-level security policy, Key (NationalId)=({RowValue})", "ERROR", "ERROR", "42501"));

    private static (SeedRunner Runner, ListLogger Logger) Runner(
        string environment, SeedProfile profile = SeedProfile.None, bool allowFailure = false)
    {
        var logger = new ListLogger();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SeedRunner.AllowMandatoryFailureKey] = allowFailure ? "true" : null,
        }).Build();
        return (new SeedRunner("HR.Core", profile, new TestHostEnvironment(environment), configuration, logger), logger);
    }

    [Fact]
    public async Task A_failed_mandatory_seed_stops_a_production_start_naming_the_seed_without_data()
    {
        var (runner, logger) = Runner("Production");

        var ex = await Assert.ThrowsAsync<MandatorySeedFailedException>(() =>
            runner.RunMandatoryAsync("national-mandatory", () => throw Failure()));

        Assert.Equal("HR.Core", ex.Service);
        Assert.Equal("national-mandatory", ex.Seed);
        Assert.Contains("national-mandatory", ex.Message, StringComparison.Ordinal);
        Assert.Contains("HR.Core", ex.Message, StringComparison.Ordinal);
        Assert.Contains("SQLSTATE 42501", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RowValue, ex.Message, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
        var critical = Assert.Single(logger.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains("national-mandatory", critical.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RowValue, critical.Message, StringComparison.Ordinal);
        Assert.Null(critical.Exception);
    }

    [Fact]
    public async Task The_emergency_switch_logs_critical_counts_and_lets_the_start_go_on()
    {
        var (runner, logger) = Runner("Production", allowFailure: true);
        long counted = 0;
        using var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "HRManagement.Seeding" && instrument.Name == "seed_mandatory_failures_total")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref counted, value));
        listener.Start();

        await runner.RunMandatoryAsync("national-mandatory", () => throw Failure());

        var critical = Assert.Single(logger.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains(SeedRunner.AllowMandatoryFailureKey, critical.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RowValue, critical.Message, StringComparison.Ordinal);
        Assert.Equal(1, Interlocked.Read(ref counted));
    }

    [Fact]
    public async Task Development_only_warns_about_a_failed_mandatory_seed()
    {
        var (runner, logger) = Runner("Development", SeedProfile.Demo);
        await runner.RunMandatoryAsync("national-mandatory", () => throw Failure());
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.DoesNotContain(RowValue, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_acceptance_profile_keeps_failing_the_start_for_every_seed()
    {
        var (runner, _) = Runner("Acceptance", SeedProfile.Acceptance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunMandatoryAsync("national-mandatory", () => throw Failure()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunOptionalAsync("acceptance", () => throw Failure()));
    }

    [Fact]
    public async Task The_acceptance_environment_without_the_acceptance_profile_stops_like_production()
    {
        // The least-privilege stack: ASPNETCORE_ENVIRONMENT=Acceptance with Seed:Profile=None (AC-OPS-38h).
        var (runner, _) = Runner("Acceptance", SeedProfile.None);
        var ex = await Assert.ThrowsAsync<MandatorySeedFailedException>(() =>
            runner.RunMandatoryAsync("national-mandatory", () => throw Failure()));
        Assert.DoesNotContain(RowValue, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_optional_seed_is_a_warning_even_in_production()
    {
        var (runner, logger) = Runner("Production", SeedProfile.Demo);
        await runner.RunOptionalAsync("demo", () => throw Failure());
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("demo", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RowValue, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Successful_seeds_run_once_and_log_nothing()
    {
        var (runner, logger) = Runner("Production");
        var runs = 0;
        await runner.RunMandatoryAsync("national-mandatory", () => { runs++; return Task.CompletedTask; });
        await runner.RunOptionalAsync("demo", () => { runs++; return Task.CompletedTask; });
        Assert.Equal(2, runs);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void The_services_route_their_seeds_through_the_runner()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !System.IO.File.Exists(Path.Combine(root.FullName, "HRManagement.slnx")))
            root = root.Parent;
        string Read(string relative) => System.IO.File.ReadAllText(Path.Combine(root!.FullName, relative));

        var core = Read("src/Services/HRCore/HR.Core.Web/Program.cs");
        Assert.Contains("seeds.RunMandatoryAsync(\"national-mandatory\"", core);
        Assert.DoesNotContain("HR.Core seed skipped", core);

        // The DEFAULT contract type is seeded in every profile but Acceptance, including None (production).
        var contracts = Read("src/Services/HRContracts/HR.Contracts.Web/Program.cs");
        Assert.Contains("seeds.RunMandatoryAsync(\"default-contract-type\", () => ContractsDataSeeder.SeedAsync(contractsDb))", contracts);
        Assert.DoesNotContain("if (seedProfile != SeedProfile.None)", contracts);
        Assert.Contains("seeds.RunOptionalAsync(\"demo\", () => DemoContractsSeeder.SeedAsync(contractsDb))", contracts);

        Assert.Contains("new SeedRunner(\"HR.Training\"", Read("src/Services/HRTraining/HR.Training.Web/Program.cs"));
        Assert.True(System.IO.File.Exists(Path.Combine(root!.FullName, "docs", "OPS_SEED_INVENTORY.md")));
    }

    [Fact]
    public void Lint_pool_rule_fails_its_own_negatives()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !System.IO.File.Exists(Path.Combine(root.FullName, "HRManagement.slnx")))
            root = root.Parent;
        var script = Path.Combine(root!.FullName, "scripts", "vps", "lint-compose.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -SelfTest",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root.FullName,
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"lint-compose -SelfTest failed: {stdout}\n{stderr}");
        Assert.Contains("[SELFTEST PASS] over 90 percent", stdout);
        Assert.Contains("[SELFTEST PASS] no maximum", stdout);
        Assert.Contains("[SELFTEST PASS] keycloak unbounded", stdout);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HRManagement.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
