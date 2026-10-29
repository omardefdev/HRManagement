using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.LegalControl;

public sealed class LegalControlMetrics
{
    public const string MeterName = "HRManagement.LegalControl";

    private readonly ILegalControlCatalogProvider _provider;
    private long _implemented;
    private long _partial;
    private long _blocked;

    public LegalControlMetrics(ILegalControlCatalogProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "legal_control_status_count",
            ObserveCounts,
            unit: "{controls}",
            description: "STR-002 control counts by status");
        meter.CreateObservableGauge(
            "legal_control_release_profile",
            () => string.Equals(_provider.ReleaseProfile, "NationalClaim", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            unit: "{profile}",
            description: "1=Interim, 2=NationalClaim");
    }

    public void Recount()
    {
        _implemented = _provider.Catalog.Controls.Count(c =>
            string.Equals(c.Status, "Implemented", StringComparison.OrdinalIgnoreCase));
        _partial = _provider.Catalog.Controls.Count(c =>
            string.Equals(c.Status, "Partial", StringComparison.OrdinalIgnoreCase));
        _blocked = _provider.Catalog.Controls.Count(c =>
            string.Equals(c.Status, "Blocked-Legal", StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<Measurement<long>> ObserveCounts()
    {
        yield return new Measurement<long>(_implemented, new KeyValuePair<string, object?>("status", "Implemented"));
        yield return new Measurement<long>(_partial, new KeyValuePair<string, object?>("status", "Partial"));
        yield return new Measurement<long>(_blocked, new KeyValuePair<string, object?>("status", "Blocked-Legal"));
    }
}

public interface ILegalControlGuard
{
    /// <summary>Returns error when domain cannot proceed for release-sensitive work; null if ok.</summary>
    string? EnsureDomainReleaseable(string domain);

    /// <summary>False for Blocked-Legal; Partial requires Active exception.</summary>
    bool IsCapabilityAllowed(string requirementId);

    /// <summary>Payroll start gate for critical payroll/tax controls.</summary>
    string? ValidatePayrollRelease();
}

public sealed class LegalControlGuard(
    ILegalControlCatalogProvider provider,
    LegalControlMetrics metrics,
    ILogger<LegalControlGuard> logger) : ILegalControlGuard
{
    public string? EnsureDomainReleaseable(string domain)
    {
        metrics.Recount();
        var controls = provider.GetControlsByDomain(domain);
        foreach (var c in controls)
        {
            if (string.Equals(c.Status, "Implemented", StringComparison.OrdinalIgnoreCase)
                && LegalControlCatalogEvaluator.ControlBlobContainsUnresolvedLegalMarker(c))
            {
                return $"STR-002: domain '{domain}' has Implemented control {c.RequirementId} referencing unresolved legal marker";
            }

            if (string.Equals(c.Status, "Partial", StringComparison.OrdinalIgnoreCase)
                && !provider.IsExceptionActive(c.ExceptionId))
            {
                var msg =
                    $"STR-002: domain '{domain}' Partial control {c.RequirementId} lacks Active exception";
                logger.LogWarning("{Message}", msg);
                return msg;
            }
        }

        return null;
    }

    public bool IsCapabilityAllowed(string requirementId) =>
        LegalControlCatalogEvaluator.IsCapabilityAllowed(
            provider.Catalog,
            requirementId,
            provider.IsExceptionActive);

    public string? ValidatePayrollRelease()
    {
        metrics.Recount();
        var err = LegalControlCatalogEvaluator.ValidatePayrollCriticalControls(
            provider.Catalog,
            provider.IsExceptionActive);
        if (err is not null)
            logger.LogError("{Message}", err);
        return err;
    }
}

/// <summary>Fail-fast catalog / profile / legislative-watch rules when EnforceOnStartup is set.</summary>
public sealed class LegalControlStartupValidator(
    ILegalControlCatalogProvider provider,
    LegalControlMetrics metrics,
    IHostEnvironment environment,
    ILogger<LegalControlStartupValidator> logger,
    TimeProvider timeProvider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var evaluateWatch = opts.EnforceLegislativeWatch
                            && environment.IsProduction();
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var failures = LegalControlCatalogEvaluator.Evaluate(
            provider.Catalog,
            provider.ReleaseProfile,
            opts.AllowBlockedLegalNationalClaim,
            provider.WatchStamp,
            utcNow,
            evaluateLegislativeWatch: evaluateWatch);

        // Staging: warn on overdue watch when enforce is on, but do not fail solely for watch.
        if (opts.EnforceOnStartup
            && environment.IsStaging()
            && opts.EnforceLegislativeWatch
            && provider.WatchStamp is not null
            && GovernanceDateParser.TryParseUtcDate(provider.WatchStamp.NextDue, out var nextDue)
            && nextDue < utcNow.Date)
        {
            logger.LogWarning(
                "STR-002: legislative watch overdue (nextDue={NextDue}) — warning only in Staging",
                provider.WatchStamp.NextDue);
        }

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("STR-002 catalog rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "STR-002 LegalControl startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "STR-002: {Count} catalog rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        var byStatus = provider.Catalog.Controls
            .GroupBy(c => c.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "STR-002 legal control active profile={Profile} enforce={Enforce} implemented={Implemented} partial={Partial} blockedLegal={Blocked}",
            provider.ReleaseProfile,
            opts.EnforceOnStartup,
            byStatus.GetValueOrDefault("Implemented", 0),
            byStatus.GetValueOrDefault("Partial", 0),
            byStatus.GetValueOrDefault("Blocked-Legal", 0));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
