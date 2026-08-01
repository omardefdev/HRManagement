using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ExtApi;

public sealed class ExtApiMetrics
{
    public const string MeterName = "HRManagement.ExtApi";

    private readonly IExtApiProvider _provider;
    private long _catalogFieldCount;
    private long _purposeCount;
    private long _auditFieldCount;
    private long _liveClaim;

    public ExtApiMetrics(IExtApiProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "ext_api_catalog_field_count",
            () => _catalogFieldCount,
            unit: "{fields}",
            description: "EXT-005 required catalog field count");
        meter.CreateObservableGauge(
            "ext_api_purpose_count",
            () => _purposeCount,
            unit: "{purposes}",
            description: "EXT-005 purpose allowlist count");
        meter.CreateObservableGauge(
            "ext_api_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "EXT-005 audit required field count");
        meter.CreateObservableGauge(
            "ext_api_live_exchange_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveExchangeClaim is true");
    }

    public void Recount()
    {
        _catalogFieldCount = _provider.Matrix.RequiredCatalogFields.Count;
        _purposeCount = _provider.Matrix.PurposeBoundExport?.PurposeAllowlist.Count ?? 0;
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveExchangeClaim ? 1 : 0;
    }
}

public sealed class ApiExchangeClaimGuard(
    IExtApiProvider provider,
    ILogger<ApiExchangeClaimGuard> logger) : IApiExchangeClaimGuard
{
    public string? EnsureLiveExchangeAllowed()
    {
        if (!provider.Options.LiveExchangeClaim)
            return null;
        var err = "LiveExchangeClaim forbidden until EXT-005a-e api-exchange workers";
        logger.LogError("EXT-005 exchange claim: {Error}", err);
        return err;
    }

    public string? ValidateCatalogEntry(IEnumerable<string> presentFields)
    {
        var err = ExtApiEvaluator.ValidateCatalogEntry(presentFields, provider.Matrix);
        if (err is not null)
            logger.LogError("EXT-005 catalog gate: {Error}", err);
        return err;
    }

    public string? ValidateBreakingChange(bool isBreaking, bool majorBumped)
    {
        var err = ExtApiEvaluator.ValidateBreakingChange(isBreaking, majorBumped, provider.Matrix.Versioning);
        if (err is not null)
            logger.LogError("EXT-005 versioning gate: {Error}", err);
        return err;
    }

    public string? ValidateSharedDb(bool usesSharedDb)
    {
        var err = ExtApiEvaluator.ValidateSharedDb(usesSharedDb, provider.Matrix.SharedDbForbidden);
        if (err is not null)
            logger.LogError("EXT-005 shared-db gate: {Error}", err);
        return err;
    }

    public string? ValidatePurposeExport(string purpose, bool consentPresent)
    {
        var err = ExtApiEvaluator.ValidatePurposeExport(purpose, consentPresent, provider.Matrix.PurposeBoundExport);
        if (err is not null)
            logger.LogError("EXT-005 purpose gate: {Error}", err);
        return err;
    }

    public string? ValidateCertifiedConsumerBreak(bool breaksCertified)
    {
        var err = ExtApiEvaluator.ValidateCertifiedConsumerBreak(
            breaksCertified, provider.Matrix.ConsumerDriven);
        if (err is not null)
            logger.LogError("EXT-005 consumer-driven gate: {Error}", err);
        return err;
    }

    public string? ValidateSchemaCompat(bool compatible)
    {
        var err = ExtApiEvaluator.ValidateSchemaCompat(compatible);
        if (err is not null)
            logger.LogError("EXT-005 schema gate: {Error}", err);
        return err;
    }

    public string? ValidateDeprecationWindow(int noticeDays)
    {
        var err = ExtApiEvaluator.ValidateDeprecationWindow(noticeDays, provider.Matrix.Deprecation);
        if (err is not null)
            logger.LogError("EXT-005 deprecation gate: {Error}", err);
        return err;
    }

    public string? ValidateThrottleExceed(bool exceeded, bool returned429)
    {
        var err = ExtApiEvaluator.ValidateThrottleExceed(exceeded, returned429, provider.Matrix.Throttling);
        if (err is not null)
            logger.LogError("EXT-005 throttle gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ExtApiEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("EXT-005 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ExtApiSigning? meta)
    {
        var err = ExtApiEvaluator.ValidateSigning(meta, provider.Matrix.ApiExchangePolicyVersion);
        if (err is not null)
            logger.LogError("EXT-005 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ExtApiStartupValidator(
    IExtApiProvider provider,
    IApiExchangeClaimGuard claimGuard,
    ExtApiMetrics metrics,
    IHostEnvironment environment,
    ILogger<ExtApiStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ExtApiEvaluator.Evaluate(provider.Matrix, opts.LiveExchangeClaim);

        var claimErr = claimGuard.EnsureLiveExchangeAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("EXT-005 api-exchange rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "EXT-005 ExtApi startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "EXT-005: {Count} api-exchange rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "EXT-005 ext api active enforce={Enforce} liveExchange={Claim} catalogFields={Catalog} purposes={Purposes} auditFields={Audit} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveExchangeClaim,
            provider.Matrix.RequiredCatalogFields.Count,
            provider.Matrix.PurposeBoundExport?.PurposeAllowlist.Count ?? 0,
            provider.Matrix.Audit?.RequiredFields.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
