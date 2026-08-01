using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Ownership;

public sealed class OwnershipMetrics
{
    public const string MeterName = "HRManagement.ProgramOwnership";

    private readonly IOwnershipProvider _provider;
    private long _criticalComponents;
    private long _avgBusFactorTenths;
    private long _national24x7Claim;

    public OwnershipMetrics(IOwnershipProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "program_ownership_critical_component_count",
            () => _criticalComponents,
            unit: "{components}",
            description: "STR-006 critical component count");
        meter.CreateObservableGauge(
            "program_ownership_avg_bus_factor_tenths",
            () => _avgBusFactorTenths,
            unit: "{tenths}",
            description: "Average busFactor * 10 for critical components");
        meter.CreateObservableGauge(
            "program_ownership_national_24x7_claim",
            () => _national24x7Claim,
            unit: "{bool}",
            description: "1 when National24x7Claim is true");
    }

    public void Recount()
    {
        var critical = _provider.Matrix.Components.Where(c => c.Critical).ToList();
        _criticalComponents = critical.Count;
        _avgBusFactorTenths = critical.Count == 0
            ? 0
            : (long)Math.Round(critical.Average(c => c.BusFactor) * 10);
        _national24x7Claim = _provider.Options.National24x7Claim
                             || (_provider.Matrix.Staffing?.National24x7Claim ?? false)
            ? 1
            : 0;
    }
}

public sealed class SustainmentClaimGuard(
    IOwnershipProvider provider,
    ILogger<SustainmentClaimGuard> logger) : ISustainmentClaimGuard
{
    public string? EnsureNational24x7Allowed()
    {
        var claim = provider.Options.National24x7Claim
                    || (provider.Matrix.Staffing?.National24x7Claim ?? false);
        if (!claim)
            return null;

        var sre = provider.Matrix.Blockers.FirstOrDefault(b =>
            string.Equals(b.Id, "SRE-001", StringComparison.OrdinalIgnoreCase));
        if (sre is null || string.Equals(sre.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            var err = "National24x7Claim forbidden while SRE-001 is Blocked";
            logger.LogError("STR-006 sustainment claim: {Error}", err);
            return err;
        }

        return null;
    }

    public string? ValidateBudgetShape(IEnumerable<OwnershipBudgetLine> budgetLines)
    {
        var failures = OwnershipEvaluator.EvaluateBudget(budgetLines, provider.Matrix.BudgetRule);
        if (failures.Count == 0)
            return null;
        var err = string.Join("; ", failures);
        logger.LogError("STR-006 budget gate: {Error}", err);
        return err;
    }

    public string? ValidateVendor(OwnershipVendor vendor)
    {
        var err = OwnershipEvaluator.ValidateVendor(vendor, provider.Matrix.RequiredVendorClauses);
        if (err is not null)
            logger.LogError("STR-006 vendor gate: {Error}", err);
        return err;
    }
}

public sealed class OwnershipStartupValidator(
    IOwnershipProvider provider,
    ISustainmentClaimGuard claimGuard,
    OwnershipMetrics metrics,
    IHostEnvironment environment,
    ILogger<OwnershipStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = OwnershipEvaluator.Evaluate(provider.Matrix, opts.National24x7Claim);

        var claimErr = claimGuard.EnsureNational24x7Allowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("STR-006 ownership rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "STR-006 ProgramOwnership startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "STR-006: {Count} ownership rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        var critical = provider.Matrix.Components.Count(c => c.Critical);
        logger.LogInformation(
            "STR-006 program ownership active enforce={Enforce} national24x7={Claim} critical={Critical} vendors={Vendors} env={Env}",
            opts.EnforceOnStartup,
            opts.National24x7Claim || (provider.Matrix.Staffing?.National24x7Claim ?? false),
            critical,
            provider.Matrix.Vendors.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
