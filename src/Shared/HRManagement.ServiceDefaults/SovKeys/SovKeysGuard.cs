using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.SovKeys;

public sealed class SovKeysMetrics
{
    public const string MeterName = "HRManagement.SovKeys";

    private readonly ISovKeysProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;

    public SovKeysMetrics(ISovKeysProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "sov_keys_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "SOV-003 audit required field count");
        meter.CreateObservableGauge(
            "sov_keys_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "SOV-003 core policy flags asserted");
        meter.CreateObservableGauge(
            "sov_keys_live_key_custody_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveKeyCustodyClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveKeyCustodyClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.HsmKms?.VendorAloneCannotDecrypt == true ? 1 : 0) +
            (m.DualControl?.Required == true ? 1 : 0) +
            (m.Rotation?.CustodianLossDrillRequired == true ? 1 : 0) +
            (m.TrustedTime?.ClockDriftBoundRequired == true ? 1 : 0) +
            (m.MockSandbox?.LiveHsmForbidden == true ? 1 : 0);
    }
}

public sealed class KeyCustodyClaimGuard(
    ISovKeysProvider provider,
    ILogger<KeyCustodyClaimGuard> logger) : IKeyCustodyClaimGuard
{
    public string? EnsureLiveKeyCustodyAllowed()
    {
        if (!provider.Options.LiveKeyCustodyClaim)
            return null;
        var err = "LiveKeyCustodyClaim forbidden until SOV-003a live HSM/KMS";
        logger.LogError("SOV-003 key custody claim: {Error}", err);
        return err;
    }

    public string? ValidateVendorAloneCannotDecrypt(bool vendorAlone)
    {
        var err = SovKeysEvaluator.ValidateVendorAloneCannotDecrypt(vendorAlone, provider.Matrix.HsmKms);
        if (err is not null)
            logger.LogError("SOV-003 vendor gate: {Error}", err);
        return err;
    }

    public string? ValidateHierarchy(bool inventoryPresent)
    {
        var err = SovKeysEvaluator.ValidateHierarchy(inventoryPresent, provider.Matrix.KeyHierarchy);
        if (err is not null)
            logger.LogError("SOV-003 hierarchy gate: {Error}", err);
        return err;
    }

    public string? ValidateDualControl(bool dualControlPresent)
    {
        var err = SovKeysEvaluator.ValidateDualControl(dualControlPresent, provider.Matrix.DualControl);
        if (err is not null)
            logger.LogError("SOV-003 dual-control gate: {Error}", err);
        return err;
    }

    public string? ValidateRotation(bool rotationDrillPresent)
    {
        var err = SovKeysEvaluator.ValidateRotation(rotationDrillPresent, provider.Matrix.Rotation);
        if (err is not null)
            logger.LogError("SOV-003 rotation gate: {Error}", err);
        return err;
    }

    public string? ValidateAlgorithmAgility(bool deprecatedAlgClaimedInProd)
    {
        var err = SovKeysEvaluator.ValidateAlgorithmAgility(
            deprecatedAlgClaimedInProd, provider.Matrix.AlgorithmAgility);
        if (err is not null)
            logger.LogError("SOV-003 algorithm gate: {Error}", err);
        return err;
    }

    public string? ValidateTrustedTime(bool clockDriftBoundPresent)
    {
        var err = SovKeysEvaluator.ValidateTrustedTime(clockDriftBoundPresent, provider.Matrix.TrustedTime);
        if (err is not null)
            logger.LogError("SOV-003 trusted-time gate: {Error}", err);
        return err;
    }

    public string? ValidateSoftKeyProdClaim(bool softKeyClaimedAsProd)
    {
        var err = SovKeysEvaluator.ValidateSoftKeyProdClaim(softKeyClaimedAsProd);
        if (err is not null)
            logger.LogError("SOV-003 soft-key gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = SovKeysEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("SOV-003 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(SovKeysSigning? meta)
    {
        var err = SovKeysEvaluator.ValidateSigning(meta, provider.Matrix.KeySovereigntyPolicyVersion);
        if (err is not null)
            logger.LogError("SOV-003 signing gate: {Error}", err);
        return err;
    }
}

public sealed class SovKeysStartupValidator(
    ISovKeysProvider provider,
    IKeyCustodyClaimGuard claimGuard,
    SovKeysMetrics metrics,
    IHostEnvironment environment,
    ILogger<SovKeysStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = SovKeysEvaluator.Evaluate(provider.Matrix, opts.LiveKeyCustodyClaim);

        var claimErr = claimGuard.EnsureLiveKeyCustodyAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("SOV-003 key-sovereignty rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "SOV-003 SovKeys startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "SOV-003: {Count} key-sovereignty rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "SOV-003 sov keys active enforce={Enforce} liveCustody={Claim} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveKeyCustodyClaim,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
