using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProdBranch;

public sealed class ProdBranchMetrics
{
    public const string MeterName = "HRManagement.ProdBranch";

    private readonly IProdBranchProvider _provider;
    private long _auditFieldCount;
    private long _budgetFlags;
    private long _liveClaim;
    private long _policyFlags;

    public ProdBranchMetrics(IProdBranchProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "prod_branch_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "PROD-004 audit required field count");
        meter.CreateObservableGauge(
            "prod_branch_budget_flag_count",
            () => _budgetFlags,
            unit: "{flags}",
            description: "PROD-004 performance budget flags asserted");
        meter.CreateObservableGauge(
            "prod_branch_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "PROD-004 core policy flags asserted");
        meter.CreateObservableGauge(
            "prod_branch_live_remote_branch_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveRemoteBranchClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        var m = _provider.Matrix;
        _budgetFlags =
            (m.PerformanceBudgets?.Required == true ? 1 : 0) +
            (m.PerformanceBudgets?.MaxTtiMs > 0 ? 1 : 0) +
            (m.PerformanceBudgets?.MaxPayloadKb > 0 ? 1 : 0);
        _liveClaim = _provider.Options.LiveRemoteBranchClaim ? 1 : 0;
        _policyFlags =
            (m.NetworkProfile?.LowBandwidthSupported == true ? 1 : 0) +
            (m.ResumableUploads?.Required == true ? 1 : 0) +
            (m.Offline?.EncryptionRequired == true ? 1 : 0) +
            (m.Accessibility?.Wcag22AaRequired == true ? 1 : 0) +
            (m.MockSandbox?.LiveRemoteBranchForbidden == true ? 1 : 0);
    }
}

public sealed class RemoteBranchClaimGuard(
    IProdBranchProvider provider,
    ILogger<RemoteBranchClaimGuard> logger) : IRemoteBranchClaimGuard
{
    public string? EnsureLiveRemoteBranchAllowed()
    {
        if (!provider.Options.LiveRemoteBranchClaim)
            return null;
        var err = "LiveRemoteBranchClaim forbidden until PROD-004a-e remote branch workers";
        logger.LogError("PROD-004 remote-branch claim: {Error}", err);
        return err;
    }

    public string? ValidatePerformanceBudget(bool withinBudget)
    {
        var err = ProdBranchEvaluator.ValidatePerformanceBudget(withinBudget, provider.Matrix.PerformanceBudgets);
        if (err is not null)
            logger.LogError("PROD-004 budget gate: {Error}", err);
        return err;
    }

    public string? ValidateNoDataLossOnInterrupt(bool interrupted, bool dataPreserved)
    {
        var err = ProdBranchEvaluator.ValidateNoDataLossOnInterrupt(interrupted, dataPreserved);
        if (err is not null)
            logger.LogError("PROD-004 interrupt gate: {Error}", err);
        return err;
    }

    public string? ValidateResumableUpload(bool resumable)
    {
        var err = ProdBranchEvaluator.ValidateResumableUpload(resumable, provider.Matrix.ResumableUploads);
        if (err is not null)
            logger.LogError("PROD-004 resumable gate: {Error}", err);
        return err;
    }

    public string? ValidateDraftSaved(bool draftPresent)
    {
        var err = ProdBranchEvaluator.ValidateDraftSaved(draftPresent, provider.Matrix.SaveDraft);
        if (err is not null)
            logger.LogError("PROD-004 draft gate: {Error}", err);
        return err;
    }

    public string? ValidateIdempotentRetry(bool idempotent)
    {
        var err = ProdBranchEvaluator.ValidateIdempotentRetry(idempotent, provider.Matrix.RetryIdempotency);
        if (err is not null)
            logger.LogError("PROD-004 retry gate: {Error}", err);
        return err;
    }

    public string? ValidateLocalizationReady(bool arabicEnglish, bool rtlLtr)
    {
        var err = ProdBranchEvaluator.ValidateLocalizationReady(arabicEnglish, rtlLtr, provider.Matrix.Localization);
        if (err is not null)
            logger.LogError("PROD-004 locale gate: {Error}", err);
        return err;
    }

    public string? ValidateOfflinePolicy(bool encrypted, bool limited, bool threatModelPresent)
    {
        var err = ProdBranchEvaluator.ValidateOfflinePolicy(
            encrypted, limited, threatModelPresent, provider.Matrix.Offline);
        if (err is not null)
            logger.LogError("PROD-004 offline gate: {Error}", err);
        return err;
    }

    public string? ValidateWcagRequired(bool wcag22Aa)
    {
        var err = ProdBranchEvaluator.ValidateWcagRequired(wcag22Aa, provider.Matrix.Accessibility);
        if (err is not null)
            logger.LogError("PROD-004 a11y gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ProdBranchEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("PROD-004 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ProdBranchSigning? meta)
    {
        var err = ProdBranchEvaluator.ValidateSigning(meta, provider.Matrix.RemoteBranchPolicyVersion);
        if (err is not null)
            logger.LogError("PROD-004 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ProdBranchStartupValidator(
    IProdBranchProvider provider,
    IRemoteBranchClaimGuard claimGuard,
    ProdBranchMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProdBranchStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProdBranchEvaluator.Evaluate(provider.Matrix, opts.LiveRemoteBranchClaim);

        var claimErr = claimGuard.EnsureLiveRemoteBranchAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("PROD-004 remote-branch rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "PROD-004 ProdBranch startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "PROD-004: {Count} remote-branch rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "PROD-004 prod branch active enforce={Enforce} liveRemote={Claim} maxTti={Tti} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveRemoteBranchClaim,
            provider.Matrix.PerformanceBudgets?.MaxTtiMs ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
