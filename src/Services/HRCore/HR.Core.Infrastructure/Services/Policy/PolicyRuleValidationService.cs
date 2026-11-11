using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>
/// PLAN-47: the validator with what it needs loaded from the database. PLAN-48: every call names the scope of the template the
/// rules belong to; for a TenantOverride the agency ceiling (D-TEN-8 = D-POL-4) is checked against the domain's published national
/// version.
/// </summary>
public interface IPolicyRuleValidationService
{
    /// <summary>
    /// PLAN-49 (D-POL-1): the refusal of a draft or a publication in a domain the catalog does not govern (empty when governed).
    /// Every draft entry point and every publication ask it; the retirement change set's final version is the one exception, and
    /// <see cref="PolicyPublishService"/> decides it.
    /// </summary>
    IReadOnlyList<PolicyRuleIssue> ValidateDomain(string domain);

    Task<PolicyRuleValidationContext> LoadContextAsync(string domain, PolicyTemplateScope scope, CancellationToken ct = default);

    Task<IReadOnlyList<PolicyRuleIssue>> ValidateRuleAsync(string domain, PolicyTemplateScope scope, PolicyRuleInput rule, CancellationToken ct = default);

    Task<IReadOnlyList<PolicyRuleIssue>> ValidateVersionAsync(string domain, PolicyTemplateScope scope, IReadOnlyList<PolicyRuleInput> rules, CancellationToken ct = default);
}

public sealed class PolicyRuleValidationService(HRCoreDbContext db, IPolicyDomainCapabilities capabilities) : IPolicyRuleValidationService
{
    /// <summary>PLAN-49: the validation every path uses when no container is at hand (seeders, tools): the catalog's capabilities.</summary>
    public static PolicyRuleValidationService ForCatalog(HRCoreDbContext db) => new(db, CatalogPolicyDomainCapabilities.Instance);

    public IReadOnlyList<PolicyRuleIssue> ValidateDomain(string domain) => PolicyRuleValidator.ValidateDomain(domain, capabilities);

    public async Task<PolicyRuleValidationContext> LoadContextAsync(string domain, PolicyTemplateScope scope, CancellationToken ct = default)
    {
        // PLAN-48: the facts schemas, workflow definitions, bindings and code templates are read through the one national read
        // path. Under forced row security the national_shared_read policy shows the same national rows, so an agency session's
        // validator now accepts the fields of the national schema (PLAN-47 failed closed until this plan). The schema is the one
        // the evaluator uses (PolicyOrchestrator): the agency's own first, then the national one.
        var tenant = db.CurrentTenantId;
        var schemaEntity = await db.PolicyFactsSchemas.OwnOrNational(db).AsNoTracking()
            .Where(s => s.Domain == domain)
            .OwnFirst(tenant)
            .FirstOrDefaultAsync(ct);

        // The session tenant and the national tenant only (an agency rule never names another agency's workflow or key).
        var definitions = await db.WorkflowDefinitions.OwnOrNational(db).AsNoTracking()
            .Select(d => new { d.Code, d.IsPublished, d.Status })
            .ToListAsync(ct);

        var retired = new HashSet<string>(WorkflowPackSeeder.RetiredCodes, StringComparer.OrdinalIgnoreCase);
        // A code whose versions are all retired is retired; an older retired version beside a published one is not.
        foreach (var code in definitions.GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Any(d => d.Status == WorkflowDefinitionStatus.Retired) && g.All(d => d.Status != WorkflowDefinitionStatus.Published))
                     .Select(g => g.Key))
            retired.Add(code);

        var published = new HashSet<string>(
            definitions.Where(d => d.IsPublished && d.Status == WorkflowDefinitionStatus.Published && !retired.Contains(d.Code)).Select(d => d.Code),
            StringComparer.OrdinalIgnoreCase);

        var bound = await db.PolicyWorkflowBindings.OwnOrNational(db).AsNoTracking()
            .Where(b => b.Domain == domain)
            .Select(b => b.WorkflowCode)
            .ToListAsync(ct);

        // D-TEN-1: on the national side only Scope = National (the trial agency's overrides stay its own).
        var codeKeys = await db.CodePolicyTemplates.OwnOrNational(db).AsNoTracking()
            .Where(t => t.PolicyKey != null)
            .Where(t => db.BypassTenantFilter || (tenant != null && t.TenantId == tenant) || t.Scope == CodePolicyScope.National)
            .Select(t => t.PolicyKey!)
            .ToListAsync(ct);

        var isOverride = scope == PolicyTemplateScope.TenantOverride;
        var (nationalFields, nationalTriggers) = isOverride
            ? await LoadNationalCeilingAsync(domain, ct)
            : (new HashSet<string>(StringComparer.Ordinal), false);

        return new PolicyRuleValidationContext
        {
            Domain = domain,
            Schema = PolicyEntityMapper.ParseSchema(schemaEntity),
            PublishedWorkflowCodes = published,
            RetiredWorkflowCodes = retired,
            BoundWorkflowCodes = new HashSet<string>(bound, StringComparer.OrdinalIgnoreCase),
            CodePolicyKeys = new HashSet<string>(codeKeys, StringComparer.Ordinal),
            Capabilities = capabilities,
            IsAgencyOverride = isOverride,
            NationalSetFields = nationalFields,
            NationalTriggersWorkflow = nationalTriggers
        };
    }

    /// <summary>
    /// PLAN-48 (§4): what the domain's national version in force sets with SetField and whether it triggers a workflow: the
    /// national template (national tenant, Scope = National), its version effective now, its rules as the evaluator reads them.
    /// </summary>
    private async Task<(HashSet<string> Fields, bool Triggers)> LoadNationalCeilingAsync(string domain, CancellationToken ct)
    {
        var template = await db.PolicyTemplates.NationalOnly().AsNoTracking()
            .Include(t => t.Versions.Where(v => v.Status == PolicyVersionStatus.Published))
            .ThenInclude(v => v.Rules)
            .Where(t => t.Domain == domain && t.Scope == PolicyTemplateScope.National)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        var fields = new HashSet<string>(StringComparer.Ordinal);
        var version = template is null ? null : PolicyDraftService.EffectiveVersion(template, DateTime.UtcNow);
        if (version is null)
            return (fields, false);

        var effects = PolicyEntityMapper.ToSnapshot(template!, version).Rules.SelectMany(r => r.Then).ToList();
        foreach (var effect in effects.Where(e => e.Effect == PolicyEffect.SetField))
        {
            if (effect.Params?.GetValueOrDefault("field") is { Length: > 0 } field)
                fields.Add(field);
        }

        return (fields, effects.Any(e => e.Effect == PolicyEffect.TriggerWorkflow));
    }

    public async Task<IReadOnlyList<PolicyRuleIssue>> ValidateRuleAsync(string domain, PolicyTemplateScope scope, PolicyRuleInput rule, CancellationToken ct = default) =>
        PolicyRuleValidator.ValidateRule(rule, await LoadContextAsync(domain, scope, ct));

    public async Task<IReadOnlyList<PolicyRuleIssue>> ValidateVersionAsync(string domain, PolicyTemplateScope scope, IReadOnlyList<PolicyRuleInput> rules, CancellationToken ct = default) =>
        PolicyRuleValidator.ValidateVersion(rules, await LoadContextAsync(domain, scope, ct));

    public static PolicyRuleInput ToInput(PolicyRule rule) => new(rule.RuleKey, rule.Name, rule.Priority, rule.WhenJson, rule.ThenJson);
}
