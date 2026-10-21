using System.Security.Claims;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.AspNetCore.Http;

namespace HR.Core.Web.Services;

public sealed class HrCoreWorkInboxService(
    WorkflowInboxQueryService query,
    IHttpContextAccessor httpContextAccessor) : IWorkInboxService
{
    public async Task<IReadOnlyList<WorkInboxItem>> GetPersonalInboxAsync(
        Guid assigneeId,
        CancellationToken ct = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
            return [];

        var userId = WorkflowPanelBuilder.ParseUserId(user);
        if (userId != assigneeId)
            return [];

        return await query.GetActionableItemsAsync(user, ct);
    }

    public Task<IReadOnlyList<WorkInboxItem>> GetTeamInboxAsync(string teamId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WorkInboxItem>>([]);

    public Task<WorkInboxItem> CreateItemAsync(WorkInboxItem item, CancellationToken ct = default) =>
        throw new NotSupportedException("Work inbox persistence is out of scope for PLAN-23 (virtual projection).");

    public Task CompleteItemAsync(Guid itemId, string? resolution = null, CancellationToken ct = default) =>
        throw new NotSupportedException("Complete inbox items via entity Details approve flow (PLAN-21/22) or legacy Approvals.");

    public Task DelegateItemAsync(Guid itemId, Guid newAssigneeId, CancellationToken ct = default) =>
        throw new NotSupportedException("Delegation is out of scope for PLAN-23.");

    public Task EscalateItemAsync(Guid itemId, string reason, CancellationToken ct = default) =>
        throw new NotSupportedException("Escalation is out of scope for PLAN-23.");

    public async Task<int> GetPendingCountAsync(Guid assigneeId, CancellationToken ct = default)
    {
        var items = await GetPersonalInboxAsync(assigneeId, ct);
        return items.Count;
    }

    internal async Task<(int Total, int WorkflowCount, int LegacyCount)> GetPendingBreakdownAsync(
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var workflow = await query.GetActionableWorkflowItemsAsync(user, ct);
        var legacy = await query.GetActionableLegacyItemsAsync(user, ct);
        return (workflow.Count + legacy.Count, workflow.Count, legacy.Count);
    }
}
