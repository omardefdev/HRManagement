using System.Security.Cryptography;
using System.Text;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-40 (D-40-3): name-based UUID version 5 (RFC 9562 §5.5) message ids for repaired and replayed events:
/// <c>UUIDv5(namespace, "&lt;EventType&gt;:&lt;AggregateId&gt;:v1")</c>. The same event for the same aggregate always gets
/// the same <c>MessageId</c>, so a replay is deduplicated by the consumers' inbox receipts.
/// Use with <c>Publish&lt;T&gt;(values, ctx =&gt; ctx.MessageId = DeterministicMessageId.For&lt;T&gt;(id), ct)</c>.
/// </summary>
public static class DeterministicMessageId
{
    /// <summary>The fixed namespace of HRManagement domain events (never change it: replays rely on it).</summary>
    public static readonly Guid Namespace = Guid.Parse("6f1c9a52-40e8-4b1d-9a3e-2d7c0b5e4a10");

    /// <summary>The id of event <typeparamref name="TEvent"/> about <paramref name="aggregateId"/>.</summary>
    public static Guid For<TEvent>(Guid aggregateId, int version = 1) =>
        For(typeof(TEvent).Name, aggregateId, version);

    public static Guid For(string eventType, Guid aggregateId, int version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        return Create(Namespace, $"{eventType}:{aggregateId:D}:v{version}");
    }

    /// <summary>RFC 9562 UUIDv5 of <paramref name="name"/> (UTF-8) in <paramref name="namespaceId"/>.</summary>
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Span<byte> ns = stackalloc byte[16];
        namespaceId.TryWriteBytes(ns, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + nameBytes.Length];
        ns.CopyTo(input);
        nameBytes.CopyTo(input, 16);

        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 9562 v5 is defined on SHA-1; the id is a name, not a security boundary.
        SHA1.HashData(input, hash);
#pragma warning restore CA5350
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
