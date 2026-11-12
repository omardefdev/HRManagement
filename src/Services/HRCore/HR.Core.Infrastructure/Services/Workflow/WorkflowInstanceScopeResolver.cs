using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-52 (P-5): the org unit of the entity a workflow instance decides, per entity type, in one place. The work inbox reads it for each
/// item, <see cref="WorkflowStarter"/> stamps it into <see cref="WorkflowInstance.ScopeOrgUnitId"/> when an internal type starts, the
/// acceptance seeds stamp it after their own inserts, and <c>HCM052_OrgScopeKeys</c> fills old rows with the same logic in SQL (its parity
/// test compares the two). The four recruitment types reach a unit through their funded position; any other type (an establishment change,
/// a competition, its results) has no key: null, central (D-52-1). External types carry the unit their request gave (PLAN-43) and are not
/// resolved here.
/// </summary>
public sealed class WorkflowInstanceScopeResolver(HRCoreDbContext db)
{
    public const string RecruitmentRequisition = "RecruitmentRequisition";
    public const string OfferLetter = "OfferLetter";
    public const string SelectionCommitteeSession = "SelectionCommitteeSession";
    public const string RecruitmentAppeal = "RecruitmentAppeal";

    /// <summary>The internal types that have a scope key.</summary>
    public static readonly IReadOnlyList<string> KeyedTypes = [RecruitmentRequisition, OfferLetter, SelectionCommitteeSession, RecruitmentAppeal];

    /// <summary>The unit of each entity of <paramref name="entityType"/> (null: no unit, a missing entity or a type with no key).</summary>
    public async Task<IReadOnlyDictionary<Guid, Guid?>> ResolveAsync(string entityType, IReadOnlyCollection<Guid> entityIds, CancellationToken ct = default)
    {
        var ids = entityIds.Distinct().ToList();
        var result = ids.ToDictionary(id => id, _ => (Guid?)null);
        if (ids.Count == 0)
            return result;
        var rows = entityType switch
        {
            RecruitmentRequisition => db.RecruitmentRequisitions.AsNoTracking()
                .Where(r => ids.Contains(r.Id))
                .Select(r => new UnitRow(r.Id, (Guid?)r.FundedPosition!.OrgUnitId)),
            OfferLetter => db.OfferLetters.AsNoTracking()
                .Where(o => ids.Contains(o.Id))
                .Select(o => new UnitRow(o.Id, (Guid?)o.FundedPosition!.OrgUnitId)),
            SelectionCommitteeSession => db.SelectionCommitteeSessions.AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .Select(s => new UnitRow(s.Id, (Guid?)s.Requisition!.FundedPosition!.OrgUnitId)),
            RecruitmentAppeal => db.RecruitmentAppeals.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .Select(a => new UnitRow(a.Id, (Guid?)a.RecruitmentApplication!.Requisition!.FundedPosition!.OrgUnitId)),
            _ => null
        };
        if (rows is null)
            return result;
        foreach (var row in await rows.ToListAsync(ct))
            result[row.Id] = row.OrgUnitId;
        return result;
    }

    public async Task<Guid?> ResolveAsync(string entityType, Guid entityId, CancellationToken ct = default) =>
        (await ResolveAsync(entityType, [entityId], ct)).GetValueOrDefault(entityId);

    /// <summary>Stamps every instance of a keyed type whose scope is still null (the acceptance seeds insert instances themselves).</summary>
    public async Task<int> StampMissingAsync(CancellationToken ct = default)
    {
        var stamped = 0;
        foreach (var type in KeyedTypes)
        {
            var instances = await db.WorkflowInstances
                .Where(i => i.EntityType == type && i.ScopeOrgUnitId == null)
                .ToListAsync(ct);
            if (instances.Count == 0)
                continue;
            var units = await ResolveAsync(type, instances.Select(i => i.EntityId).ToList(), ct);
            foreach (var instance in instances)
            {
                if (units.GetValueOrDefault(instance.EntityId) is not Guid unit)
                    continue;
                instance.ScopeOrgUnitId = unit;
                stamped++;
            }
        }

        if (stamped > 0)
            await db.SaveChangesAsync(ct);
        return stamped;
    }

    private sealed record UnitRow(Guid Id, Guid? OrgUnitId);
}
