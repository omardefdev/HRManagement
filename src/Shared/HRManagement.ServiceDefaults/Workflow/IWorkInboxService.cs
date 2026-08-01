namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>WF-002: abstraction for the unified national work inbox.</summary>
public interface IWorkInboxService
{
    Task<IReadOnlyList<WorkInboxItem>> GetPersonalInboxAsync(Guid assigneeId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkInboxItem>> GetTeamInboxAsync(string teamId, CancellationToken ct = default);

    Task<WorkInboxItem> CreateItemAsync(WorkInboxItem item, CancellationToken ct = default);

    Task CompleteItemAsync(Guid itemId, string? resolution = null, CancellationToken ct = default);

    Task DelegateItemAsync(Guid itemId, Guid newAssigneeId, CancellationToken ct = default);

    Task EscalateItemAsync(Guid itemId, string reason, CancellationToken ct = default);

    Task<int> GetPendingCountAsync(Guid assigneeId, CancellationToken ct = default);
}
