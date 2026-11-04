using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;

namespace HRManagement.UnitTests.Messaging;

/// <summary>PLAN-40 (D-40-3): UUIDv5 message ids for repaired and replayed events.</summary>
public sealed class DeterministicMessageIdTests
{
    private static readonly Guid DnsNamespace = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    [Fact]
    public void Matches_the_published_uuid5_vector()
    {
        // Python: uuid.uuid5(uuid.NAMESPACE_DNS, "python.org")
        Assert.Equal(Guid.Parse("886313e1-3b8a-5372-9b90-0c9aee199e5d"), DeterministicMessageId.Create(DnsNamespace, "python.org"));
    }

    [Fact]
    public void Sets_version_5_and_the_rfc_variant()
    {
        var text = DeterministicMessageId.For<IPromotionExecuted>(Guid.NewGuid()).ToString("D");
        Assert.Equal('5', text[14]);
        Assert.Contains(text[19], "89ab");
    }

    [Fact]
    public void Same_event_and_aggregate_give_the_same_id()
    {
        var id = Guid.NewGuid();
        Assert.Equal(DeterministicMessageId.For<IPromotionExecuted>(id), DeterministicMessageId.For<IPromotionExecuted>(id));
        Assert.Equal(DeterministicMessageId.For<IPromotionExecuted>(id), DeterministicMessageId.For(nameof(IPromotionExecuted), id));
    }

    [Fact]
    public void Event_type_aggregate_and_version_each_change_the_id()
    {
        var id = Guid.NewGuid();
        var baseId = DeterministicMessageId.For<IPromotionExecuted>(id);
        Assert.NotEqual(baseId, DeterministicMessageId.For<IDelegationExecuted>(id));
        Assert.NotEqual(baseId, DeterministicMessageId.For<IPromotionExecuted>(Guid.NewGuid()));
        Assert.NotEqual(baseId, DeterministicMessageId.For<IPromotionExecuted>(id, version: 2));
    }

    [Fact]
    public void The_namespace_never_changes()
    {
        // Replays compare ids with the ones published before; a new namespace would turn every replay into a new message.
        Assert.Equal(Guid.Parse("6f1c9a52-40e8-4b1d-9a3e-2d7c0b5e4a10"), DeterministicMessageId.Namespace);
    }
}
