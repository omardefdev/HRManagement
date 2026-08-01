using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProdExtensibility;

public sealed class ProdExtensibilityMetrics
{
    public const string MeterName = "HRManagement.ProdExtensibility";

    private readonly IProdExtensibilityProvider _provider;
    private long _auditFieldCount;
    private long _contractTypeCount;
    private long _liveClaim;
    private long _policyFlags;

    public ProdExtensibilityMetrics(IProdExtensibilityProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "prod_extensibility_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "PROD-007 audit required field count");
        meter.CreateObservableGauge(
            "prod_extensibility_contract_type_count",
            () => _contractTypeCount,
            unit: "{types}",
            description: "PROD-007 supported contract type count");
        meter.CreateObservableGauge(
            "prod_extensibility_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "PROD-007 core policy flags asserted");
        meter.CreateObservableGauge(
            "prod_extensibility_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveExtensibilityClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _contractTypeCount = _provider.Matrix.SupportedContracts?.ContractTypes.Count ?? 0;
        _liveClaim = _provider.Options.LiveExtensibilityClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.CompatibilityWindows?.SupportNMinus2 == true ? 1 : 0) +
            (m.PluginIsolation?.CrossTenantForbidden == true ? 1 : 0) +
            (m.NoDirectDb ? 1 : 0) +
            (m.NoCustomFork ? 1 : 0) +
            (m.MockSandbox?.LiveExtensionRuntimeForbidden == true ? 1 : 0);
    }
}

public sealed class ExtensibilityClaimGuard(
    IProdExtensibilityProvider provider,
    ILogger<ExtensibilityClaimGuard> logger) : IExtensibilityClaimGuard
{
    public string? EnsureLiveExtensibilityAllowed()
    {
        if (!provider.Options.LiveExtensibilityClaim)
            return null;
        var err = "LiveExtensibilityClaim forbidden until PROD-007a-e live extensibility workers";
        logger.LogError("PROD-007 extensibility claim: {Error}", err);
        return err;
    }

    public string? ValidateN2Compatibility(bool withinWindow)
    {
        var err = ProdExtensibilityEvaluator.ValidateN2Compatibility(
            withinWindow, provider.Matrix.CompatibilityWindows);
        if (err is not null)
            logger.LogError("PROD-007 N-2 gate: {Error}", err);
        return err;
    }

    public string? ValidatePluginIsolation(bool withinTenant, bool coreHealthy)
    {
        var err = ProdExtensibilityEvaluator.ValidatePluginIsolation(
            withinTenant, coreHealthy, provider.Matrix.PluginIsolation);
        if (err is not null)
            logger.LogError("PROD-007 isolation gate: {Error}", err);
        return err;
    }

    public string? ValidateQuota(bool withinLimits)
    {
        var err = ProdExtensibilityEvaluator.ValidateQuota(withinLimits, provider.Matrix.Quotas);
        if (err is not null)
            logger.LogError("PROD-007 quota gate: {Error}", err);
        return err;
    }

    public string? ValidateDeprecation(bool breakingChange, bool noticeSatisfied)
    {
        var err = ProdExtensibilityEvaluator.ValidateDeprecation(
            breakingChange, noticeSatisfied, provider.Matrix.Deprecation);
        if (err is not null)
            logger.LogError("PROD-007 deprecation gate: {Error}", err);
        return err;
    }

    public string? ValidateNoDirectDb(bool directDbAttempted)
    {
        var err = ProdExtensibilityEvaluator.ValidateNoDirectDb(directDbAttempted, provider.Matrix.NoDirectDb);
        if (err is not null)
            logger.LogError("PROD-007 direct-db gate: {Error}", err);
        return err;
    }

    public string? ValidateNoCustomFork(bool customForkAttempted)
    {
        var err = ProdExtensibilityEvaluator.ValidateNoCustomFork(customForkAttempted, provider.Matrix.NoCustomFork);
        if (err is not null)
            logger.LogError("PROD-007 custom-fork gate: {Error}", err);
        return err;
    }

    public string? ValidateUpgradePath(bool rehearsalPresent)
    {
        var err = ProdExtensibilityEvaluator.ValidateUpgradePath(rehearsalPresent);
        if (err is not null)
            logger.LogError("PROD-007 upgrade gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ProdExtensibilityEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("PROD-007 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ProdExtensibilitySigning? meta)
    {
        var err = ProdExtensibilityEvaluator.ValidateSigning(meta, provider.Matrix.ExtensibilityPolicyVersion);
        if (err is not null)
            logger.LogError("PROD-007 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ProdExtensibilityStartupValidator(
    IProdExtensibilityProvider provider,
    IExtensibilityClaimGuard claimGuard,
    ProdExtensibilityMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProdExtensibilityStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProdExtensibilityEvaluator.Evaluate(provider.Matrix, opts.LiveExtensibilityClaim);

        var claimErr = claimGuard.EnsureLiveExtensibilityAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("PROD-007 extensibility rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "PROD-007 ProdExtensibility startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "PROD-007: {Count} extensibility rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "PROD-007 prod extensibility active enforce={Enforce} liveExtensibility={Claim} contractTypes={Types} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveExtensibilityClaim,
            provider.Matrix.SupportedContracts?.ContractTypes.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
