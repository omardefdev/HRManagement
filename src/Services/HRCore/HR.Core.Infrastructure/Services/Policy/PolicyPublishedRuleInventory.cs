using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>
/// One published rule the validator refuses. <paramref name="InForce"/>: its version is effective now or will be (its window has
/// not closed); otherwise the version is history, superseded by a later one.
/// </summary>
public sealed record PolicyInvalidPublishedRule(
    string Domain, string Scope, Guid TenantId, int VersionNumber, Guid VersionId, string? RuleKey, string Codes, bool InForce);

/// <summary>
/// PLAN-47 (P-5): the published rules the validator refuses. Never fixed in place: a fix is a new change set. Logged at start-up
/// with a fixed prefix that scripts/pol047-validate.ps1 reads; the boot never fails on them. PLAN-48: an override's rules are
/// checked with the agency ceiling too (CEILING_* codes).
/// <para>
/// PLAN-49 (plan §2, rule 1): the inventory tells a rule in force from history. A rule of a version that is effective now, or
/// will be, is a defect and gets one warning each: after PLAN-49's change sets there is none (no effect outside its domain's
/// capabilities, no orphan trigger, nothing in a domain out of the engine's scope). The rules of superseded versions stay as
/// they were written (history is never rewritten, and the ledger still names those versions): they are only counted.
/// </para>
/// </summary>
public static class PolicyPublishedRuleInventory
{
    public const string LogPrefix = "PLAN-47 validator inventory";

    public static async Task<IReadOnlyList<PolicyInvalidPublishedRule>> FindAsync(HRCoreDbContext db, IPolicyRuleValidationService validation, CancellationToken ct = default)
    {
        var templates = await db.PolicyTemplates.AsNoTracking()
            .Include(t => t.Versions.Where(v => v.Status == PolicyVersionStatus.Published))
            .ThenInclude(v => v.Rules)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var found = new List<PolicyInvalidPublishedRule>();
        var contexts = new Dictionary<(string Domain, PolicyTemplateScope Scope), PolicyRuleValidationContext>();
        foreach (var template in templates.OrderBy(t => t.Domain).ThenBy(t => t.Scope))
        {
            if (!contexts.TryGetValue((template.Domain, template.Scope), out var context))
                contexts[(template.Domain, template.Scope)] = context = await validation.LoadContextAsync(template.Domain, template.Scope, ct);

            // In force: the version the engine evaluates now (the same choice the engine and the draft service make), or one
            // published to take effect later.
            var effective = PolicyDraftService.EffectiveVersion(template, now);
            foreach (var version in template.Versions.OrderBy(v => v.VersionNumber))
            {
                var inForce = version.Id == effective?.Id || version.EffectiveFrom > now;
                foreach (var rule in version.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal))
                {
                    var issues = PolicyRuleValidator.ValidateRule(PolicyRuleValidationService.ToInput(rule), context);
                    if (issues.Count > 0)
                        found.Add(new(template.Domain, template.Scope.ToString(), template.TenantId, version.VersionNumber, version.Id, rule.RuleKey,
                            string.Join(",", issues.Select(i => i.Code).Distinct()), inForce));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Called by the national seeding after the code policies are seeded: a SelectCodePolicy rule is checked against their keys,
    /// and on a new database they are written after the policy seed.
    /// </summary>
    public static async Task LogAsync(HRCoreDbContext db, IServiceProvider? services, CancellationToken ct = default)
    {
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(PolicyPublishedRuleInventory).FullName!);
        if (logger is null)
            return;

        try
        {
            var validation = services?.GetService<IPolicyRuleValidationService>()
                             ?? PolicyRuleValidationService.ForCatalog(db);
            var found = await FindAsync(db, validation, ct);
            var inForce = found.Where(r => r.InForce).ToList();
            foreach (var rule in inForce)
                logger.LogWarning(
                    "{Prefix}: {Domain} {Scope} tenant {TenantId} v{VersionNumber} ({VersionId}) rule {RuleKey}: {Codes}",
                    LogPrefix, rule.Domain, rule.Scope, rule.TenantId, rule.VersionNumber, rule.VersionId, rule.RuleKey, rule.Codes);
            logger.LogInformation(
                "{Prefix}: {Count} rule(s) of effective versions the validator refuses; {Historic} more in superseded versions (history, unchanged)",
                LogPrefix, inForce.Count, found.Count - inForce.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A report: it never fails the boot.
            logger.LogWarning(ex, "{Prefix}: the inventory could not be taken", LogPrefix);
        }
    }
}
