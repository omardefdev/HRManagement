using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
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
/// <para>
/// The agencies' overrides are read apart (the review of PLAN-49, finding 1). The start-up session is the national tenant's,
/// and a session sees its own tenant only (the EF filter, and row security in production), so the lines above say nothing
/// about an agency's override. Each tenant of the Tenants table is therefore read in its own session (the PLAN-44 and PLAN-48
/// pattern), and its findings are logged under their own prefix: an override rule the validator refuses stays in force until
/// the agency publishes a version without it (a copy is refused at publish), and the agency ceiling bounds it at evaluation.
/// </para>
/// </summary>
public static class PolicyPublishedRuleInventory
{
    public const string LogPrefix = "PLAN-47 validator inventory";

    /// <summary>The prefix of the agencies' part, read by scripts/pol047-validate.ps1 too.</summary>
    public const string OverrideLogPrefix = "PLAN-49 agency override inventory";

    public static async Task<IReadOnlyList<PolicyInvalidPublishedRule>> FindAsync(HRCoreDbContext db, IPolicyRuleValidationService validation, CancellationToken ct = default) =>
        (await ReadAsync(db, validation, ct)).Found;

    /// <summary>
    /// As <see cref="FindAsync"/>, with the number of templates the session held. "Nothing refused" and "nothing read" would
    /// otherwise print the same line: a session that is nobody's sees no template at all (the last review of PLAN-49).
    /// </summary>
    public static async Task<(IReadOnlyList<PolicyInvalidPublishedRule> Found, int TemplatesRead)> ReadAsync(
        HRCoreDbContext db, IPolicyRuleValidationService validation, CancellationToken ct = default)
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

        return (found, templates.Count);
    }

    /// <summary>
    /// The published rules of the agencies' overrides that the validator refuses, each tenant read in its own session, with the
    /// number of tenants read. Null when the tenants cannot be read apart: no service scope holds the context (a tool or a test
    /// built over one context), or the session is a request's, whose tenant is the caller's whatever the flow says (the token's
    /// tenant comes first): one session cannot stand for the others.
    /// </summary>
    public static async Task<(IReadOnlyList<PolicyInvalidPublishedRule> Found, int TenantsRead)?> FindAgencyOverridesAsync(
        HRCoreDbContext db, IServiceProvider? services, CancellationToken ct = default)
    {
        var scopeFactory = services?.GetService<IServiceScopeFactory>();
        if (scopeFactory is null || services!.GetService<IServiceProviderIsService>()?.IsService(typeof(HRCoreDbContext)) != true)
            return null;

        // The Tenants table is outside row security: the one place every tenant can be listed from.
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).ToListAsync(ct);
        var found = new List<PolicyInvalidPublishedRule>();
        var read = 0;
        foreach (var tenantId in tenants.Where(id => !NationalTenant.IsNational(id)))
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenantId);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var tenantDb = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
                // The second review of PLAN-49: inside a request the new session is still the caller's tenant, and the walk
                // would read that one tenant again for each agency and report them all as read.
                if (tenantDb.CurrentTenantId != tenantId)
                    return null;

                var validation = scope.ServiceProvider.GetService<IPolicyRuleValidationService>()
                                 ?? PolicyRuleValidationService.ForCatalog(tenantDb);
                found.AddRange((await FindAsync(tenantDb, validation, ct)).Where(rule => rule.TenantId == tenantId));
                read++;
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        return (found, read);
    }

    /// <summary>
    /// Called once at HR.Core's start, after every seed of the profile (the step <c>policy-rule-inventory</c> of Program.cs): a
    /// SelectCodePolicy rule is checked against the code policies' keys, which a new database gets after the policy seed, and the
    /// agencies a profile seeds are there to be read. It ran inside the national seed until the acceptance run of PLAN-49 showed
    /// that a first start then read no agency at all: the profile's seed creates them afterwards.
    /// </summary>
    public static async Task LogAsync(HRCoreDbContext db, IServiceProvider? services, CancellationToken ct = default)
    {
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(PolicyPublishedRuleInventory).FullName!);
        if (logger is null)
            return;

        // The first part is read as the national tenant, whatever the caller holds. At the step of Program.cs the flow is the
        // national tenant already, so this is a safeguard for any other caller; with no tenant the session would see no
        // template, and the summary says how many it read so that this cannot pass for "nothing refused".
        var session = TenantIdFlow.Get();
        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            await LogNationalAsync(db, services, logger, ct);
            await LogAgencyOverridesAsync(db, services, logger, ct);
        }
        finally
        {
            TenantIdFlow.Set(session);
        }
    }

    private static async Task LogNationalAsync(HRCoreDbContext db, IServiceProvider? services, ILogger logger, CancellationToken ct)
    {
        try
        {
            var validation = services?.GetService<IPolicyRuleValidationService>()
                             ?? PolicyRuleValidationService.ForCatalog(db);
            var (found, templatesRead) = await ReadAsync(db, validation, ct);
            var inForce = found.Where(r => r.InForce).ToList();
            foreach (var rule in inForce)
                logger.LogWarning(
                    "{Prefix}: {Domain} {Scope} tenant {TenantId} v{VersionNumber} ({VersionId}) rule {RuleKey}: {Codes}",
                    LogPrefix, rule.Domain, rule.Scope, rule.TenantId, rule.VersionNumber, rule.VersionId, rule.RuleKey, rule.Codes);
            logger.LogInformation(
                "{Prefix}: {Count} rule(s) of effective versions the validator refuses; {Historic} more in superseded versions (history, unchanged); {Templates} national template(s) read",
                LogPrefix, inForce.Count, found.Count - inForce.Count, templatesRead);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A report: it never fails the boot.
            logger.LogWarning(ex, "{Prefix}: the inventory could not be taken", LogPrefix);
        }
    }

    private static async Task LogAgencyOverridesAsync(HRCoreDbContext db, IServiceProvider? services, ILogger logger, CancellationToken ct)
    {
        try
        {
            var overrides = await FindAgencyOverridesAsync(db, services, ct);
            if (overrides is not { } taken)
            {
                logger.LogInformation("{Prefix}: not taken (each tenant cannot be read in its own session here)", OverrideLogPrefix);
                return;
            }

            var inForce = taken.Found.Where(r => r.InForce).ToList();
            foreach (var rule in inForce)
                logger.LogWarning(
                    "{Prefix}: {Domain} tenant {TenantId} v{VersionNumber} ({VersionId}) rule {RuleKey}: {Codes}",
                    OverrideLogPrefix, rule.Domain, rule.TenantId, rule.VersionNumber, rule.VersionId, rule.RuleKey, rule.Codes);
            logger.LogInformation(
                "{Prefix}: {Count} rule(s) of overrides in force the validator refuses, in {Tenants} of {Read} agency tenant(s) read; {Historic} more in superseded versions (history, unchanged)",
                OverrideLogPrefix, inForce.Count, inForce.Select(r => r.TenantId).Distinct().Count(), taken.TenantsRead, taken.Found.Count - inForce.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Prefix}: the inventory could not be taken", OverrideLogPrefix);
        }
    }
}
