using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.UnitTests;

public class DatabaseMigrationSkipTests
{
    private class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    [Fact]
    public async Task TryMigrateAsync_skips_when_config_is_false()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false"
                })
                .Build());
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase("skip-test"));

        var sp = services.BuildServiceProvider();

        var ex = await Record.ExceptionAsync(() =>
            HRManagement.ServiceDefaults.DatabaseMigrationExtensions
                .TryMigrateAsync<TestDbContext>(sp));

        Assert.Null(ex);
    }

    [Fact]
    public async Task TryMigrateAsync_runs_when_config_is_true()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "true"
                })
                .Build());
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase("run-test"));

        var sp = services.BuildServiceProvider();

        var ex = await Record.ExceptionAsync(() =>
            HRManagement.ServiceDefaults.DatabaseMigrationExtensions
                .TryMigrateAsync<TestDbContext>(sp));

        Assert.Null(ex);
    }

    [Fact]
    public async Task TryMigrateAsync_defaults_to_true()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase("default-test"));

        var sp = services.BuildServiceProvider();

        var ex = await Record.ExceptionAsync(() =>
            HRManagement.ServiceDefaults.DatabaseMigrationExtensions
                .TryMigrateAsync<TestDbContext>(sp));

        Assert.Null(ex);
    }

    [Fact]
    public async Task TryMigrateAsync_throws_in_production_on_failure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "true",
                    ["ASPNETCORE_ENVIRONMENT"] = "Production"
                })
                .Build());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production));
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o =>
            o.UseNpgsql("Host=invalid.example;Database=test;Username=x;Password=y"));

        var sp = services.BuildServiceProvider();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            HRManagement.ServiceDefaults.DatabaseMigrationExtensions
                .TryMigrateAsync<TestDbContext>(sp));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
