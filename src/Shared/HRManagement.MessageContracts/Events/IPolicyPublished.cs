namespace HRManagement.MessageContracts.Events;

public interface IPolicyPublished
{
    string Domain { get; }
    Guid VersionId { get; }
    DateTime PublishedAt { get; }
    Guid? TenantId => null;
    int SchemaVersion => 1;
}

public sealed class PolicyPublished : IPolicyPublished
{
    public required string Domain { get; init; }
    public required Guid VersionId { get; init; }
    public required DateTime PublishedAt { get; init; }
}
