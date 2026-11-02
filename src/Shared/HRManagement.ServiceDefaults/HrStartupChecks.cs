using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-39 (P-3): a check that must pass before a service touches its database. A hosted service starts too late: by
/// then the migrations and the seeds of <c>Program</c> have already connected (and a fresh Data Protection key ring could
/// already have protected new data over the old). The checks run in registration order from
/// <see cref="DatabaseMigrationExtensions.TryMigrateAsync{TContext}"/>, which every host calls before its migrations and
/// seeds, also when <c>Database:RunMigrationsAtStartup=false</c>; a host that never calls it still runs them as the first
/// hosted service. A check that refuses throws, and the start stops.
/// </summary>
public interface IHrStartupCheck
{
    /// <summary>Short name for the log (no value of any setting).</summary>
    string Name { get; }

    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>Remembers that the checks of this host already ran, so the early hook and the hosted fallback run them once.</summary>
public sealed class HrStartupCheckState
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public bool Completed { get; internal set; }
}

public static class HrStartupChecks
{
    /// <summary>Registers a start-up check (singleton) and, once per host, the state and the hosted fallback.</summary>
    public static IServiceCollection AddHrStartupCheck<TCheck>(this IServiceCollection services)
        where TCheck : class, IHrStartupCheck
    {
        services.TryAddSingleton<HrStartupCheckState>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HrStartupChecksHostedService>());
        services.TryAddSingleton<TCheck>();
        services.AddSingleton<IHrStartupCheck>(sp => sp.GetRequiredService<TCheck>());
        return services;
    }

    /// <summary>Runs every registered check once, in registration order; throws the first refusal.</summary>
    public static async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var state = services.GetService<HrStartupCheckState>();
        if (state is null || state.Completed)
            return;

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (state.Completed)
                return;
            foreach (var check in services.GetServices<IHrStartupCheck>())
                await check.RunAsync(cancellationToken);
            state.Completed = true;
        }
        finally
        {
            state.Gate.Release();
        }
    }
}

/// <summary>Fallback for a host that starts without calling TryMigrateAsync: the checks still run before the other hosted services.</summary>
internal sealed class HrStartupChecksHostedService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => HrStartupChecks.RunOnceAsync(services, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
