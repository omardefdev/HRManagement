using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.SovData;

public sealed class SovDataMetrics
{
    public const string MeterName = "HRManagement.SovData";

    private readonly ISovDataProvider _provider;
    private long _auditFieldCount;
    private long _copyTypeCount;
    private long _liveClaim;
    private long _policyFlags;

    public SovDataMetrics(ISovDataProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "sov_data_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "SOV-002 audit required field count");
        meter.CreateObservableGauge(
            "sov_data_copy_type_count",
            () => _copyTypeCount,
            unit: "{types}",
            description: "SOV-002 copy type count");
        meter.CreateObservableGauge(
            "sov_data_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "SOV-002 core policy flags asserted");
        meter.CreateObservableGauge(
            "sov_data_live_sovereignty_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveSovereigntyClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _copyTypeCount = _provider.Matrix.DataCopyInventory?.CopyTypes.Count ?? 0;
        _liveClaim = _provider.Options.LiveSovereigntyClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.Egress?.DenyByDefault == true ? 1 : 0) +
            (m.RemoteSupport?.JitRequired == true ? 1 : 0) +
            (m.Destruction?.CertificateRequired == true ? 1 : 0) +
            (m.Residency?.OutsideScopeForbidden == true ? 1 : 0) +
            (m.MockSandbox?.LiveEgressForbidden == true ? 1 : 0);
    }
}

public sealed class SovereigntyClaimGuard(
    ISovDataProvider provider,
    ILogger<SovereigntyClaimGuard> logger) : ISovereigntyClaimGuard
{
    public string? EnsureLiveSovereigntyAllowed()
    {
        if (!provider.Options.LiveSovereigntyClaim)
            return null;
        var err = "LiveSovereigntyClaim forbidden until SOV-002a live egress enforcement";
        logger.LogError("SOV-002 sovereignty claim: {Error}", err);
        return err;
    }

    public string? ValidateCopyClassification(bool classified, bool approvedLocation)
    {
        var err = SovDataEvaluator.ValidateCopyClassification(
            classified, approvedLocation, provider.Matrix.DataCopyInventory);
        if (err is not null)
            logger.LogError("SOV-002 inventory gate: {Error}", err);
        return err;
    }

    public string? ValidateResidency(bool insideApprovedRegion)
    {
        var err = SovDataEvaluator.ValidateResidency(insideApprovedRegion, provider.Matrix.Residency);
        if (err is not null)
            logger.LogError("SOV-002 residency gate: {Error}", err);
        return err;
    }

    public string? ValidateEgress(bool denyByDefault)
    {
        var err = SovDataEvaluator.ValidateEgress(denyByDefault, provider.Matrix.Egress);
        if (err is not null)
            logger.LogError("SOV-002 egress gate: {Error}", err);
        return err;
    }

    public string? ValidateRemoteSupport(bool jit, bool logged, bool approved)
    {
        var err = SovDataEvaluator.ValidateRemoteSupport(jit, logged, approved, provider.Matrix.RemoteSupport);
        if (err is not null)
            logger.LogError("SOV-002 support gate: {Error}", err);
        return err;
    }

    public string? ValidateDestruction(bool certificatePresent)
    {
        var err = SovDataEvaluator.ValidateDestruction(certificatePresent, provider.Matrix.Destruction);
        if (err is not null)
            logger.LogError("SOV-002 destruction gate: {Error}", err);
        return err;
    }

    public string? ValidateLawfulDisclosure(bool logged)
    {
        var err = SovDataEvaluator.ValidateLawfulDisclosure(logged, provider.Matrix.LawfulDisclosure);
        if (err is not null)
            logger.LogError("SOV-002 disclosure gate: {Error}", err);
        return err;
    }

    public string? ValidateDataFlowMap(bool mapPresent)
    {
        var err = SovDataEvaluator.ValidateDataFlowMap(mapPresent, provider.Matrix.DataFlowMap);
        if (err is not null)
            logger.LogError("SOV-002 flow-map gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = SovDataEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("SOV-002 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(SovDataSigning? meta)
    {
        var err = SovDataEvaluator.ValidateSigning(meta, provider.Matrix.DataSovereigntyPolicyVersion);
        if (err is not null)
            logger.LogError("SOV-002 signing gate: {Error}", err);
        return err;
    }
}

public sealed class SovDataStartupValidator(
    ISovDataProvider provider,
    ISovereigntyClaimGuard claimGuard,
    SovDataMetrics metrics,
    IHostEnvironment environment,
    ILogger<SovDataStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = SovDataEvaluator.Evaluate(provider.Matrix, opts.LiveSovereigntyClaim);

        var claimErr = claimGuard.EnsureLiveSovereigntyAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("SOV-002 data-sovereignty rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "SOV-002 SovData startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "SOV-002: {Count} data-sovereignty rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "SOV-002 sov data active enforce={Enforce} liveSovereignty={Claim} copyTypes={Count} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveSovereigntyClaim,
            provider.Matrix.DataCopyInventory?.CopyTypes.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
