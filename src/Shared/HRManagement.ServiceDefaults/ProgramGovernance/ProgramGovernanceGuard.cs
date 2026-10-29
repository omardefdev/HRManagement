using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProgramGovernance;

public sealed class ProgramGovernanceMetrics
{
    public const string MeterName = "HRManagement.ProgramGovernance";

    private readonly IProgramGovernanceProvider _provider;
    private readonly TimeProvider _timeProvider;
    private long _decisions;
    private long _activeExceptions;
    private long _goLiveDeclared;

    public ProgramGovernanceMetrics(
        IProgramGovernanceProvider provider,
        IMeterFactory? meterFactory = null,
        TimeProvider? timeProvider = null)
    {
        _provider = provider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "program_governance_decision_count",
            () => _decisions,
            unit: "{decisions}",
            description: "STR-005 decision-log row count");
        meter.CreateObservableGauge(
            "program_governance_active_exception_count",
            () => _activeExceptions,
            unit: "{exceptions}",
            description: "Declared exceptions not yet expired");
        meter.CreateObservableGauge(
            "program_governance_golive_declared",
            () => _goLiveDeclared,
            unit: "{bool}",
            description: "1 when GoLiveDeclared is true");
    }

    public void Recount()
    {
        _decisions = _provider.DecisionLog.Decisions.Count;
        var now = _timeProvider.GetUtcNow().UtcDateTime.Date;
        _activeExceptions = _provider.Matrix.Exceptions.Count(ex =>
        {
            if (!string.Equals(ex.Status, "Declared", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!GovernanceDateParser.TryParseUtcDate(ex.ExpiresOn, out var expires))
                return true;
            return expires >= now;
        });
        _goLiveDeclared = _provider.Options.GoLiveDeclared ? 1 : 0;
    }
}

public sealed class GoLiveSeatGuard(
    IProgramGovernanceProvider provider,
    ILogger<GoLiveSeatGuard> logger) : IGoLiveSeatGuard
{
    public string? ValidateEvidence(GoLiveEvidenceDocument? evidence)
    {
        var err = ProgramGovernanceEvaluator.ValidateGoLiveEvidence(provider.Matrix, evidence);
        if (err is not null)
            logger.LogError("STR-005 Go-Live seat gate: {Error}", err);
        return err;
    }

    public string? EnsureGoLiveAllowed()
    {
        if (!provider.Options.GoLiveDeclared)
            return null;
        return ValidateEvidence(provider.GoLiveEvidence);
    }
}

public sealed class ProgramGovernanceStartupValidator(
    IProgramGovernanceProvider provider,
    IGoLiveSeatGuard goLiveGuard,
    ProgramGovernanceMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProgramGovernanceStartupValidator> logger,
    TimeProvider timeProvider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProgramGovernanceEvaluator.Evaluate(
            provider.Matrix,
            provider.DecisionLog,
            opts.GoLiveDeclared,
            provider.GoLiveEvidence,
            timeProvider.GetUtcNow().UtcDateTime);

        // EnsureGoLiveAllowed is covered by Evaluate when GoLiveDeclared; call for logging clarity.
        var goLiveErr = goLiveGuard.EnsureGoLiveAllowed();
        if (goLiveErr is not null && !failures.Contains(goLiveErr))
            failures.Add(goLiveErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("STR-005 governance rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "STR-005 ProgramGovernance startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "STR-005: {Count} governance rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "STR-005 program governance active enforce={Enforce} goLiveDeclared={GoLive} decisions={Decisions} exceptions={Exceptions} env={Env}",
            opts.EnforceOnStartup,
            opts.GoLiveDeclared,
            provider.DecisionLog.Decisions.Count,
            provider.Matrix.Exceptions.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
