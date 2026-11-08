using HR.Core.Domain.Entities.Policy;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Core.Infrastructure.Data.Interceptors;

/// <summary>PLAN-47: a write that would change a published (or archived) policy version.</summary>
public sealed class PolicyImmutabilityException(string message) : InvalidOperationException(message);

/// <summary>
/// PLAN-47 (G-POL-07, §3): a published policy version never changes through the application. Refused: adding, changing or
/// deleting a rule of a version that is not a draft; changing a published or archived version in anything but EffectiveTo,
/// UpdatedAt and UpdatedBy; deleting a version that is not a draft. The PostgreSQL trigger of POL047_PolicyRuleLifecycle
/// covers what passes outside EF (ExecuteUpdate, raw SQL).
/// </summary>
public sealed class PublishedPolicyImmutabilityInterceptor : SaveChangesInterceptor
{
    public const string RuleMessage = "الإصدار المنشور لا يُعدَّل: لا تُضاف قواعده ولا تُعدَّل ولا تُحذف. أنشئ مسودة جديدة";
    public const string VersionMessage = "الإصدار المنشور لا يُعدَّل إلا في تاريخ نهاية النفاذ";
    public const string DeleteMessage = "لا يُحذف إلا إصدار مسودة";

    private static readonly HashSet<string> WritableOnPublished = new(StringComparer.Ordinal)
    {
        nameof(PolicyTemplateVersion.EffectiveTo),
        nameof(PolicyTemplateVersion.UpdatedAt),
        nameof(PolicyTemplateVersion.UpdatedBy)
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            Check(context, ids => Lookup(context, ids).ToDictionary(x => x.Id, x => x.Status));
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var ids = UntrackedVersionIds(context);
            var stored = ids.Count == 0
                ? new Dictionary<Guid, PolicyVersionStatus>()
                : await Lookup(context, ids).ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);
            Check(context, _ => stored);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class VersionStatusRow
    {
        public Guid Id { get; init; }
        public PolicyVersionStatus Status { get; init; }
    }

    private static IQueryable<VersionStatusRow> Lookup(DbContext context, IReadOnlyCollection<Guid> ids) =>
        context.Set<PolicyTemplateVersion>()
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new VersionStatusRow { Id = v.Id, Status = v.Status });

    /// <summary>Versions named by changed rules that the change tracker does not hold.</summary>
    private static List<Guid> UntrackedVersionIds(DbContext context) => UntrackedVersionIdsOf(context, ChangedRules(context));

    private static void Check(DbContext context, Func<IReadOnlyCollection<Guid>, IReadOnlyDictionary<Guid, PolicyVersionStatus>> load)
    {
        foreach (var entry in context.ChangeTracker.Entries<PolicyTemplateVersion>())
        {
            if (entry.State == EntityState.Deleted && entry.Property(v => v.Status).OriginalValue != PolicyVersionStatus.Draft)
                throw new PolicyImmutabilityException(DeleteMessage);

            if (entry.State != EntityState.Modified || entry.Property(v => v.Status).OriginalValue == PolicyVersionStatus.Draft)
                continue;

            foreach (var property in entry.Properties)
            {
                if (!property.IsModified || WritableOnPublished.Contains(property.Metadata.Name))
                    continue;
                if (!Equals(property.OriginalValue, property.CurrentValue))
                    throw new PolicyImmutabilityException($"{VersionMessage} (الحقل {property.Metadata.Name})");
            }
        }

        var rules = ChangedRules(context).ToList();
        if (rules.Count == 0)
            return;

        var untracked = UntrackedVersionIdsOf(context, rules);
        var stored = untracked.Count == 0 ? new Dictionary<Guid, PolicyVersionStatus>() : load(untracked);
        foreach (var rule in rules)
        {
            foreach (var versionId in RuleVersionIds(rule))
            {
                if (!IsDraftBeforeAndAfter(context, versionId, stored))
                    throw new PolicyImmutabilityException(RuleMessage);
            }
        }
    }

    private static List<Guid> UntrackedVersionIdsOf(DbContext context, IEnumerable<EntityEntry<PolicyRule>> rules)
    {
        var tracked = context.ChangeTracker.Entries<PolicyTemplateVersion>().Select(e => e.Entity.Id).ToHashSet();
        return rules.SelectMany(RuleVersionIds).Where(id => !tracked.Contains(id)).Distinct().ToList();
    }

    private static bool IsDraftBeforeAndAfter(DbContext context, Guid versionId, IReadOnlyDictionary<Guid, PolicyVersionStatus> stored)
    {
        var entry = context.ChangeTracker.Entries<PolicyTemplateVersion>().FirstOrDefault(e => e.Entity.Id == versionId);
        if (entry is not null)
        {
            var current = entry.Property(v => v.Status).CurrentValue;
            var original = entry.State == EntityState.Added ? current : entry.Property(v => v.Status).OriginalValue;
            return original == PolicyVersionStatus.Draft && current == PolicyVersionStatus.Draft;
        }

        // Not tracked: the stored status decides; a version that cannot be found is refused (fail closed).
        return stored.TryGetValue(versionId, out var status) && status == PolicyVersionStatus.Draft;
    }

    private static IEnumerable<EntityEntry<PolicyRule>> ChangedRules(DbContext context) =>
        context.ChangeTracker.Entries<PolicyRule>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

    private static IEnumerable<Guid> RuleVersionIds(EntityEntry<PolicyRule> rule)
    {
        var property = rule.Property(r => r.PolicyTemplateVersionId);
        var current = rule.Entity.PolicyTemplateVersion?.Id is Guid nav && nav != Guid.Empty ? nav : property.CurrentValue;
        yield return current;
        if (rule.State == EntityState.Modified && property.OriginalValue != current)
            yield return property.OriginalValue;
    }
}
