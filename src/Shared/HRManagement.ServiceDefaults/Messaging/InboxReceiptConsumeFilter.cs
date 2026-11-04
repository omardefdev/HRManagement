using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using MassTransit.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>Marks a bus whose endpoints run the consumer outbox: <see cref="InboxConsumeFilter{T}"/> then only passes through.</summary>
public sealed record ConsumerOutboxRegistration(Type DbContextType);

/// <summary>
/// PLAN-40 (§4, P-1): the inbox receipt in join mode. MassTransit's consumer outbox runs two transactions per message: the
/// first only inserts <c>InboxState</c> and commits, the second runs the consumer. A receipt staged before the outbox would
/// be committed by the first one, and a consumer that then failed would be skipped as a duplicate on retry. So this filter
/// sits inside the outbox, on the consumer and saga message pipes: it opens no transaction, skips a message that already has
/// a receipt, and stages the receipt after the consumer, so the outbox's <c>SaveChanges</c> commits it with the business
/// state and the <c>OutboxMessage</c> rows.
/// </summary>
public sealed class InboxReceiptConsumeFilter<TDbContext, TMessage> : IFilter<ConsumeContext<TMessage>>
    where TDbContext : DbContext
    where TMessage : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("inboxReceipt");

    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        var provider = context.GetPayload<IServiceProvider>();
        var db = provider.GetRequiredService<TDbContext>();
        if (db.Model.FindEntityType(typeof(IntegrationMessageReceipt)) is null)
        {
            await next.Send(context);
            return;
        }

        var metrics = provider.GetRequiredService<InboxMetrics>();
        var messageId = IntegrationMessageInboxExtensions.RequireMessageId(context);
        var consumerType = IntegrationMessageInboxExtensions.ResolveConsumerType(context);
        var tenantId = IntegrationMessageInboxExtensions.ResolveInboxTenantId(provider.GetService<ITenantContext>());

        if (await db.HasReceiptAsync(messageId, consumerType, tenantId, context.CancellationToken))
        {
            metrics.DuplicateSkip.Add(1);
            provider.GetService<ILogger<InboxReceiptConsumeFilter<TDbContext, TMessage>>>()?.LogDebug(
                "Inbox skip duplicate {ConsumerType} MessageId={MessageId} Tenant={TenantId}",
                consumerType, messageId, tenantId);
            return;
        }

        await next.Send(context);

        db.StageReceipt(messageId, consumerType, tenantId);
        metrics.Processed.Add(1);
    }
}

/// <summary>Adds <see cref="InboxReceiptConsumeFilter{TDbContext,TMessage}"/> to every consumer and saga message pipe of an endpoint.</summary>
internal sealed class InboxReceiptConfigurationObserver<TDbContext> : IConsumerConfigurationObserver, ISagaConfigurationObserver
    where TDbContext : DbContext
{
    public void ConsumerConfigured<TConsumer>(IConsumerConfigurator<TConsumer> configurator)
        where TConsumer : class
    {
    }

    public void ConsumerMessageConfigured<TConsumer, TMessage>(IConsumerMessageConfigurator<TConsumer, TMessage> configurator)
        where TConsumer : class
        where TMessage : class
    {
        if (configurator is not IConsumerMessageConfigurator<TMessage> message)
            throw new ConfigurationException($"The inbox receipt filter could not be added: {typeof(TConsumer).Name} - {typeof(TMessage).Name}");

        AddFilter(message);
    }

    public void SagaConfigured<TSaga>(ISagaConfigurator<TSaga> configurator)
        where TSaga : class, ISaga
    {
    }

    public void StateMachineSagaConfigured<TInstance>(ISagaConfigurator<TInstance> configurator, SagaStateMachine<TInstance> stateMachine)
        where TInstance : class, ISaga, SagaStateMachineInstance
    {
    }

    public void SagaMessageConfigured<TSaga, TMessage>(ISagaMessageConfigurator<TSaga, TMessage> configurator)
        where TSaga : class, ISaga
        where TMessage : class
    {
        if (configurator is not ISagaMessageConfigurator<TMessage> message)
            throw new ConfigurationException($"The inbox receipt filter could not be added: {typeof(TSaga).Name} - {typeof(TMessage).Name}");

        AddFilter(message);
    }

    private static void AddFilter<TMessage>(IPipeConfigurator<ConsumeContext<TMessage>> configurator)
        where TMessage : class =>
        configurator.AddPipeSpecification(
            new FilterPipeSpecification<ConsumeContext<TMessage>>(new InboxReceiptConsumeFilter<TDbContext, TMessage>()));
}
