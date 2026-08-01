using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Connectors;

public sealed class GovernmentConnectorStartupValidator(
    LiveConnectorRegistry registry,
    IOptions<GovernmentConnectorOptions> options,
    IHostEnvironment environment,
    ILogger<GovernmentConnectorStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var failures = Evaluate(registry, opts, environment);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("INT-004 connector rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "INT-004 GovernmentConnectors startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "INT-004: {Count} connector rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "INT-004 government connectors active enforce={Enforce} treasury={Treasury} civil={Civil} federation={Federation} env={Env}",
            opts.EnforceOnStartup,
            opts.Treasury.Mode,
            opts.CivilRegistry.Mode,
            opts.Federation.Mode,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static List<string> Evaluate(
        LiveConnectorRegistry registry,
        GovernmentConnectorOptions opts,
        IHostEnvironment environment)
    {
        var failures = new List<string>();
        var isProduction = environment.IsProduction();

        foreach (var connector in registry.All)
        {
            if (isProduction
                && connector.RequiredInProduction
                && connector.Mode is IntegrationConnectorMode.Stub or IntegrationConnectorMode.Sandbox)
            {
                failures.Add(
                    $"Production forbids {connector.Mode} mode for required connector '{connector.ConnectorId}' ({connector.ExtCard})");
            }

            if (connector.Mode == IntegrationConnectorMode.Live
                && !connector.SandboxEnabled
                && connector is TreasuryLiveConnector or CivilRegistryLiveConnector)
            {
                failures.Add(
                    $"Live+!Sandbox for '{connector.ConnectorId}' blocked until {connector.ExtCard} national endpoints certified");
            }
        }

        foreach (var descriptor in LiveConnectorRegistry.BuiltIn)
        {
            var live = registry.Get(descriptor.ConnectorId);
            if (live is null)
                failures.Add($"Missing registered connector '{descriptor.ConnectorId}'");
        }

        return failures;
    }
}
