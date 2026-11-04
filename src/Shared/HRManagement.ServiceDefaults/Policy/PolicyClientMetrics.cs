using System.Diagnostics.Metrics;

namespace HRManagement.ServiceDefaults.Policy;

/// <summary>PLAN-42: counters of the remote enforce client. Tags hold fixed names only, never a request value.</summary>
public sealed class PolicyClientMetrics
{
    public const string MeterName = "HRManagement.Policy";

    private readonly Counter<long> _failures;
    private readonly Counter<long> _modeMismatches;

    public PolicyClientMetrics(IMeterFactory? meterFactory = null)
    {
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _failures = meter.CreateCounter<long>("policy_client_failure_total");
        _modeMismatches = meter.CreateCounter<long>("policy_mode_mismatch_total");
    }

    public void Failure(string domain, string mode, string reason) =>
        _failures.Add(
            1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("reason", reason));

    public void ModeMismatch(string domain) =>
        _modeMismatches.Add(1, new KeyValuePair<string, object?>("domain", domain));
}
