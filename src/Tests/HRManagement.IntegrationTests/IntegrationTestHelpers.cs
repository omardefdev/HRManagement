using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport.CodeGeneration;
using HRManagement.TestSupport.Policy;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>Test-only: if no X-Tenant-ID header, use platform default so inbox filter can run.</summary>
public sealed class TestDefaultTenantConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("testDefaultTenant");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (TenantIdFlow.Get() is null)
            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            await next.Send(context);
        }
        finally
        {
            // TenantIdConsumeFilter clears in its finally; keep consistent if it ran first
        }
    }
}

public static class IntegrationTestHelpers
{
    public static void AddDeterministicServiceClients(IServiceCollection services)
    {
        services.ReplaceCodeGenerationClientWithFake();
        services.ReplacePolicyEnforceClientWithFake();
    }

    public static async Task AssertConsumedAsync<T>(
        ITestHarness harness,
        int minimumCount = 1,
        TimeSpan? timeout = null)
        where T : class
    {
        timeout ??= TimeSpan.FromSeconds(15);
        var filter = harness.Consumed.Select<T>();
        var deadline = DateTime.UtcNow + timeout.Value;
        while (DateTime.UtcNow < deadline)
        {
            if (filter.Count() >= minimumCount)
                return;
            await Task.Delay(100);
        }

        var count = filter.Count();
        Assert.True(
            count >= minimumCount,
            $"Expected at least {minimumCount} consumption(s) of {typeof(T).Name} within {timeout.Value.TotalSeconds}s, got {count}");
    }

    public static void AddSqliteDbContext<TContext>(
        IServiceCollection services,
        SqliteConnection connection)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>(options => options.UseSqlite(connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
    }

    public static void AddNoOpDirectoryEntrySync(IServiceCollection services)
    {
        services.AddSingleton<HRManagement.SharedKernel.Interfaces.IDirectoryEntrySyncService,
            HRManagement.ServiceDefaults.NoOpDirectoryEntrySyncService>();
    }

    public static void AddHRCoreConsumerDependencies(IServiceCollection services)
    {
        services.AddSingleton<IPublishEndpoint>(_ =>
        {
            var mock = new Mock<IPublishEndpoint>(MockBehavior.Loose);
            return mock.Object;
        });
        services.AddScoped<HR.Core.Infrastructure.Integration.AttendanceIntegrationService>();
        services.AddScoped<HR.Core.Infrastructure.Integration.EmployeeIntegrationService>();
    }
}

public sealed class MassTransitTestScope : IAsyncDisposable
{
    private readonly List<SqliteConnection> _connections = [];
    private readonly IServiceScope _dbScope;

    public ServiceProvider Provider { get; }
    public ITestHarness Harness { get; }

    private MassTransitTestScope(
        ServiceProvider provider,
        ITestHarness harness,
        IServiceScope dbScope,
        IEnumerable<SqliteConnection> connections)
    {
        Provider = provider;
        Harness = harness;
        _dbScope = dbScope;
        _connections.AddRange(connections);
    }

    public static async Task<MassTransitTestScope> CreateAsync(
        Action<IServiceCollection, List<SqliteConnection>> configureServices,
        Action<IBusRegistrationConfigurator> configureConsumers,
        bool enableInboxFilter = false)
    {
        var connections = new List<SqliteConnection>();
        var services = new ServiceCollection();
        configureServices(services, connections);
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<InboxMetrics>();

        services.AddMassTransitTestHarness(x =>
        {
            configureConsumers(x);
            if (enableInboxFilter)
            {
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UseConsumeFilter(typeof(TenantIdConsumeFilter<>), context);
                    cfg.UseConsumeFilter(typeof(TestDefaultTenantConsumeFilter<>), context);
                    cfg.UseConsumeFilter(typeof(InboxConsumeFilter<>), context);
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var dbScope = provider.CreateScope();
        return new MassTransitTestScope(provider, harness, dbScope, connections);
    }

    public T GetDb<T>() where T : notnull => _dbScope.ServiceProvider.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Harness.Stop();
        _dbScope.Dispose();
        await Provider.DisposeAsync();
        foreach (var connection in _connections)
            await connection.DisposeAsync();
    }
}

[CollectionDefinition("IntegrationSerial", DisableParallelization = true)]
public sealed class IntegrationSerialCollection;
