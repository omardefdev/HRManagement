using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ExtSector;

public sealed class ExtSectorMetrics
{
    public const string MeterName = "HRManagement.ExtSector";

    private readonly IExtSectorProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;
    private long _domainCount;

    public ExtSectorMetrics(IExtSectorProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "ext_sector_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "EXT-007 audit required field count");
        meter.CreateObservableGauge(
            "ext_sector_domain_count",
            () => _domainCount,
            unit: "{domains}",
            description: "EXT-007 catalog domain count");
        meter.CreateObservableGauge(
            "ext_sector_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "EXT-007 core policy flags asserted");
        meter.CreateObservableGauge(
            "ext_sector_live_catalog_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveSectorCatalogClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _domainCount = _provider.Matrix.CatalogDomains.Count;
        _liveClaim = _provider.Options.LiveSectorCatalogClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.NoImplicitCriticalIntegrations ? 1 : 0) +
            (m.Notification?.SensitivePiiForbidden == true ? 1 : 0) +
            (m.Failure?.SwallowForbidden == true ? 1 : 0) +
            (m.Certification?.RequiredBeforeEnable == true ? 1 : 0) +
            (m.MockSandbox?.LiveAdaptersForbidden == true ? 1 : 0);
    }
}

public sealed class SectorCatalogClaimGuard(
    IExtSectorProvider provider,
    ILogger<SectorCatalogClaimGuard> logger) : ISectorCatalogClaimGuard
{
    public string? EnsureLiveCatalogAllowed()
    {
        if (!provider.Options.LiveSectorCatalogClaim)
            return null;
        var err = "LiveSectorCatalogClaim forbidden until EXT-007a-e sector catalog workers";
        logger.LogError("EXT-007 catalog claim: {Error}", err);
        return err;
    }

    public string? ValidateNoImplicitCritical(bool isDeclaredCritical, bool isInCatalog)
    {
        var err = ExtSectorEvaluator.ValidateNoImplicitCritical(
            isDeclaredCritical, isInCatalog, provider.Matrix.NoImplicitCriticalIntegrations);
        if (err is not null)
            logger.LogError("EXT-007 implicit-critical gate: {Error}", err);
        return err;
    }

    public string? ValidateAdapterCompleteness(
        bool hasContract, bool hasSlo, bool hasRetry, bool hasRecon, bool hasExit)
    {
        var err = ExtSectorEvaluator.ValidateAdapterCompleteness(
            hasContract, hasSlo, hasRetry, hasRecon, hasExit, provider.Matrix.AdapterRequirements);
        if (err is not null)
            logger.LogError("EXT-007 adapter-completeness gate: {Error}", err);
        return err;
    }

    public string? ValidateNotificationNoSensitivePii(bool containsSensitivePii)
    {
        var err = ExtSectorEvaluator.ValidateNotificationNoSensitivePii(
            containsSensitivePii, provider.Matrix.Notification?.SensitivePiiForbidden ?? true);
        if (err is not null)
            logger.LogError("EXT-007 notify-pii gate: {Error}", err);
        return err;
    }

    public string? ValidateFailureSurfaced(bool failureOccurred, bool wasSwallowed)
    {
        var err = ExtSectorEvaluator.ValidateFailureSurfaced(
            failureOccurred, wasSwallowed, provider.Matrix.Failure);
        if (err is not null)
            logger.LogError("EXT-007 failure gate: {Error}", err);
        return err;
    }

    public string? ValidateCertification(bool enabled, bool certified)
    {
        var err = ExtSectorEvaluator.ValidateCertification(
            enabled, certified, provider.Matrix.Certification);
        if (err is not null)
            logger.LogError("EXT-007 certification gate: {Error}", err);
        return err;
    }

    public string? ValidateReconciliation(bool required, bool completed)
    {
        var err = ExtSectorEvaluator.ValidateReconciliation(
            required, completed, provider.Matrix.Reconciliation);
        if (err is not null)
            logger.LogError("EXT-007 reconciliation gate: {Error}", err);
        return err;
    }

    public string? ValidateDeliveryReceipt(bool applicable, bool hasReceipt)
    {
        var err = ExtSectorEvaluator.ValidateDeliveryReceipt(
            applicable, hasReceipt, provider.Matrix.DeliveryReceipts);
        if (err is not null)
            logger.LogError("EXT-007 delivery-receipt gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ExtSectorEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("EXT-007 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ExtSectorSigning? meta)
    {
        var err = ExtSectorEvaluator.ValidateSigning(meta, provider.Matrix.SectorCatalogPolicyVersion);
        if (err is not null)
            logger.LogError("EXT-007 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ExtSectorStartupValidator(
    IExtSectorProvider provider,
    ISectorCatalogClaimGuard claimGuard,
    ExtSectorMetrics metrics,
    IHostEnvironment environment,
    ILogger<ExtSectorStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ExtSectorEvaluator.Evaluate(provider.Matrix, opts.LiveSectorCatalogClaim);

        var claimErr = claimGuard.EnsureLiveCatalogAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("EXT-007 sector-catalog rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "EXT-007 ExtSector startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "EXT-007: {Count} sector-catalog rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "EXT-007 ext sector active enforce={Enforce} liveCatalog={Claim} domains={Domains} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveSectorCatalogClaim,
            provider.Matrix.CatalogDomains.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
