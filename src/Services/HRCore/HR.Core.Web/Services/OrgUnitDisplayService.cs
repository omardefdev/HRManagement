using HR.Core.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.Services;

/// <summary>Builds human-readable hierarchical labels without exposing technical paths.</summary>
public static class OrgUnitDisplayService
{
    public static IReadOnlyDictionary<Guid, string> BuildLabels(IEnumerable<OrgUnit> units)
    {
        var byId = units.ToDictionary(x => x.Id);
        var labels = new Dictionary<Guid, string>();
        foreach (var unit in byId.Values)
            labels[unit.Id] = BuildLabel(unit, byId, labels, new HashSet<Guid>());
        return labels;
    }

    public static IReadOnlyList<SelectListItem> BuildOptions(
        IEnumerable<OrgUnit> units,
        Guid? selectedId = null)
    {
        var labels = BuildLabels(units);
        return labels
            .OrderBy(x => x.Value, StringComparer.CurrentCulture)
            .Select(x => new SelectListItem
            {
                Value = x.Key.ToString(),
                Text = x.Value,
                Selected = selectedId == x.Key
            })
            .ToList();
    }

    private static string BuildLabel(
        OrgUnit unit,
        IReadOnlyDictionary<Guid, OrgUnit> byId,
        IDictionary<Guid, string> labels,
        ISet<Guid> visiting)
    {
        if (labels.TryGetValue(unit.Id, out var cached))
            return cached;

        if (!visiting.Add(unit.Id))
            return unit.NameAr;

        var label = unit.ParentId is Guid parentId && byId.TryGetValue(parentId, out var parent)
            ? $"{BuildLabel(parent, byId, labels, visiting)} / {unit.NameAr}"
            : unit.NameAr;

        visiting.Remove(unit.Id);
        labels[unit.Id] = label;
        return label;
    }
}
