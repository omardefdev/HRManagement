namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-47: a consumer every instance of the service must run for every message (it clears in-memory state, such as a cache).
/// <see cref="MassTransitExtensions.AddServiceMassTransit"/> gives it a temporary queue per instance, named with
/// <see cref="MassTransitExtensions.PerInstanceEndpointName"/>, outside the consumer outbox and the inbox receipts. Its effect
/// must be idempotent and in memory only.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PerInstanceConsumerAttribute(string purpose) : Attribute
{
    public string Purpose { get; } = purpose;
}
