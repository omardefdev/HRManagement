using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProdEss;

public sealed class ProdEssMetrics
{
    public const string MeterName = "HRManagement.ProdEss";

    private readonly IProdEssProvider _provider;
    private long _auditFieldCount;
    private long _journeyCount;
    private long _liveClaim;
    private long _policyFlags;

    public ProdEssMetrics(IProdEssProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "prod_ess_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "PROD-003 audit required field count");
        meter.CreateObservableGauge(
            "prod_ess_journey_count",
            () => _journeyCount,
            unit: "{journeys}",
            description: "PROD-003 high-volume journey count");
        meter.CreateObservableGauge(
            "prod_ess_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "PROD-003 core policy flags asserted");
        meter.CreateObservableGauge(
            "prod_ess_live_employee_selfservice_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveEmployeeSelfServiceClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _journeyCount = _provider.Matrix.HighVolumeJourneys?.Journeys.Count ?? 0;
        _liveClaim = _provider.Options.LiveEmployeeSelfServiceClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.HighVolumeJourneys?.ExceptionOnlyBackOffice == true ? 1 : 0) +
            (m.CorrectionVisibility?.SourceVisible == true ? 1 : 0) +
            (m.ManagerScope?.OutsideScopeForbidden == true ? 1 : 0) +
            (m.UsageTelemetry?.LiveForbidden == true ? 1 : 0) +
            (m.MockSandbox?.LiveEmployeePortalForbidden == true ? 1 : 0);
    }
}

public sealed class EmployeeSelfServiceClaimGuard(
    IProdEssProvider provider,
    ILogger<EmployeeSelfServiceClaimGuard> logger) : IEmployeeSelfServiceClaimGuard
{
    public string? EnsureLiveEssAllowed()
    {
        if (!provider.Options.LiveEmployeeSelfServiceClaim)
            return null;
        var err = "LiveEmployeeSelfServiceClaim forbidden until PROD-003a-e employee selfservice workers";
        logger.LogError("PROD-003 ESS claim: {Error}", err);
        return err;
    }

    public string? ValidateHighVolumeJourney(bool completedWithoutBackOffice, bool isException)
    {
        var err = ProdEssEvaluator.ValidateHighVolumeJourney(
            completedWithoutBackOffice, isException, provider.Matrix.HighVolumeJourneys);
        if (err is not null)
            logger.LogError("PROD-003 high-volume gate: {Error}", err);
        return err;
    }

    public string? ValidateCorrectionVisibility(bool sourceVisible, bool statusVisible)
    {
        var err = ProdEssEvaluator.ValidateCorrectionVisibility(
            sourceVisible, statusVisible, provider.Matrix.CorrectionVisibility);
        if (err is not null)
            logger.LogError("PROD-003 correction gate: {Error}", err);
        return err;
    }

    public string? ValidateManagerScope(bool withinScope)
    {
        var err = ProdEssEvaluator.ValidateManagerScope(withinScope, provider.Matrix.ManagerScope);
        if (err is not null)
            logger.LogError("PROD-003 manager-scope gate: {Error}", err);
        return err;
    }

    public string? ValidateUsageMeasured(bool telemetryPresent)
    {
        var err = ProdEssEvaluator.ValidateUsageMeasured(telemetryPresent, provider.Matrix.UsageTelemetry);
        if (err is not null)
            logger.LogError("PROD-003 usage gate: {Error}", err);
        return err;
    }

    public string? ValidateLostSessionHandled(bool sessionLost, bool recoveredSafely)
    {
        var err = ProdEssEvaluator.ValidateLostSessionHandled(
            sessionLost, recoveredSafely, provider.Matrix.DeviceSession);
        if (err is not null)
            logger.LogError("PROD-003 lost-session gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ProdEssEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("PROD-003 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ProdEssSigning? meta)
    {
        var err = ProdEssEvaluator.ValidateSigning(meta, provider.Matrix.EmployeeSelfServicePolicyVersion);
        if (err is not null)
            logger.LogError("PROD-003 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ProdEssStartupValidator(
    IProdEssProvider provider,
    IEmployeeSelfServiceClaimGuard claimGuard,
    ProdEssMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProdEssStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProdEssEvaluator.Evaluate(provider.Matrix, opts.LiveEmployeeSelfServiceClaim);

        var claimErr = claimGuard.EnsureLiveEssAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("PROD-003 employee-selfservice rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "PROD-003 ProdEss startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "PROD-003: {Count} employee-selfservice rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "PROD-003 prod ess active enforce={Enforce} liveEss={Claim} journeys={Journeys} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveEmployeeSelfServiceClaim,
            provider.Matrix.HighVolumeJourneys?.Journeys.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
