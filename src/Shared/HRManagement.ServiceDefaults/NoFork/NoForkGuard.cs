using System.Diagnostics.Metrics;
using HRManagement.ServiceDefaults.ModuleCatalog;
using HRManagement.ServiceDefaults.ProductCapability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.NoFork;

/// <summary>
/// PRD-001 bridge: validates STR-004 agency pack module exposure against archetype entitlements.
/// See also <see cref="CapabilityRegistry"/> for runtime module guard.
/// </summary>
public static class NoForkCapabilityValidation
{
    public static IReadOnlyList<string> ValidateAgainstCapability(
        AgencyConfigPack pack,
        ICapabilityRegistry registry,
        string archetypeId)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(archetypeId))
        {
            failures.Add($"{pack.AgencyId}: missing archetypeId for PRD-001 validation");
            return failures;
        }

        if (registry.GetArchetype(archetypeId) is null)
        {
            failures.Add($"{pack.AgencyId}: unknown archetype '{archetypeId}'");
            return failures;
        }

        foreach (var moduleId in pack.EnabledOptionalModules)
        {
            if (!registry.IsModuleEnabled(archetypeId, moduleId))
            {
                failures.Add(
                    $"{pack.AgencyId}: enabled optional module '{moduleId}' is disabled for archetype '{archetypeId}'");
            }
        }

        foreach (var moduleId in pack.DisabledOptionalModules)
        {
            var classification = registry.GetClassification(archetypeId, moduleId);
            if (string.Equals(classification, "mandatory", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{pack.AgencyId}: cannot disable mandatory module '{moduleId}' for archetype '{archetypeId}'");
            }
        }

        return failures;
    }
}

public sealed class NoForkMetrics
{
    public const string MeterName = "HRManagement.NoFork";

    private readonly INoForkPolicyProvider _provider;
    private long _packCount;

    public NoForkMetrics(INoForkPolicyProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        _packCount = provider.LoadedPacks.Count;
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "nofork_agency_pack_count",
            () => _packCount,
            unit: "{packs}",
            description: "Loaded STR-004 agency config packs");
        meter.CreateObservableGauge(
            "nofork_digest_bound",
            () => string.IsNullOrWhiteSpace(_provider.ReleaseDigest) ? 0 : 1,
            unit: "{bool}",
            description: "1 when releaseDigest is bound");
    }

    public void Recount() => _packCount = _provider.LoadedPacks.Count;
}

public sealed class AgencyBrandingAccessor(
    IOptions<OrganizationBrandingOptions> defaults,
    IAgencyConfigResolver? resolver = null,
    IOptions<NoForkOptions>? noForkOptions = null,
    IHttpContextAccessor? http = null) : IAgencyBrandingAccessor
{
    public OrganizationBrandingOptions GetBranding()
    {
        var baseline = defaults.Value;
        var opts = noForkOptions?.Value;
        if (opts is null || !opts.ResolveAgencyPacks || resolver is null)
            return baseline;

        var tenantId = ResolveTenantId();
        var pack = resolver.TryResolve(tenantId);
        if (pack?.Branding is null)
            return baseline;

        return new OrganizationBrandingOptions
        {
            Name = pack.Branding.OrganizationName ?? baseline.Name,
            ShortName = pack.Branding.ShortName ?? baseline.ShortName,
            LogoUrl = pack.Branding.LogoUrl ?? baseline.LogoUrl,
            PrimaryColor = pack.Branding.PrimaryColor ?? baseline.PrimaryColor
        };
    }

    private Guid? ResolveTenantId()
    {
        var value = http?.HttpContext?.User?.FindFirst(TenantContext.TenantClaimType)?.Value;
        if (Guid.TryParse(value, out var id)) return id;
        return TenantIdFlow.Get();
    }
}

public sealed class NoForkStartupValidator(
    INoForkPolicyProvider provider,
    NoForkMetrics metrics,
    IHostEnvironment environment,
    ILogger<NoForkStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = NoForkEvaluator.EvaluateStartup(
            provider.Matrix,
            provider.LoadedPacks,
            opts.ActiveReleaseDigest);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("STR-004 nofork rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "STR-004 NoFork startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "STR-004: {Count} nofork rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "STR-004 nofork active digest={Digest} packs={Packs} resolvePacks={Resolve} enforce={Enforce} env={Env}",
            provider.ReleaseDigest,
            provider.LoadedPacks.Count,
            opts.ResolveAgencyPacks,
            opts.EnforceOnStartup,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
