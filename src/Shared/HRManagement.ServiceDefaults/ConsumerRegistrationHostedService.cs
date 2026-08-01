using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>INT-002: fail startup when discovered IConsumer types were not registered via AddConsumers.</summary>
public sealed class ConsumerRegistrationHostedService(
    ConsumerRegistrationOptions options,
    ILogger<ConsumerRegistrationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.FailOnUnregistered || options.AssembliesToScan.Count == 0)
            return Task.CompletedTask;

        var discovered = options.AssembliesToScan
            .SelectMany(DiscoverConsumers)
            .Distinct()
            .ToHashSet();

        var registered = options.RegisteredConsumerTypes.Distinct().ToHashSet();
        if (registered.Count == 0)
            registered = options.ExpectedConsumerTypes.Distinct().ToHashSet();

        var allow = options.Allowlist.ToHashSet();
        var unregistered = discovered
            .Where(d => !registered.Contains(d) && !allow.Contains(d))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        // Registered types that are not real consumers / not in scanned assemblies
        var bogus = registered
            .Where(r => !discovered.Contains(r) && !allow.Contains(r))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        if (unregistered.Count > 0 || bogus.Count > 0)
        {
            var msg = "INT-002: consumer registration mismatch. Unregistered: "
                      + string.Join(", ", unregistered)
                      + "; Unexpected registered: "
                      + string.Join(", ", bogus);
            logger.LogError("{Message}", msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation(
            "INT-002 consumer registration OK ({Count} consumers across {Assemblies} assemblies)",
            discovered.Count, options.AssembliesToScan.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static IEnumerable<Type> DiscoverConsumers(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.GetInterfaces().Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)));
}

public sealed class ConsumerRegistrationOptions
{
    public bool FailOnUnregistered { get; set; } = true;
    public List<Assembly> AssembliesToScan { get; } = [];
    /// <summary>Types registered via AddConsumers / AddConsumer.</summary>
    public List<Type> RegisteredConsumerTypes { get; } = [];
    /// <summary>Legacy alias used by tests — same as RegisteredConsumerTypes.</summary>
    public List<Type> ExpectedConsumerTypes => RegisteredConsumerTypes;
    /// <summary>Explicitly disabled consumers that may exist in the assembly.</summary>
    public List<Type> Allowlist { get; } = [];
}
