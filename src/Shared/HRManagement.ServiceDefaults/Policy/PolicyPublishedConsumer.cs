using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Policy;

public sealed class PolicyPublishedConsumer(
    IPolicySnapshotStore snapshotStore,
    IPolicyEnforceClient policyClient,
    ILogger<PolicyPublishedConsumer> logger) : IConsumer<IPolicyPublished>
{
    public async Task Consume(ConsumeContext<IPolicyPublished> context)
    {
        var domain = context.Message.Domain;
        snapshotStore.Invalidate(domain);

        var snapshot = await policyClient.GetSnapshotAsync(domain, context.Message.TenantId, context.CancellationToken);
        if (snapshot is not null)
            snapshotStore.Set(domain, snapshot);
        else
            logger.LogWarning("PolicyPublished for {Domain}: snapshot pull returned null", domain);
    }
}
