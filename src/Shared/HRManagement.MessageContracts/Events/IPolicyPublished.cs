namespace HRManagement.MessageContracts.Events;

/// <summary>
/// A policy version was published. PLAN-47: staged by PolicyVersionPublisher in the publication's own save, for every kind
/// (manual, rollback, seed change set); every HR.Core instance clears its policy cache for the domain on it.
/// </summary>
public interface IPolicyPublished
{
    string Domain { get; }
    Guid VersionId { get; }
    DateTime PublishedAt { get; }
    Guid? TenantId { get; }
    int SchemaVersion => 1;

    /// <summary>PLAN-47: the published version's number (0 in a message staged before PLAN-47).</summary>
    int VersionNumber { get; }

    /// <summary>PLAN-47: when the version takes effect; it may be later than PublishedAt.</summary>
    DateTime? EffectiveFrom { get; }
}

public sealed class PolicyPublished : IPolicyPublished
{
    public required string Domain { get; init; }
    public required Guid VersionId { get; init; }
    public required DateTime PublishedAt { get; init; }
    public Guid? TenantId { get; init; }
    public int VersionNumber { get; init; }
    public DateTime? EffectiveFrom { get; init; }
}
