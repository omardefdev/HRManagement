using HRManagement.ServiceDefaults.Workflow;

namespace HR.Core.Web.ViewModels;

public sealed class WorkInboxIndexViewModel
{
    public IReadOnlyList<WorkInboxItem> Items { get; init; } = [];

    public int PendingCount { get; init; }

    public int WorkflowCount { get; init; }

    public int LegacyCount { get; init; }

    public string? EntityTypeFilter { get; init; }

    public string? SourceFilter { get; init; }

    public IReadOnlyList<WorkInboxItem> FilteredItems =>
        Items.Where(MatchesFilters).OrderBy(i => i.CreatedAt).ToList();

    private bool MatchesFilters(WorkInboxItem item)
    {
        if (!string.IsNullOrWhiteSpace(SourceFilter)
            && !string.Equals(item.SourceSystem, SourceFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(EntityTypeFilter))
            return true;

        return item.Description?.Contains(EntityTypeFilter, StringComparison.OrdinalIgnoreCase) == true
               || item.Title?.Contains(EntityTypeFilter, StringComparison.OrdinalIgnoreCase) == true;
    }
}
