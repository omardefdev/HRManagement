using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ExtTreasury;

public sealed class ExtTreasuryMetrics
{
    public const string MeterName = "HRManagement.ExtTreasury";

    private readonly IExtTreasuryProvider _provider;
    private long _paymentOrderFieldCount;
    private long _auditFieldCount;
    private long _liveClaim;

    public ExtTreasuryMetrics(IExtTreasuryProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "ext_treasury_payment_order_field_count",
            () => _paymentOrderFieldCount,
            unit: "{fields}",
            description: "EXT-003 paymentOrder required field count");
        meter.CreateObservableGauge(
            "ext_treasury_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "EXT-003 audit required field count");
        meter.CreateObservableGauge(
            "ext_treasury_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveTreasuryClaim is true");
    }

    public void Recount()
    {
        _paymentOrderFieldCount = _provider.Matrix.PaymentOrder?.RequiredFields.Count ?? 0;
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveTreasuryClaim ? 1 : 0;
    }
}

public sealed class TreasuryPaymentClaimGuard(
    IExtTreasuryProvider provider,
    ILogger<TreasuryPaymentClaimGuard> logger) : ITreasuryPaymentClaimGuard
{
    public string? EnsureLiveTreasuryAllowed()
    {
        if (!provider.Options.LiveTreasuryClaim)
            return null;
        var err = "LiveTreasuryClaim forbidden until EXT-003a-e treasury/payment workers";
        logger.LogError("EXT-003 treasury claim: {Error}", err);
        return err;
    }

    public string? ValidateApprovedRun(string runStatus)
    {
        var err = ExtTreasuryEvaluator.ValidateApprovedRun(runStatus, provider.Matrix);
        if (err is not null)
            logger.LogError("EXT-003 approved-run gate: {Error}", err);
        return err;
    }

    public string? ValidateIdempotency(bool duplicateSubmit, bool secondBusinessEffect)
    {
        var err = ExtTreasuryEvaluator.ValidateIdempotency(
            duplicateSubmit, secondBusinessEffect, provider.Matrix.ExactlyOnce);
        if (err is not null)
            logger.LogError("EXT-003 exactly-once gate: {Error}", err);
        return err;
    }

    public string? ValidateTotals(decimal orderTotal, decimal settledTotal, bool lineCountMatch)
    {
        var err = ExtTreasuryEvaluator.ValidateTotals(
            orderTotal, settledTotal, lineCountMatch, provider.Matrix.Reconciliation);
        if (err is not null)
            logger.LogError("EXT-003 recon gate: {Error}", err);
        return err;
    }

    public string? ValidateRejectCorrection(bool isReject, bool correctionQueued)
    {
        var err = ExtTreasuryEvaluator.ValidateRejectCorrection(
            isReject, correctionQueued, provider.Matrix.RejectCorrectionWorkflow);
        if (err is not null)
            logger.LogError("EXT-003 reject-correction gate: {Error}", err);
        return err;
    }

    public string? ValidateManualFile(bool audited)
    {
        var err = ExtTreasuryEvaluator.ValidateManualFile(audited, provider.Matrix.ManualFiles);
        if (err is not null)
            logger.LogError("EXT-003 manual-file gate: {Error}", err);
        return err;
    }

    public string? ValidateCutoff(bool withinCutoff, bool contingencyHold = false)
    {
        var err = ExtTreasuryEvaluator.ValidateCutoff(
            withinCutoff, contingencyHold, provider.Matrix.CutoffCalendar);
        if (err is not null)
            logger.LogError("EXT-003 cutoff gate: {Error}", err);
        return err;
    }

    public string? ValidateLivePkiClaim(bool livePki)
    {
        var err = ExtTreasuryEvaluator.ValidateLivePkiClaim(
            livePki, provider.Matrix.Signatures, provider.Matrix.Blockers);
        if (err is not null)
            logger.LogError("EXT-003 live-PKI gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ExtTreasuryEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("EXT-003 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ExtTreasurySigning? meta)
    {
        var err = ExtTreasuryEvaluator.ValidateSigning(meta, provider.Matrix.TreasuryPolicyVersion);
        if (err is not null)
            logger.LogError("EXT-003 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ExtTreasuryStartupValidator(
    IExtTreasuryProvider provider,
    ITreasuryPaymentClaimGuard claimGuard,
    ExtTreasuryMetrics metrics,
    IHostEnvironment environment,
    ILogger<ExtTreasuryStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ExtTreasuryEvaluator.Evaluate(provider.Matrix, opts.LiveTreasuryClaim);

        var claimErr = claimGuard.EnsureLiveTreasuryAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("EXT-003 treasury rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "EXT-003 ExtTreasury startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "EXT-003: {Count} treasury rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "EXT-003 ext treasury active enforce={Enforce} liveTreasury={Claim} paymentOrderFields={Order} auditFields={Audit} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveTreasuryClaim,
            provider.Matrix.PaymentOrder?.RequiredFields.Count ?? 0,
            provider.Matrix.Audit?.RequiredFields.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
