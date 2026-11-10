using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>One published rule the validator refuses.</summary>
public sealed record PolicyInvalidPublishedRule(string Domain, string Scope, Guid TenantId, int VersionNumber, Guid VersionId, string? RuleKey, string Codes);

/// <summary>
/// PLAN-47 (P-5): the published rules the validator refuses: the stored rules of before PLAN-47 (validation applies to new and
/// changed rules only) and the rules that fire a workflow PLAN-46 retired (PLAN-49 retires them). Never fixed in place: a fix is
/// a new change set. Logged at start-up, one warning per rule with a fixed prefix that scripts/pol047-validate.ps1 reads; the
/// boot never fails on them. PLAN-48: an override's rules are checked with the agency ceiling too (CEILING_* codes): an old
/// overlay the evaluator bounds is listed here.
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

        var found = new List<PolicyInvalidPublishedRule>();
        var contexts = new Dictionary<(string Domain, PolicyTemplateScope Scope), PolicyRuleValidationContext>();
        foreach (var template in templates.OrderBy(t => t.Domain).ThenBy(t => t.Scope))
        {
            if (!contexts.TryGetValue((template.Domain, template.Scope), out var context))
                contexts[(template.Domain, template.Scope)] = context = await validation.LoadContextAsync(template.Domain, template.Scope, ct);

            foreach (var version in template.Versions.OrderBy(v => v.VersionNumber))
            {
                foreach (var rule in version.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal))
                {
                    var issues = PolicyRuleValidator.ValidateRule(PolicyRuleValidationService.ToInput(rule), context);
                    if (issues.Count > 0)
                        found.Add(new(template.Domain, template.Scope.ToString(), template.TenantId, version.VersionNumber, version.Id, rule.RuleKey,
                            string.Join(",", issues.Select(i => i.Code).Distinct())));
                }
            }
        }

        return found;
    }

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
            foreach (var rule in found)
                logger.LogWarning(
                    "{Prefix}: {Domain} {Scope} tenant {TenantId} v{VersionNumber} ({VersionId}) rule {RuleKey}: {Codes}",
                    LogPrefix, rule.Domain, rule.Scope, rule.TenantId, rule.VersionNumber, rule.VersionId, rule.RuleKey, rule.Codes);
            logger.LogInformation("{Prefix}: {Count} published rule(s) the validator refuses", LogPrefix, found.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A report: it never fails the boot.
            logger.LogWarning(ex, "{Prefix}: the inventory could not be taken", LogPrefix);
        }
    }
}
