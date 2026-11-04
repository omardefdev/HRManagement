using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>PLAN-40 (D-40-6): tells a transient failure, worth a retry, from a business failure.</summary>
public static class MessagingFaults
{
    /// <summary>The retry count of the bus when the configuration does not set <c>MassTransit:RetryCount</c>.</summary>
    public const int DefaultRetryCount = 3;

    public static bool IsTransient(Exception exception, CancellationToken callerToken = default)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case DbUpdateConcurrencyException:
                case TimeoutException:
                case HttpRequestException:
                case NpgsqlException { IsTransient: true }:
                case TaskCanceledException when !callerToken.IsCancellationRequested:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a party acknowledgement should rethrow <paramref name="exception"/> without publishing: a cancelled consume,
    /// or a transient failure while retries remain (<c>GetRetryAttempt</c> is 0 on the first delivery).
    /// </summary>
    public static bool ShouldRetry(Exception exception, ConsumeContext context)
    {
        if (exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
            return true;

        return IsTransient(exception, context.CancellationToken) && context.GetRetryAttempt() < RetryCount(context);
    }

    private static int RetryCount(ConsumeContext context)
    {
        var configuration = context.TryGetPayload<IServiceProvider>(out var provider)
            ? provider.GetService<IConfiguration>()
            : null;
        return Math.Max(0, configuration?.GetValue("MassTransit:RetryCount", DefaultRetryCount) ?? DefaultRetryCount);
    }
}
