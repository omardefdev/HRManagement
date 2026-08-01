using System.Diagnostics.Metrics;
using Polly;
using Polly.Extensions.Http;

namespace HRManagement.Gateway.Services;

/// <summary>NFR-002: visible circuit breaker for upstream health probes (failures surface to callers).</summary>
public static class GatewayHttpResilience
{
    public const string HealthProbeClientName = "gateway-health-probe";

    private static readonly Meter Meter = new("hrm.gateway.resilience", "1.0.0");
    private static readonly Counter<long> CircuitOpened = Meter.CreateCounter<long>("circuit_breaker_opened");

    public static IHttpClientBuilder AddGatewayHealthProbeClient(this IServiceCollection services, IConfiguration configuration)
    {
        var handledBeforeBreak = Math.Clamp(configuration.GetValue("Gateway:HealthProbe:CircuitBreakFailures", 5), 2, 50);
        var durationSeconds = Math.Clamp(configuration.GetValue("Gateway:HealthProbe:CircuitBreakSeconds", 30), 5, 300);

        var breaker = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode >= 500)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: handledBeforeBreak,
                durationOfBreak: TimeSpan.FromSeconds(durationSeconds),
                onBreak: (outcome, breakDelay) =>
                {
                    CircuitOpened.Add(1);
                    Serilog.Log.Warning(
                        "Gateway health-probe circuit OPEN for {Break} after failures (status={Status}); callers see BrokenCircuitException — NFR-002",
                        breakDelay,
                        outcome.Result?.StatusCode);
                },
                onReset: () => Serilog.Log.Information("Gateway health-probe circuit RESET"),
                onHalfOpen: () => Serilog.Log.Information("Gateway health-probe circuit HALF-OPEN"));

        return services.AddHttpClient(HealthProbeClientName)
            .AddPolicyHandler(breaker);
    }
}
