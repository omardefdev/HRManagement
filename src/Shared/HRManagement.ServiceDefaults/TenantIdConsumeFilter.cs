using MassTransit;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Sets <see cref="TenantIdFlow"/> from inbound message headers before consumer execution.
/// </summary>
public sealed class TenantIdConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("tenantId");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var header = context.Headers.Get<string>(TenantIdMiddleware.HeaderName);
        if (Guid.TryParse(header, out var id))
            TenantIdFlow.Set(id);

        try
        {
            await next.Send(context);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }
}
