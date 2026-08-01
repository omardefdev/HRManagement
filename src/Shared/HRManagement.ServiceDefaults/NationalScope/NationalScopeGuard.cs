using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.NationalScope;

public sealed class NationalScopeMetrics
{
    public const string MeterName = "HRManagement.NationalScope";

    private readonly INationalScopeProvider _scope;
    private long _tenantUtilizationMilli;
    private long _payrollUtilizationMilli;

    public NationalScopeMetrics(INationalScopeProvider scope, IMeterFactory? meterFactory = null)
    {
        _scope = scope;
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "national_scope_envelope_active",
            () => string.Equals(_scope.ActiveEnvelope, "national-competition", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            unit: "{envelope}",
            description: "1=syria-interim, 2=national-competition");
        meter.CreateObservableGauge(
            "national_scope_utilization_ratio",
            ObserveUtilization,
            unit: "{ratio_milli}",
            description: "Utilization milli-ratio (0-1000+) for tracked ceilings");
    }

    public void ReportTenantUtilization(long currentCount)
    {
        var ceiling = _scope.TenantCeiling;
        if (ceiling is null or <= 0) { _tenantUtilizationMilli = 0; return; }
        _tenantUtilizationMilli = (long)Math.Round(currentCount / ceiling.Value * 1000m);
    }

    public void ReportPayrollUtilization(long personCount)
    {
        var ceiling = _scope.PayrollWindowPersonsCeiling;
        if (ceiling is null or <= 0) { _payrollUtilizationMilli = 0; return; }
        _payrollUtilizationMilli = (long)Math.Round(personCount / ceiling.Value * 1000m);
    }

    private IEnumerable<Measurement<long>> ObserveUtilization()
    {
        yield return new Measurement<long>(_tenantUtilizationMilli, new KeyValuePair<string, object?>("indicator", "tenants"));
        yield return new Measurement<long>(_payrollUtilizationMilli, new KeyValuePair<string, object?>("indicator", "payroll_window_persons"));
    }
}

/// <summary>Validates Production cannot claim national-competition without AllowNationalCompetitionClaim.</summary>
public sealed class NationalScopeStartupValidator(
    INationalScopeProvider scope,
    IHostEnvironment environment,
    ILogger<NationalScopeStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = scope.Options;
        var isProd = environment.IsProduction();
        var isNational = string.Equals(scope.ActiveEnvelope, "national-competition", StringComparison.OrdinalIgnoreCase);

        if (isProd && isNational && !opts.AllowNationalCompetitionClaim)
        {
            throw new InvalidOperationException(
                "STR-001: Production cannot use ActiveEnvelope=national-competition without NationalScope:AllowNationalCompetitionClaim=true (requires CAP-001).");
        }

        if (isNational && !opts.AllowNationalCompetitionClaim)
        {
            logger.LogWarning(
                "STR-001: ActiveEnvelope is national-competition but AllowNationalCompetitionClaim is false. Forbidden in Production.");
        }

        logger.LogInformation(
            "STR-001 national scope active envelope={Envelope} country={Country} currency={Currency} labScale={Lab} hardEnforce={Hard}",
            scope.ActiveEnvelope,
            opts.CountryCode,
            scope.ResolvedCurrencyCode,
            opts.IsLabScale,
            opts.HardEnforceCeilings);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface INationalScopeGuard
{
    /// <summary>Returns error message when hard-enforce and over ceiling; otherwise null (may log warning).</summary>
    string? CheckTenantCount(long currentTenantCountAfterAdd);

    /// <summary>Returns error when hard-enforce and payroll persons exceed envelope window.</summary>
    string? CheckPayrollPersonCount(long personCount);
}

public sealed class NationalScopeGuard(
    INationalScopeProvider scope,
    NationalScopeMetrics metrics,
    ILogger<NationalScopeGuard> logger) : INationalScopeGuard
{
    public string? CheckTenantCount(long currentTenantCountAfterAdd)
    {
        metrics.ReportTenantUtilization(currentTenantCountAfterAdd);
        var ceiling = scope.TenantCeiling;
        if (ceiling is null) return null;
        if (currentTenantCountAfterAdd <= ceiling) return null;

        var msg =
            $"STR-001 envelope tenant ceiling exceeded: count={currentTenantCountAfterAdd}, ceiling={ceiling} (envelope={scope.ActiveEnvelope}).";
        if (scope.Options.HardEnforceCeilings)
            return msg;

        logger.LogWarning("{Message}", msg);
        return null;
    }

    public string? CheckPayrollPersonCount(long personCount)
    {
        metrics.ReportPayrollUtilization(personCount);
        var ceiling = scope.PayrollWindowPersonsCeiling;
        if (ceiling is null) return null;
        if (personCount <= ceiling) return null;

        var msg =
            $"STR-001 envelope payroll window exceeded: persons={personCount}, ceiling={ceiling} (envelope={scope.ActiveEnvelope}).";
        if (scope.Options.HardEnforceCeilings)
            return msg;

        logger.LogWarning("{Message}", msg);
        return null;
    }
}
