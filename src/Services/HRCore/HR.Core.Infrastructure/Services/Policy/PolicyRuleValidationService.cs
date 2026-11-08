using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>PLAN-47: the validator with what it needs loaded from the database.</summary>
public interface IPolicyRuleValidationService
{
    Task<PolicyRuleValidationContext> LoadContextAsync(string domain, CancellationToken ct = default);

    Task<IReadOnlyList<PolicyRuleIssue>> ValidateRuleAsync(string domain, PolicyRuleInput rule, CancellationToken ct = default);

    Task<IReadOnlyList<PolicyRuleIssue>> ValidateVersionAsync(string domain, IReadOnlyList<PolicyRuleInput> rules, CancellationToken ct = default);
}

public sealed class PolicyRuleValidationService(HRCoreDbContext db, IPolicyDomainCapabilities capabilities) : IPolicyRuleValidationService
{
    public async Task<PolicyRuleValidationContext> LoadContextAsync(string domain, CancellationToken ct = default)
    {
        // The facts schemas, workflow definitions, bindings and code templates are national (PLATFORM tenant) rows: an agency
        // session reads them past the EF tenant filter. Under forced row security a tenant session still cannot see them; the
        // validator then refuses every field (fail closed) until PLAN-48 adds the shared national read.
        var schemaEntity = await db.PolicyFactsSchemas.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution).AsNoTracking()
            .FirstOrDefaultAsync(s => s.Domain == domain, ct);

        // The session tenant and the national tenant only (an agency rule never names another agency's workflow or key).
        var tenant = db.CurrentTenantId;
        var national = HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId;
        var definitions = await db.WorkflowDefinitions.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution).AsNoTracking()
            .Where(d => tenant == null || d.TenantId == tenant || d.TenantId == national)
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

        var bound = await db.PolicyWorkflowBindings.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution).AsNoTracking()
            .Where(b => b.Domain == domain)
            .Where(b => tenant == null || b.TenantId == tenant || b.TenantId == national)
            .Select(b => b.WorkflowCode)
            .ToListAsync(ct);

        var codeKeys = await db.CodePolicyTemplates.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution).AsNoTracking()
            .Where(t => t.PolicyKey != null)
            .Where(t => tenant == null || t.TenantId == tenant || t.TenantId == national)
            .Select(t => t.PolicyKey!)
            .ToListAsync(ct);

        return new PolicyRuleValidationContext
        {
            Domain = domain,
            Schema = PolicyEntityMapper.ParseSchema(schemaEntity),
            PublishedWorkflowCodes = published,
            RetiredWorkflowCodes = retired,
            BoundWorkflowCodes = new HashSet<string>(bound, StringComparer.OrdinalIgnoreCase),
            CodePolicyKeys = new HashSet<string>(codeKeys, StringComparer.Ordinal),
            Capabilities = capabilities
        };
    }

    public async Task<IReadOnlyList<PolicyRuleIssue>> ValidateRuleAsync(string domain, PolicyRuleInput rule, CancellationToken ct = default) =>
        PolicyRuleValidator.ValidateRule(rule, await LoadContextAsync(domain, ct));

    public async Task<IReadOnlyList<PolicyRuleIssue>> ValidateVersionAsync(string domain, IReadOnlyList<PolicyRuleInput> rules, CancellationToken ct = default) =>
        PolicyRuleValidator.ValidateVersion(rules, await LoadContextAsync(domain, ct));

    public static PolicyRuleInput ToInput(PolicyRule rule) => new(rule.RuleKey, rule.Name, rule.Priority, rule.WhenJson, rule.ThenJson);
}
