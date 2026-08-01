using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.MigOnboarding;

public sealed class MigOnboardingMetrics
{
    public const string MeterName = "HRManagement.MigOnboarding";

    private readonly IMigOnboardingProvider _provider;
    private long _gateCount;
    private long _agencyCount;
    private long _liveClaim;

    public MigOnboardingMetrics(IMigOnboardingProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "mig_onboarding_readiness_gate_count",
            () => _gateCount,
            unit: "{gates}",
            description: "MIG-007 readiness gate count");
        meter.CreateObservableGauge(
            "mig_onboarding_agency_count",
            () => _agencyCount,
            unit: "{agencies}",
            description: "MIG-007 lab agency count");
        meter.CreateObservableGauge(
            "mig_onboarding_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveDualAgencyOnboardClaim is true");
    }

    public void Recount()
    {
        _gateCount = _provider.Matrix.ReadinessGates.Count;
        _agencyCount = _provider.Matrix.Agencies.Count;
        _liveClaim = _provider.Options.LiveDualAgencyOnboardClaim ? 1 : 0;
    }
}

public sealed class AgencyOnboardingClaimGuard(
    IMigOnboardingProvider provider,
    ILogger<AgencyOnboardingClaimGuard> logger) : IAgencyOnboardingClaimGuard
{
    public string? EnsureLiveDualAgencyOnboardAllowed()
    {
        if (!provider.Options.LiveDualAgencyOnboardClaim)
            return null;
        var err = "LiveDualAgencyOnboardClaim forbidden until MIG-007a-d onboarding workers";
        logger.LogError("MIG-007 agency onboarding claim: {Error}", err);
        return err;
    }

    public string? ValidateNofork(bool proposedFork)
    {
        var err = MigOnboardingEvaluator.ValidateNofork(proposedFork, provider.Matrix.Playbook);
        if (err is not null)
            logger.LogError("MIG-007 nofork gate: {Error}", err);
        return err;
    }

    public string? ValidateWaveSignOffs(bool waveStarted, bool signOffsComplete)
    {
        var err = MigOnboardingEvaluator.ValidateWaveSignOffs(
            waveStarted, signOffsComplete, provider.Matrix.WaveCriteria);
        if (err is not null)
            logger.LogError("MIG-007 wave gate: {Error}", err);
        return err;
    }

    public string? ValidateOwners(IEnumerable<string> presentOwners)
    {
        var err = MigOnboardingEvaluator.ValidateOwners(presentOwners, provider.Matrix.RequiredOwners);
        if (err is not null)
            logger.LogError("MIG-007 owners gate: {Error}", err);
        return err;
    }

    public string? ValidateLeadTime(int days, int? targetOverride = null)
    {
        var target = targetOverride ?? provider.Matrix.Telemetry?.LeadTimeTargetDays ?? 0;
        var err = MigOnboardingEvaluator.ValidateLeadTime(days, target);
        if (err is not null)
            logger.LogError("MIG-007 lead-time gate: {Error}", err);
        return err;
    }

    public string? ValidateDefects(int count, int? budgetOverride = null)
    {
        var budget = budgetOverride ?? provider.Matrix.Telemetry?.DefectBudget ?? 0;
        var err = MigOnboardingEvaluator.ValidateDefects(count, budget);
        if (err is not null)
            logger.LogError("MIG-007 defects gate: {Error}", err);
        return err;
    }

    public string? ValidateLivePortalClaim(bool livePortal)
    {
        var err = MigOnboardingEvaluator.ValidateLivePortalClaim(
            livePortal, provider.Matrix.SandboxCertification);
        if (err is not null)
            logger.LogError("MIG-007 portal gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(MigOnboardingSigning? meta)
    {
        var err = MigOnboardingEvaluator.ValidateSigning(meta, provider.Matrix.OnboardingPolicyVersion);
        if (err is not null)
            logger.LogError("MIG-007 signing gate: {Error}", err);
        return err;
    }
}

public sealed class MigOnboardingStartupValidator(
    IMigOnboardingProvider provider,
    IAgencyOnboardingClaimGuard claimGuard,
    MigOnboardingMetrics metrics,
    IHostEnvironment environment,
    ILogger<MigOnboardingStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = MigOnboardingEvaluator.Evaluate(provider.Matrix, opts.LiveDualAgencyOnboardClaim);

        var claimErr = claimGuard.EnsureLiveDualAgencyOnboardAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("MIG-007 onboarding rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "MIG-007 MigOnboarding startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "MIG-007: {Count} onboarding rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "MIG-007 mig onboarding active enforce={Enforce} liveOnboard={Claim} gates={Gates} agencies={Agencies} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveDualAgencyOnboardClaim,
            provider.Matrix.ReadinessGates.Count,
            provider.Matrix.Agencies.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
