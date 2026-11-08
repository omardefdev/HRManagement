using HR.Core.Infrastructure.Services.Policy;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>
/// PLAN-47 (§8, G-POL-15): every HR.Core instance clears its policy snapshot cache for the domain of a published version. It runs
/// on a temporary endpoint per instance (production-ha runs two), outside the consumer outbox and the inbox receipts: clearing a
/// cache is idempotent and touches no database.
/// </summary>
[PerInstanceConsumer(Purpose)]
public sealed class PolicyPublishedCacheInvalidationConsumer(
    IPolicyPublishedSnapshotCache snapshotCache,
    ILogger<PolicyPublishedCacheInvalidationConsumer> logger) : IConsumer<IPolicyPublished>
{
    public const string Purpose = "policy-cache";

    public Task Consume(ConsumeContext<IPolicyPublished> context)
    {
        snapshotCache.InvalidateDomain(context.Message.Domain);
        logger.LogInformation(
            "PLAN-47 policy cache cleared for {Domain} (version {VersionId} v{VersionNumber})",
            context.Message.Domain, context.Message.VersionId, context.Message.VersionNumber);
        return Task.CompletedTask;
    }
}
