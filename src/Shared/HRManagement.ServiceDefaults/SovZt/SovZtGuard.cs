using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.SovZt;

public sealed class SovZtMetrics
{
    public const string MeterName = "HRManagement.SovZt";

    private readonly ISovZtProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;

    public SovZtMetrics(ISovZtProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "sov_zt_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "SOV-004 audit required field count");
        meter.CreateObservableGauge(
            "sov_zt_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "SOV-004 core policy flags asserted");
        meter.CreateObservableGauge(
            "sov_zt_live_pam_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LivePamClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LivePamClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.NoStandingPrivilege?.SharedRootForbidden == true ? 1 : 0) +
            (m.PamJit?.ApprovalRequired == true ? 1 : 0) +
            (m.AdminWorkstations?.OrdinaryDeviceForbidden == true ? 1 : 0) +
            (m.BreakGlass?.ImmediateAlertRequired == true ? 1 : 0) +
            (m.MockSandbox?.LivePamForbidden == true ? 1 : 0);
    }
}

public sealed class PamClaimGuard(
    ISovZtProvider provider,
    ILogger<PamClaimGuard> logger) : IPamClaimGuard
{
    public string? EnsureLivePamAllowed()
    {
        if (!provider.Options.LivePamClaim)
            return null;
        var err = "LivePamClaim forbidden until SOV-004a live PAM workers";
        logger.LogError("SOV-004 pam claim: {Error}", err);
        return err;
    }

    public string? ValidateNoStandingPrivilege(bool hasStanding, bool sharedRoot)
    {
        var err = SovZtEvaluator.ValidateNoStandingPrivilege(hasStanding, sharedRoot, provider.Matrix.NoStandingPrivilege);
        if (err is not null)
            logger.LogError("SOV-004 standing gate: {Error}", err);
        return err;
    }

    public string? ValidateJit(bool temporary, bool reasonPresent, bool approved)
    {
        var err = SovZtEvaluator.ValidateJit(temporary, reasonPresent, approved, provider.Matrix.PamJit);
        if (err is not null)
            logger.LogError("SOV-004 jit gate: {Error}", err);
        return err;
    }

    public string? ValidateAdminWorkstation(bool fromOrdinaryDevice)
    {
        var err = SovZtEvaluator.ValidateAdminWorkstation(fromOrdinaryDevice, provider.Matrix.AdminWorkstations);
        if (err is not null)
            logger.LogError("SOV-004 workstation gate: {Error}", err);
        return err;
    }

    public string? ValidateBreakGlass(bool alerted, bool reviewed)
    {
        var err = SovZtEvaluator.ValidateBreakGlass(alerted, reviewed, provider.Matrix.BreakGlass);
        if (err is not null)
            logger.LogError("SOV-004 break-glass gate: {Error}", err);
        return err;
    }

    public string? ValidateMfa(bool phishingResistant)
    {
        var err = SovZtEvaluator.ValidateMfa(phishingResistant, provider.Matrix.PhishingResistantMfa);
        if (err is not null)
            logger.LogError("SOV-004 mfa gate: {Error}", err);
        return err;
    }

    public string? ValidateRecertification(bool dormantOrphanPresent)
    {
        var err = SovZtEvaluator.ValidateRecertification(dormantOrphanPresent, provider.Matrix.AccessRecertification);
        if (err is not null)
            logger.LogError("SOV-004 recert gate: {Error}", err);
        return err;
    }

    public string? ValidateSessionRecord(bool recorded)
    {
        var err = SovZtEvaluator.ValidateSessionRecord(recorded, provider.Matrix.SessionRecord);
        if (err is not null)
            logger.LogError("SOV-004 session gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = SovZtEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("SOV-004 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(SovZtSigning? meta)
    {
        var err = SovZtEvaluator.ValidateSigning(meta, provider.Matrix.ZeroTrustPolicyVersion);
        if (err is not null)
            logger.LogError("SOV-004 signing gate: {Error}", err);
        return err;
    }
}

public sealed class SovZtStartupValidator(
    ISovZtProvider provider,
    IPamClaimGuard claimGuard,
    SovZtMetrics metrics,
    IHostEnvironment environment,
    ILogger<SovZtStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = SovZtEvaluator.Evaluate(provider.Matrix, opts.LivePamClaim);

        var claimErr = claimGuard.EnsureLivePamAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("SOV-004 zero-trust rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "SOV-004 SovZt startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "SOV-004: {Count} zero-trust rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "SOV-004 sov zt active enforce={Enforce} livePam={Claim} env={Env}",
            opts.EnforceOnStartup,
            opts.LivePamClaim,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
