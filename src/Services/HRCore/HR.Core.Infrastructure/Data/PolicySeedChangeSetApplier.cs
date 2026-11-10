using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Core.Infrastructure.Data;

/// <summary>PLAN-47: an applied change set whose recorded digest differs from the code's, or a set the validator refuses.</summary>
public sealed class PolicySeedChangeSetException(string message) : InvalidOperationException(message);

/// <summary>
/// PLAN-47 (§4): applies <see cref="PolicySeedChangeSets"/> to the national template of each domain they touch, at start-up in the
/// national tenant (as the national seeder does). For a set not yet applied to a template: a draft copied from the effective
/// version, the operations (each rule validated), then a SystemSeed publication through <see cref="PolicyPublishService"/>, or
/// a draft left for an approver with PolicyEngine:Seeds:RequireApproval=true. A set with no effect is recorded as NoOp with no
/// version. Every application is recorded in PolicySeedApplications with its digest, so it never repeats; a changed digest fails
/// the boot. Rules already in the effective version are carried over as they are (PLAN-46's retired triggers stay for PLAN-49).
/// </summary>
public sealed class PolicySeedChangeSetApplier(
    HRCoreDbContext db,
    PolicyDraftService drafts,
    PolicyPublishService publisher,
    IConfiguration? configuration = null,
    ILogger<PolicySeedChangeSetApplier>? logger = null)
{
    public const string RequireApprovalKey = "PolicyEngine:Seeds:RequireApproval";

    public const string OutcomePublished = "Published";
    public const string OutcomeNoOp = "NoOp";
    public const string OutcomeDraft = "Draft";

    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    /// <summary>The applier from the request's services when they hold the same context, otherwise one built over it.</summary>
    public static PolicySeedChangeSetApplier Create(HRCoreDbContext db, IServiceProvider? services)
    {
        var configuration = services?.GetService<IConfiguration>() ?? new ConfigurationBuilder().Build();
        var logger = services?.GetService<ILogger<PolicySeedChangeSetApplier>>();
        if (services?.GetService<HRCoreDbContext>() is { } scoped && ReferenceEquals(scoped, db)
            && services.GetService<PolicyDraftService>() is { } scopedDrafts
            && services.GetService<PolicyPublishService>() is { } scopedPublisher)
            return new PolicySeedChangeSetApplier(db, scopedDrafts, scopedPublisher, configuration, logger);

        // No service container over this context (tests, tools): the same services, without the bus (no IPolicyPublished).
        var validation = PolicyRuleValidationService.ForCatalog(db);
        var cache = services?.GetService<IPolicyPublishedSnapshotCache>()
                    ?? new PolicyPublishedSnapshotCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var publishService = new PolicyPublishService(db, validation, new PolicyVersionPublisher(db, cache), configuration: configuration);
        return new PolicySeedChangeSetApplier(db, new PolicyDraftService(db, validation, configuration), publishService, configuration, logger);
    }

    public static Task ApplyAsync(HRCoreDbContext db, IServiceProvider? services = null, CancellationToken ct = default) =>
        Create(db, services).ApplyAsync(PolicySeedChangeSets.All, ct);

    public async Task ApplyAsync(IReadOnlyList<PolicySeedChangeSet> changeSets, CancellationToken ct = default)
    {
        foreach (var set in changeSets)
        {
            foreach (var domain in set.Domains)
                await ApplyToDomainAsync(set, domain, ct);
        }
    }

    /// <summary>The key of the advisory lock every HR.Core instance takes before it applies a change set (one at a time).</summary>
    public const long AdvisoryLockKey = 4_700_470_047;

    private async Task ApplyToDomainAsync(PolicySeedChangeSet set, string domain, CancellationToken ct)
    {
        // One transaction per template (a relational database; the in-memory test store has none). On PostgreSQL every instance
        // that boots takes the same transaction-scoped advisory lock first and reads the records under it, so two instances
        // starting together apply a set once: the second finds the first one's record.
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);

        var template = await db.PolicyTemplates
            .Include(t => t.Versions).ThenInclude(v => v.Rules)
            .FirstOrDefaultAsync(t => t.Domain == domain && t.Scope == PolicyTemplateScope.National, ct);
        var outOfScope = !PolicyDomainCatalog.IsGoverned(domain);
        if (template is null)
        {
            // PLAN-49: a new database seeds no template for a domain the catalog puts out of scope, and the catalog says why:
            // the set passes over it quietly at every start. A governed domain with no template is still worth a warning.
            if (outOfScope)
                _logger.LogDebug("PLAN-49 change set {ChangeSetId}: {Domain} is out of the engine's scope and has no template; nothing to apply", set.Id, domain);
            else
                _logger.LogWarning("PLAN-47 change set {ChangeSetId}: no national template for {Domain}; nothing applied", set.Id, domain);
            return;
        }

        var digest = set.Digest(domain);
        var applied = await db.PolicySeedApplications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ChangeSetId == set.Id && a.PolicyTemplateId == template.Id, ct);
        if (applied is not null)
        {
            if (!string.Equals(applied.Digest, digest, StringComparison.Ordinal))
                throw new PolicySeedChangeSetException(
                    $"PLAN-47: change set {set.Id} was applied to {domain} with digest {applied.Digest}, and the code now has {digest}. " +
                    "An applied change set never changes: write a new one with a new id.");
            return;
        }

        // PLAN-49: a set written before the domain left the engine's scope (the baseline's Archive.Retention part, on a database
        // that never applied it) adds no rule and no field to it; recorded once as NoOp, like any set with no effect.
        if (outOfScope)
        {
            await RecordAsync(set.Id, template, null, OutcomeNoOp, digest, ct);
            await CommitAsync(transaction, ct);
            _logger.LogInformation("PLAN-49 change set {ChangeSetId} on {Domain}: the domain is out of the engine's scope; NoOp", set.Id, domain);
            return;
        }

        if (template.Versions.Any(v => v.Status == PolicyVersionStatus.Draft))
        {
            _logger.LogWarning(
                "PLAN-47 change set {ChangeSetId}: {Domain} has an open draft (one draft per template); it applies at the next start-up after the draft is published or discarded",
                set.Id, domain);
            return;
        }

        var operations = set.For(domain);
        await EnsureFactsFieldsAsync(domain, operations, ct);

        var effective = PolicyDraftService.EffectiveVersion(template, DateTime.UtcNow)
                        ?? template.Versions.Where(v => v.Status == PolicyVersionStatus.Published).MaxBy(v => v.VersionNumber);
        var current = effective?.Rules.ToDictionary(r => r.RuleKey ?? r.Id.ToString(), StringComparer.Ordinal)
                      ?? new Dictionary<string, PolicyRule>(StringComparer.Ordinal);
        var ruleOps = operations.Where(o => o.Kind != PolicySeedOperationKind.EnsureFactsField && Changes(o, current)).ToList();

        if (ruleOps.Count == 0)
        {
            await RecordAsync(set.Id, template, null, OutcomeNoOp, digest, ct);
            await CommitAsync(transaction, ct);
            _logger.LogInformation("PLAN-47 change set {ChangeSetId} on {Domain}: NoOp", set.Id, domain);
            return;
        }

        var draft = await drafts.CreateDraftAsync(template.Id, effective?.Id, actorUserId: null, ct);
        if (!draft.Succeeded)
            throw new PolicySeedChangeSetException($"PLAN-47: change set {set.Id} could not open a draft of {domain}: {draft.MessageAr}");
        var draftId = draft.Id!.Value;

        foreach (var op in ruleOps)
        {
            var rules = await db.PolicyRules.AsNoTracking().Where(r => r.PolicyTemplateVersionId == draftId).ToListAsync(ct);
            var existing = rules.FirstOrDefault(r => r.RuleKey == op.RuleKey);
            var input = new PolicyRuleInput(op.RuleKey, op.Name, op.Priority, op.WhenJson, op.ThenJson);
            var result = op.Kind switch
            {
                PolicySeedOperationKind.AddRule => await drafts.AddRuleAsync(draftId, input, null, ct),
                PolicySeedOperationKind.ReplaceRule => await drafts.UpdateRuleAsync(draftId, existing!.Id, input, null, ct),
                PolicySeedOperationKind.RetireRule => await drafts.RemoveRuleAsync(draftId, existing!.Id, null, ct),
                _ => PolicyDraftResult.Ok()
            };
            if (!result.Succeeded)
                throw new PolicySeedChangeSetException(
                    $"PLAN-47: change set {set.Id} fails the validator on {domain} ({op.Kind} {op.RuleKey}): {result.MessageAr}");
        }

        // A set that retires the last rule leaves an empty version: that is the explicit withdrawal, with the set's reason.
        var draftVersion = await db.PolicyTemplateVersions.Include(v => v.Rules).FirstAsync(v => v.Id == draftId, ct);
        if (draftVersion.Rules.Count == 0)
        {
            draftVersion.WithdrawsAllRules = true;
            draftVersion.ChangeReason = string.Join("؛ ", ruleOps.Select(o => o.ReasonAr).Where(r => !string.IsNullOrWhiteSpace(r)).DefaultIfEmpty(set.Id));
            await db.SaveChangesAsync(ct);
        }

        if (configuration?.GetValue<bool>(RequireApprovalKey) == true)
        {
            await RecordAsync(set.Id, template, draftId, OutcomeDraft, digest, ct);
            await CommitAsync(transaction, ct);
            _logger.LogInformation("PLAN-47 change set {ChangeSetId} on {Domain}: draft {VersionId} waits for an approver", set.Id, domain, draftId);
            return;
        }

        var keys = ruleOps.Where(o => o.Kind != PolicySeedOperationKind.RetireRule).Select(o => o.RuleKey!).ToHashSet(StringComparer.Ordinal);
        var published = await publisher.PublishSystemSeedAsync(draftId, set.Id, keys, ct);
        if (!published.Succeeded)
            throw new PolicySeedChangeSetException(
                $"PLAN-47: change set {set.Id} could not publish {domain}: {published.MessageAr} {string.Join(" | ", published.IssueList.Select(i => $"{i.JsonPath}: {i.MessageAr}"))}");

        await RecordAsync(set.Id, template, draftId, OutcomePublished, digest, ct);
        await CommitAsync(transaction, ct);
        _logger.LogInformation("PLAN-47 change set {ChangeSetId} on {Domain}: published version {VersionId}", set.Id, domain, draftId);
    }

    /// <summary>Whether an operation changes the effective version.</summary>
    private static bool Changes(PolicySeedOperation op, IReadOnlyDictionary<string, PolicyRule> current) => op.Kind switch
    {
        PolicySeedOperationKind.AddRule => !current.ContainsKey(op.RuleKey!),
        PolicySeedOperationKind.ReplaceRule => current.TryGetValue(op.RuleKey!, out var r)
                                               && (r.Name != op.Name || r.Priority != op.Priority || r.WhenJson != op.WhenJson || r.ThenJson != op.ThenJson),
        PolicySeedOperationKind.RetireRule => current.ContainsKey(op.RuleKey!),
        _ => false
    };

    private async Task EnsureFactsFieldsAsync(string domain, IReadOnlyList<PolicySeedOperation> operations, CancellationToken ct)
    {
        var fieldOps = operations.Where(o => o.Kind == PolicySeedOperationKind.EnsureFactsField).ToList();
        if (fieldOps.Count == 0)
            return;

        var schema = await db.PolicyFactsSchemas.FirstOrDefaultAsync(s => s.Domain == domain, ct);
        if (schema is null)
            return;

        var fields = JsonSerializer.Deserialize<List<PolicyFieldSchema>>(schema.FieldsJson) ?? [];
        var changed = false;
        foreach (var op in fieldOps.Where(op => fields.All(f => f.Name != op.FieldName)))
        {
            fields.Add(new PolicyFieldSchema { Name = op.FieldName!, Type = op.FieldType!, DisplayNameAr = op.FieldDisplayAr });
            changed = true;
        }

        if (changed)
        {
            schema.FieldsJson = JsonSerializer.Serialize(fields);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task RecordAsync(string setId, PolicyTemplate template, Guid? versionId, string outcome, string digest, CancellationToken ct)
    {
        db.PolicySeedApplications.Add(new PolicySeedApplication
        {
            ChangeSetId = setId,
            PolicyTemplateId = template.Id,
            TenantId = template.TenantId,
            ResultVersionId = versionId,
            Outcome = outcome,
            Digest = digest,
            AppliedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task CommitAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction, CancellationToken ct)
    {
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }
}
