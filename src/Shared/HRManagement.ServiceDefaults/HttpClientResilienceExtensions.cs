using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>G7: default timeout + circuit breaker for outbound service HTTP clients.</summary>
public static class HttpClientResilienceExtensions
{
    public static IHttpClientBuilder AddHrOutboundResilience(
        this IHttpClientBuilder builder,
        int timeoutSeconds = 30,
        int circuitBreakFailures = 5,
        int circuitBreakSeconds = 30)
    {
        var timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 120)));

        var breaker = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode >= 500)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: Math.Clamp(circuitBreakFailures, 2, 50),
                durationOfBreak: TimeSpan.FromSeconds(Math.Clamp(circuitBreakSeconds, 5, 300)));

        return builder
            .AddPolicyHandler(timeout)
            .AddPolicyHandler(breaker);
    }
}
