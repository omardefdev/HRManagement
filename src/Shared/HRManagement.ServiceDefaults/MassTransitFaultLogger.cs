using MassTransit;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

public sealed class MassTransitFaultLogger(ILogger<MassTransitFaultLogger> logger) : IConsumeObserver
{
    public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        logger.LogError(
            exception,
            "MassTransit consumer fault for {MessageType} MessageId={MessageId} — message will retry or move to _error queue",
            typeof(T).Name,
            context.MessageId);
        return Task.CompletedTask;
    }
}
