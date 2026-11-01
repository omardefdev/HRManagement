using HRManagement.SharedKernel.Legal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-37 (§6): readiness fails when the SYR-003 service-regimes pack is missing, unreadable, or has a required rule
/// count of zero (an empty pack would refuse every separation, transfer and promotion). The message names the pack.
/// </summary>
public sealed class ServiceRegimePackHealthCheck : IHealthCheck
{
    public const string Name = "legal-pack-" + ServiceRegimePack.PackId;

    private readonly Func<ServiceRegimePack?> _pack;

    public ServiceRegimePackHealthCheck()
        : this(() => ServiceRegimePack.Current)
    {
    }

    /// <summary>For tests: checks the given pack instead of <see cref="ServiceRegimePack.Current"/>.</summary>
    public ServiceRegimePackHealthCheck(Func<ServiceRegimePack?> pack) => _pack = pack;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ServiceRegimePack? pack;
        try
        {
            pack = _pack();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Legal pack {ServiceRegimePack.PackId} could not be read: {ex.Message}"));
        }

        if (pack is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Legal pack {ServiceRegimePack.PackId} not found ({ServiceRegimePack.ContentRelativePath} or {ServiceRegimePack.PathKey})."));
        }

        var empty = pack.Counts.Required().Where(c => c.Count == 0).Select(c => c.Name).ToList();
        var data = new Dictionary<string, object>
        {
            ["pack"] = ServiceRegimePack.PackId,
            ["source"] = pack.Source,
            ["sha256"] = pack.Sha256
        };
        return Task.FromResult(empty.Count > 0
            ? HealthCheckResult.Unhealthy($"Legal pack {ServiceRegimePack.PackId} has no {string.Join(", ", empty)}.", data: data)
            : HealthCheckResult.Healthy($"Legal pack {ServiceRegimePack.PackId} sha256={pack.Sha256}", data));
    }
}

/// <summary>Writes the one start-up line with the pack's source, SHA-256 and rule counts; a missing configured path stops the host.</summary>
internal sealed class ServiceRegimePackStartupLog(ILogger<ServiceRegimePackStartupLog> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A configured LegalPacks:ServiceRegimesPath that does not exist throws here: the service does not start.
        var pack = ServiceRegimePack.Current;
        if (pack is null)
            logger.LogError("LegalPack missing pack={Pack}: readiness is Unhealthy until it is shipped or configured", ServiceRegimePack.PackId);
        else
            logger.LogInformation("{LegalPackLine}", pack.StartupLine());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class ServiceRegimePackHealthCheckExtensions
{
    /// <summary>
    /// PLAN-37 (§6): every service that reads ServiceRegime*Rules registers this. It applies
    /// <c>LegalPacks:ServiceRegimesPath</c> (the repository fallback only in Development), adds the <c>ready</c> check
    /// and the start-up fingerprint line.
    /// </summary>
    public static IServiceCollection AddServiceRegimePackCheck(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ServiceRegimePack.Configure(configuration[ServiceRegimePack.PathKey], environment.IsDevelopment());
        services.AddHealthChecks().AddCheck<ServiceRegimePackHealthCheck>(ServiceRegimePackHealthCheck.Name, tags: ["ready"]);
        services.AddHostedService<ServiceRegimePackStartupLog>();
        return services;
    }
}
