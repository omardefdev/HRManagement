using MassTransit;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Sets <see cref="CorrelationIdFlow"/> from inbound message headers before consumer execution.
/// </summary>
public sealed class CorrelationIdConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationId");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var correlationId = context.Headers.Get<string>(CorrelationIdMiddleware.HeaderName)
            ?? context.CorrelationId?.ToString();

        if (!string.IsNullOrWhiteSpace(correlationId))
            CorrelationIdFlow.Set(correlationId);

        try
        {
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
                    await next.Send(context);
            }
            else
            {
                await next.Send(context);
            }
        }
        finally
        {
            CorrelationIdFlow.Set(null);
        }
    }
}
