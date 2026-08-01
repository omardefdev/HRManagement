using System.Diagnostics.Metrics;
using HRManagement.ServiceDefaults.NoFork;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ModuleCatalog;

public sealed class ModuleCatalogMetrics
{
    public const string MeterName = "HRManagement.ModuleCatalog";

    private readonly IModuleCatalogProvider _provider;
    private long _ready;
    private long _notReady;
    private long _sunset;
    private long _enabled;

    public ModuleCatalogMetrics(IModuleCatalogProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount(Array.Empty<string>());
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "module_catalog_readiness_count",
            ObserveReadiness,
            unit: "{modules}",
            description: "STR-003 module counts by readiness");
        meter.CreateObservableGauge(
            "module_catalog_enabled_count",
            () => _enabled,
            unit: "{modules}",
            description: "Effective enabled module count after entitlement");
    }

    public void Recount(string[] enabledModules)
    {
        _ready = _provider.Catalog.Modules.Count(m =>
            string.Equals(m.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase));
        _notReady = _provider.Catalog.Modules.Count(m =>
            string.Equals(m.ReadinessStatus, "NotReady", StringComparison.OrdinalIgnoreCase));
        _sunset = _provider.Catalog.Modules.Count(m =>
            string.Equals(m.ReadinessStatus, "Sunset", StringComparison.OrdinalIgnoreCase));
        if (enabledModules.Length == 0)
            _enabled = _provider.GetAgencyVisibleModules().Count;
        else
            _enabled = enabledModules.Length;
    }

    private IEnumerable<Measurement<long>> ObserveReadiness()
    {
        yield return new Measurement<long>(_ready, new KeyValuePair<string, object?>("status", "Ready"));
        yield return new Measurement<long>(_notReady, new KeyValuePair<string, object?>("status", "NotReady"));
        yield return new Measurement<long>(_sunset, new KeyValuePair<string, object?>("status", "Sunset"));
    }
}

public interface IModuleEntitlementGuard
{
    bool IsModuleExposed(string slugOrSegment);
    string? EnsureEnabledModulesValid(string[]? enabledModules = null);
    string[] GetConfiguredEnabledModules();
}

public sealed class ModuleEntitlementGuard(
    IModuleCatalogProvider provider,
    IConfiguration configuration,
    ModuleCatalogMetrics metrics,
    ILogger<ModuleEntitlementGuard> logger,
    IHttpContextAccessor? http = null,
    IAgencyConfigResolver? agencyResolver = null,
    IOptions<NoForkOptions>? noForkOptions = null) : IModuleEntitlementGuard
{
    public string[] GetConfiguredEnabledModules() =>
        configuration.GetSection("Gateway:EnabledModules").Get<string[]>() ?? [];

    public bool IsModuleExposed(string slugOrSegment)
    {
        var enabled = GetConfiguredEnabledModules();
        metrics.Recount(enabled);
        var baseOk = ModuleCatalogEvaluator.IsModuleExposed(
            provider.Catalog,
            slugOrSegment,
            enabled,
            provider.Options.AllowNotReadyExposure,
            provider.HasActiveException);
        if (!baseOk) return false;

        var noFork = noForkOptions?.Value;
        if (noFork is null || !noFork.ResolveAgencyPacks || agencyResolver is null)
            return true;

        var tenantId = ResolveTenantId();
        var pack = agencyResolver.TryResolve(tenantId);
        if (pack is null) return true;

        return NoForkEvaluator.IsAllowedByAgencyPack(provider.Catalog, pack, slugOrSegment);
    }

    public string? EnsureEnabledModulesValid(string[]? enabledModules = null)
    {
        var enabled = enabledModules ?? GetConfiguredEnabledModules();
        metrics.Recount(enabled);
        var failures = ModuleCatalogEvaluator.Evaluate(
            provider.Catalog,
            enabled,
            provider.Options.AllowNotReadyExposure,
            validateEvidenceFilesOnDisk: false,
            repoRoot: null);
        if (failures.Count == 0) return null;
        var msg = "STR-003: " + string.Join("; ", failures);
        logger.LogError("{Message}", msg);
        return msg;
    }

    private Guid? ResolveTenantId()
    {
        var value = http?.HttpContext?.User?.FindFirst(TenantContext.TenantClaimType)?.Value;
        if (Guid.TryParse(value, out var id)) return id;
        return TenantIdFlow.Get();
    }
}

public sealed class ModuleCatalogStartupValidator(
    IModuleCatalogProvider provider,
    IModuleEntitlementGuard guard,
    ModuleCatalogMetrics metrics,
    IHostEnvironment environment,
    ILogger<ModuleCatalogStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = provider.Options;
        var enabled = guard.GetConfiguredEnabledModules();
        metrics.Recount(enabled);

        var repoRoot = opts.ValidateEvidenceFilesOnDisk ? provider.ResolveRepoRoot() : null;
        var failures = ModuleCatalogEvaluator.Evaluate(
            provider.Catalog,
            enabled,
            opts.AllowNotReadyExposure,
            opts.ValidateEvidenceFilesOnDisk,
            repoRoot);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("STR-003 module catalog rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "STR-003 ModuleCatalog startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "STR-003: {Count} module catalog rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        var exposed = provider.Catalog.Modules
            .Where(m => !string.Equals(m.ServiceTier, "platform", StringComparison.OrdinalIgnoreCase))
            .Count(m => guard.IsModuleExposed(m.ModuleId));

        logger.LogInformation(
            "STR-003 module catalog active enforce={Enforce} pathEntitlement={Path} ready={Ready} notReady={NotReady} enabledConfig={EnabledCount} exposed={Exposed} env={Env}",
            opts.EnforceOnStartup,
            opts.EnforcePathEntitlement,
            provider.Catalog.Modules.Count(m =>
                string.Equals(m.ReadinessStatus, "Ready", StringComparison.OrdinalIgnoreCase)),
            provider.Catalog.Modules.Count(m =>
                string.Equals(m.ReadinessStatus, "NotReady", StringComparison.OrdinalIgnoreCase)),
            enabled.Length,
            exposed,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
