using HRManagement.SharedKernel.Entities;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-40 (§6): how a publisher knows which events of a replayable type already left. The consumers' receipts live in other
/// databases, so each replayable publish writes an append-only <see cref="EntityAuditLog"/> row in the same save as its
/// <c>OutboxMessage</c>. A completed source without that row is an event lost before the fix: the dry run lists it, the
/// apply publishes it and writes the row, and the next run lists nothing.
/// </summary>
public static class PublishedEventLedger
{
    public const string Published = "Published";
    public const string Replayed = "Replayed";

    /// <summary>The audit entity type of the summary row every non-dry run writes (actor and count).</summary>
    public const string ReplayRunEntityType = "MessagingReplayRun";

    public static string EntityType<TEvent>() => $"MessagingPublished:{typeof(TEvent).Name}";

    /// <summary>The row for <paramref name="aggregateId"/>: the event is committed with the save that writes it.</summary>
    public static EntityAuditLog Entry<TEvent>(Guid aggregateId, Guid? actor, string action = Published) => new()
    {
        EntityType = EntityType<TEvent>(),
        EntityId = aggregateId,
        Action = action,
        UserId = actor
    };

    /// <summary>The audit of a non-dry run: who ran it, what kind, how many events.</summary>
    public static EntityAuditLog ReplayRun(string kind, int count, Guid? actor) => new()
    {
        EntityType = ReplayRunEntityType,
        EntityId = Guid.NewGuid(),
        Action = Replayed,
        ChangedFields = $$"""{"kind":"{{kind}}","count":{{count}}}""",
        UserId = actor
    };
}

/// <summary>What a replay found or did: ids and counts only, never personal data.</summary>
public sealed record MessagingRepairResult(string Kind, bool DryRun, int Count, IReadOnlyList<Guid> Ids);
