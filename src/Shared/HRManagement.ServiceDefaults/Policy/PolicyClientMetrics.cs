using System.Diagnostics.Metrics;

namespace HRManagement.ServiceDefaults.Policy;

/// <summary>
/// PLAN-42: counters of the remote enforce client. PLAN-49: and the duration of each call to HR.Core, because Shadow makes the
/// call on every asset, card and leave action while it changes no outcome: its cost is this number. Tags hold fixed names only
/// (the domain, the mode, the outcome), never a request value.
/// </summary>
public sealed class PolicyClientMetrics
{
    public const string MeterName = "HRManagement.Policy";
    public const string CallDurationName = "policy_client_call_duration_ms";

    private readonly Counter<long> _failures;
    private readonly Counter<long> _modeMismatches;
    private readonly Histogram<double> _callDuration;

    public PolicyClientMetrics(IMeterFactory? meterFactory = null)
    {
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _failures = meter.CreateCounter<long>("policy_client_failure_total");
        _modeMismatches = meter.CreateCounter<long>("policy_mode_mismatch_total");
        _callDuration = meter.CreateHistogram<double>(CallDurationName, "ms", "Duration of a policy enforce call to HR.Core");
    }

    public void Failure(string domain, string mode, string reason) =>
        _failures.Add(
            1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("reason", reason));

    public void ModeMismatch(string domain) =>
        _modeMismatches.Add(1, new KeyValuePair<string, object?>("domain", domain));

    /// <param name="outcome"><c>ok</c>, or the failure reason the client reports (<c>timeout</c>, <c>http-503</c>, ...).</param>
    public void CallDuration(string domain, string mode, string outcome, double milliseconds) =>
        _callDuration.Record(
            milliseconds,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("outcome", outcome));
}
