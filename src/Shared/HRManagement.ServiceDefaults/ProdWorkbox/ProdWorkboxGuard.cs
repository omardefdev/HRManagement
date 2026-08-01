using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProdWorkbox;

public sealed class ProdWorkboxMetrics
{
    public const string MeterName = "HRManagement.ProdWorkbox";

    private readonly IProdWorkboxProvider _provider;
    private long _auditFieldCount;
    private long _itemTypeCount;
    private long _liveClaim;
    private long _policyFlags;

    public ProdWorkboxMetrics(IProdWorkboxProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "prod_workbox_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "PROD-005 audit required field count");
        meter.CreateObservableGauge(
            "prod_workbox_item_type_count",
            () => _itemTypeCount,
            unit: "{types}",
            description: "PROD-005 queue item type count");
        meter.CreateObservableGauge(
            "prod_workbox_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "PROD-005 core policy flags asserted");
        meter.CreateObservableGauge(
            "prod_workbox_live_workbox_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveWorkboxClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _itemTypeCount = _provider.Matrix.QueueCompleteness?.ItemTypes.Count ?? 0;
        _liveClaim = _provider.Options.LiveWorkboxClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.QueueCompleteness?.NoLostCrossModule == true ? 1 : 0) +
            (m.SafeSearch?.ExistenceLeakForbidden == true ? 1 : 0) +
            (m.BulkActions?.FourEyesRequired == true ? 1 : 0) +
            (m.Notifications?.LiveForbidden == true ? 1 : 0) +
            (m.MockSandbox?.LiveWorkboxForbidden == true ? 1 : 0);
    }
}

public sealed class WorkboxClaimGuard(
    IProdWorkboxProvider provider,
    ILogger<WorkboxClaimGuard> logger) : IWorkboxClaimGuard
{
    public string? EnsureLiveWorkboxAllowed()
    {
        if (!provider.Options.LiveWorkboxClaim)
            return null;
        var err = "LiveWorkboxClaim forbidden until PROD-005a-e workbox workers";
        logger.LogError("PROD-005 workbox claim: {Error}", err);
        return err;
    }

    public string? ValidateNoLostTransaction(bool persistedAcrossModules)
    {
        var err = ProdWorkboxEvaluator.ValidateNoLostTransaction(
            persistedAcrossModules, provider.Matrix.QueueCompleteness);
        if (err is not null)
            logger.LogError("PROD-005 queue gate: {Error}", err);
        return err;
    }

    public string? ValidateSafeSearch(bool withinTenant, bool withinOrg, bool existenceLeakAttempted)
    {
        var err = ProdWorkboxEvaluator.ValidateSafeSearch(
            withinTenant, withinOrg, existenceLeakAttempted, provider.Matrix.SafeSearch);
        if (err is not null)
            logger.LogError("PROD-005 search gate: {Error}", err);
        return err;
    }

    public string? ValidateBulkApprove(bool withinLimits, bool fourEyesSatisfied)
    {
        var err = ProdWorkboxEvaluator.ValidateBulkApprove(
            withinLimits, fourEyesSatisfied, provider.Matrix.BulkActions);
        if (err is not null)
            logger.LogError("PROD-005 bulk gate: {Error}", err);
        return err;
    }

    public string? ValidateNotificationSafe(bool containsSecret, bool hasDeliveryReceipt, bool leadsToCorrectState)
    {
        var err = ProdWorkboxEvaluator.ValidateNotificationSafe(
            containsSecret, hasDeliveryReceipt, leadsToCorrectState, provider.Matrix.Notifications);
        if (err is not null)
            logger.LogError("PROD-005 notify gate: {Error}", err);
        return err;
    }

    public string? ValidateDeepLink(bool expired, bool reauthPerformed)
    {
        var err = ProdWorkboxEvaluator.ValidateDeepLink(expired, reauthPerformed, provider.Matrix.DeepLinks);
        if (err is not null)
            logger.LogError("PROD-005 deeplink gate: {Error}", err);
        return err;
    }

    public string? ValidateEscalationTracked(bool escalationPresent)
    {
        var err = ProdWorkboxEvaluator.ValidateEscalationTracked(
            escalationPresent, provider.Matrix.PrioritySlaEscalation);
        if (err is not null)
            logger.LogError("PROD-005 escalation gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ProdWorkboxEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("PROD-005 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ProdWorkboxSigning? meta)
    {
        var err = ProdWorkboxEvaluator.ValidateSigning(meta, provider.Matrix.WorkboxPolicyVersion);
        if (err is not null)
            logger.LogError("PROD-005 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ProdWorkboxStartupValidator(
    IProdWorkboxProvider provider,
    IWorkboxClaimGuard claimGuard,
    ProdWorkboxMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProdWorkboxStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProdWorkboxEvaluator.Evaluate(provider.Matrix, opts.LiveWorkboxClaim);

        var claimErr = claimGuard.EnsureLiveWorkboxAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("PROD-005 workbox rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "PROD-005 ProdWorkbox startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "PROD-005: {Count} workbox rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "PROD-005 prod workbox active enforce={Enforce} liveWorkbox={Claim} itemTypes={Types} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveWorkboxClaim,
            provider.Matrix.QueueCompleteness?.ItemTypes.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
