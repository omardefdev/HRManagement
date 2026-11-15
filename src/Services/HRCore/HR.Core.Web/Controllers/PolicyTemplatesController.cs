using System.Globalization;
using HRManagement.ServiceDefaults.Localization;
using System.Security.Claims;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-47 (D-POL-3): the policy pages. Reading is open to the HR.Core readers, the authors and the approvers; drafts belong
/// to <see cref="HrManagementPolicies.PolicyAuthor"/>, publishing to <see cref="HrManagementPolicies.PolicyApprover"/>, and the
/// publisher must not be among the draft's editors.
/// </summary>
[Authorize(Policy = HrManagementPolicies.PolicyReader)]
// PLAN-51 (§4, P-6): replaces the module filter's name list; it names the class policy every action here already requires.
[ModulePolicyOverride(HrManagementPolicies.PolicyReader)]
[OrgScopeNotApplicable("reference data: the policy templates (inventory C: who writes them is RBAC, IAM-H)")]
public class PolicyTemplatesController(
    HRCoreDbContext db,
    IPolicyOrchestrator orchestrator,
    PolicyPublishService publishService,
    PolicyDraftService drafts,
    IAuthorizationService authorization) : Controller
{
    public async Task<IActionResult> Index([FromServices] IPolicyModeResolver modeResolver)
    {
        // PLAN-48: the agency's own templates and the national ones it inherits (read-only).
        var templates = await db.ReadablePolicyTemplates().AsNoTracking()
            .Include(t => t.Versions)
            .OrderBy(t => t.Domain)
            .ToListAsync();
        ViewBag.Domains = await BuildDomainRowsAsync(modeResolver);
        ViewBag.SessionIsNational = NationalTenant.IsNational(db.CurrentTenantId);
        ViewBag.CanAuthor = (await authorization.AuthorizeAsync(User, HrManagementPolicies.PolicyAuthor)).Succeeded;
        return View(templates);
    }

    /// <summary>
    /// PLAN-49 (G-POL-02): the domain table of the page, read from <see cref="PolicyDomainCatalog"/> (the registry that lived in
    /// this file is gone): status, owner, the mode HR.Core applies (the resolver), the latest ledger row of the session's tenant,
    /// and the call sites or the reason the domain is out of scope.
    /// </summary>
    private async Task<IReadOnlyList<PolicyDomainRow>> BuildDomainRowsAsync(IPolicyModeResolver modeResolver)
    {
        var rows = new List<PolicyDomainRow>();
        foreach (var descriptor in PolicyDomainCatalog.All)
        {
            PolicyDomainLastDecision? last = null;
            if (descriptor.IsGoverned)
            {
                var row = await db.PolicyEnforcementLedgers.AsNoTracking()
                    .Where(l => l.Domain == descriptor.Domain)
                    .OrderByDescending(l => l.OccurredAt)
                    .Select(l => new { l.OccurredAt, l.Action, l.Mode, l.CallerClientId })
                    .FirstOrDefaultAsync();
                if (row is not null)
                    last = new PolicyDomainLastDecision(row.OccurredAt, row.Action, row.Mode.ToString(), row.CallerClientId);
            }

            rows.Add(new PolicyDomainRow(descriptor, modeResolver.Resolve(descriptor.Domain), last));
        }

        return rows;
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var t = await db.ReadablePolicyTemplates()
            .AsNoTracking()
            .Include(x => x.Versions)
            .ThenInclude(v => v.Rules)
            .Include(x => x.Versions)
            .ThenInclude(v => v.Publications)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        // PLAN-48: a national template an agency inherits is shown read-only (the agency authors its override instead).
        var inherited = db.IsInheritedNational(t.TenantId);
        ViewBag.Inherited = inherited;
        ViewBag.CanAuthor = !inherited && (await authorization.AuthorizeAsync(User, HrManagementPolicies.PolicyAuthor)).Succeeded;
        ViewBag.EffectiveVersionId = PolicyDraftService.EffectiveVersion(t, DateTime.UtcNow)?.Id;
        ViewBag.FactsSchema = await FactsSchemaAsync(t.Domain);
        ViewBag.CanSeeTechnical = await CanSeeTechnicalAsync();
        return View(t);
    }

    [HttpGet]
    public async Task<IActionResult> Simulate(Guid versionId)
    {
        var version = await db.ReadablePolicyVersions()
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId);
        if (version is null) return NotFound();
        ViewBag.VersionId = versionId;
        ViewBag.Domain = version.PolicyTemplate.Domain;
        await SimulationFormAsync(version.PolicyTemplate.Domain, version.PolicyTemplate.SubjectType);
        return View();
    }

    /// <summary>Evaluates the published policy of the version's domain, now or as of a date (PLAN-47 §6).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(Guid versionId, string? factsJson, string? asOfUtc = null, string? mode = null,
        [FromForm(Name = "fact")] Dictionary<string, string>? formFacts = null)
    {
        // PLAN-67 (§5, G-UX-06): the facts come from the form generated from the domain's schema, or, in the advanced tab, as JSON. The
        // form builds the same dictionary the JSON gives; the evaluation below does not change.
        Dictionary<string, string>? facts;
        if (string.Equals(mode, "form", StringComparison.Ordinal))
        {
            facts = (formFacts ?? new Dictionary<string, string>())
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);
            factsJson = System.Text.Json.JsonSerializer.Serialize(facts);
        }
        else
        {
            try
            {
                facts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(factsJson ?? "")
                    ?? new Dictionary<string, string>();
            }
            catch
            {
                TempData["Error"] = "JSON غير صالح";
                return RedirectToAction(nameof(Simulate), new { versionId });
            }
        }

        DateTime? asOf = null;
        if (!string.IsNullOrWhiteSpace(asOfUtc))
        {
            // PLAN-67 (D-67-2): the form sends local time (datetime-local); an ISO value with an offset is read as such.
            if (!HrLocalTime.TryParseUserTime(asOfUtc, HrLocalTime.Zone, out var parsed))
            {
                TempData["Error"] = "التاريخ غير صالح";
                return RedirectToAction(nameof(Simulate), new { versionId });
            }
            asOf = parsed;
        }

        var request = new PolicyEnforceRequest
        {
            Domain = (await db.ReadablePolicyVersions().Include(v => v.PolicyTemplate).FirstAsync(v => v.Id == versionId)).PolicyTemplate.Domain,
            // PLAN-48: the session's decision (the national rules and its own override, merged), as POST api/policies/evaluate.
            TenantId = db.CurrentTenantId,
            Facts = facts,
            Mode = PolicyEnforcementMode.Enforce,
            AsOfUtc = asOf
        };
        var decision = await orchestrator.EvaluateAsync(request);
        ViewBag.Decision = decision;
        // PLAN-67 (G-UX-04): the page names the version by its number, not its id.
        ViewBag.DecisionVersionNumber = decision.PolicyVersionId is Guid decidedBy
            ? await db.ReadablePolicyVersions().Where(v => v.Id == decidedBy).Select(v => (int?)v.VersionNumber).FirstOrDefaultAsync()
            : null;
        ViewBag.VersionId = versionId;
        ViewBag.FactsJson = factsJson;
        ViewBag.AsOfUtc = asOfUtc;
        ViewBag.Mode = string.Equals(mode, "form", StringComparison.Ordinal) ? "form" : "json";
        ViewBag.Facts = facts;
        var simulated = await db.ReadablePolicyVersions().Include(v => v.PolicyTemplate).FirstAsync(v => v.Id == versionId);
        ViewBag.Domain = simulated.PolicyTemplate.Domain;
        await SimulationFormAsync(simulated.PolicyTemplate.Domain, simulated.PolicyTemplate.SubjectType);
        return View("Simulate");
    }

    public async Task<IActionResult> Ledger(string? domain)
    {
        var q = db.PolicyEnforcementLedgers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(domain))
            q = q.Where(l => l.Domain == domain);
        var items = await q.OrderByDescending(l => l.OccurredAt).Take(100).ToListAsync();
        ViewBag.Domain = domain;
        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public IActionResult CreateDraft(string domain, string subjectType, [FromServices] IPolicyRuleValidationService validation)
    {
        // PLAN-49 (D-POL-1): no draft form for a domain the engine does not govern; the validator's refusal, shown on the page.
        if (validation.ValidateDomain(domain) is { Count: > 0 } outOfScope)
        {
            TempData["Error"] = outOfScope[0].MessageAr;
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Domain = domain;
        ViewBag.SubjectType = subjectType;
        ViewBag.SessionIsNational = NationalTenant.IsNational(db.CurrentTenantId);
        return View();
    }

    /// <summary>
    /// A draft of the domain's template: a copy of its effective version (§5); a missing template is created. PLAN-48 (§4,
    /// D-TEN-10): outside the national tenant it is the agency's override (stricter rules only) and a request for the national scope
    /// answers 403; inside it the national template, unless the trial agency's override is asked.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> CreateDraft(string domain, string subjectType, string name, string? scope)
    {
        if (!PoliciesController.TryParseScope(scope, out var requested))
            return BadRequest("النطاق غير معروف: National أو TenantOverride");

        var result = await drafts.CreateDomainDraftAsync(domain, subjectType, name, requested, ParseUserId(User));
        if (result.Outcome == PolicyDraftOutcome.Forbidden)
        {
            var forbidden = View("~/Views/Shared/NationalScopeError.cshtml", result.MessageAr);
            forbidden.StatusCode = StatusCodes.Status403Forbidden;
            return forbidden;
        }

        if (!result.Succeeded)
        {
            TempData["Error"] = result.MessageAr;
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(EditRules), new { versionId = result.Id });
    }

    /// <summary>§5: a new draft of this template, a copy of its effective version.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public Task<IActionResult> CreateTemplateDraft(Guid templateId) => CreateDraftForTemplate(templateId);

    /// <summary>§7: a rollback draft to an earlier version, with a written reason.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> CreateRollbackDraft(Guid templateId, Guid targetVersionId, string? reason)
    {
        var result = await drafts.CreateRollbackDraftAsync(templateId, targetVersionId, reason, ParseUserId(User));
        if (!result.Succeeded)
        {
            TempData["Error"] = result.MessageAr;
            return RedirectToAction(nameof(Details), new { id = templateId });
        }

        TempData["Success"] = "أُنشئت مسودة التراجع";
        return RedirectToAction(nameof(EditRules), new { versionId = result.Id });
    }

    /// <summary>The draft page: the author edits it, and an approver who is not an editor reviews it read-only and publishes it.</summary>
    [HttpGet]
    public async Task<IActionResult> EditRules(Guid versionId)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId);
        if (version is null) return NotFound();
        if (version.Status != PolicyVersionStatus.Draft)
        {
            TempData["Error"] = "يمكن تعديل المسودات فقط";
            return RedirectToAction(nameof(Details), new { id = version.PolicyTemplateId });
        }

        ViewBag.Editors = await db.PolicyVersionEdits.AsNoTracking()
            .Where(e => e.PolicyTemplateVersionId == versionId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();
        var userId = ParseUserId(User);
        var editorIds = ((List<PolicyVersionEdit>)ViewBag.Editors).Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).ToHashSet();
        if (version.CreatedBy is Guid creator)
            editorIds.Add(creator);
        ViewBag.CanPublish = userId is Guid me
                             && !editorIds.Contains(me)
                             && (await authorization.AuthorizeAsync(User, HrManagementPolicies.PolicyApprover)).Succeeded;
        ViewBag.CanAuthor = (await authorization.AuthorizeAsync(User, HrManagementPolicies.PolicyAuthor)).Succeeded;
        // The revision the reviewer sees is the one published: a change after this page answers a conflict.
        ViewBag.Revision = publishService.Revision(version);
        ViewBag.FactsSchema = await FactsSchemaAsync(version.PolicyTemplate.Domain);
        ViewBag.CanSeeTechnical = await CanSeeTechnicalAsync();
        return View(version);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> AddRule(Guid versionId, string ruleKey, string? name, int priority, string whenJson, string thenJson)
    {
        // PLAN-47: validated before it is saved; the message names the JSON path.
        var result = await drafts.AddRuleAsync(versionId, new PolicyRuleInput(ruleKey, name, priority, whenJson, thenJson), ParseUserId(User));
        return DraftRedirect(result, versionId, "أُضيفت القاعدة");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> UpdateRule(Guid versionId, Guid ruleId, string ruleKey, string? name, int priority, string whenJson, string thenJson)
    {
        var result = await drafts.UpdateRuleAsync(versionId, ruleId, new PolicyRuleInput(ruleKey, name, priority, whenJson, thenJson), ParseUserId(User));
        return DraftRedirect(result, versionId, "عُدّلت القاعدة");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> RemoveRule(Guid versionId, Guid ruleId)
    {
        var result = await drafts.RemoveRuleAsync(versionId, ruleId, ParseUserId(User));
        return DraftRedirect(result, versionId, "حُذفت القاعدة");
    }

    /// <summary>§6: the effective time is entered as UTC (the page shows the user's local time beside it).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> SetEffectiveFrom(Guid versionId, string? effectiveFromUtc)
    {
        DateTime? value = null;
        if (!string.IsNullOrWhiteSpace(effectiveFromUtc))
        {
            // PLAN-67 (D-67-2): the form sends local time (datetime-local); an ISO value with an offset is read as such.
            if (!HrLocalTime.TryParseUserTime(effectiveFromUtc, HrLocalTime.Zone, out var parsed))
            {
                TempData["Error"] = "التاريخ غير صالح";
                return RedirectToAction(nameof(EditRules), new { versionId });
            }
            value = parsed;
        }

        var result = await drafts.SetEffectiveFromAsync(versionId, value, ParseUserId(User));
        return DraftRedirect(result, versionId, "حُدّد تاريخ النفاذ");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> DiscardDraft(Guid versionId)
    {
        var templateId = await db.PolicyTemplateVersions.Where(v => v.Id == versionId).Select(v => (Guid?)v.PolicyTemplateId).FirstOrDefaultAsync();
        var result = await drafts.DiscardDraftAsync(versionId, ParseUserId(User));
        if (!result.Succeeded)
            TempData["Error"] = result.MessageAr;
        else
            TempData["Success"] = "أُهملت المسودة";
        return templateId is Guid id ? RedirectToAction(nameof(Details), new { id }) : RedirectToAction(nameof(Index));
    }

    /// <summary>PLAN-47 (D-POL-3): the approver publishes a draft they did not edit. There is no same-actor override.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyApprover)]
    public async Task<IActionResult> PublishFromUi(Guid versionId, string? evidenceRef, uint? revision)
    {
        var result = await publishService.PublishAsync(new PolicyPublishCommand(versionId, new PolicyActor(ParseUserId(User), User), evidenceRef, revision));
        if (result.Outcome == PolicyPublishOutcome.NotFound)
            return NotFound();
        if (result.Outcome == PolicyPublishOutcome.Forbidden)
        {
            // PLAN-48: a national version published from an agency's session; the version is not the session's to look up.
            var forbidden = View("~/Views/Shared/NationalScopeError.cshtml", result.MessageAr);
            forbidden.StatusCode = StatusCodes.Status403Forbidden;
            return forbidden;
        }

        var templateId = result.Version?.PolicyTemplateId
                         ?? await db.PolicyTemplateVersions.Where(v => v.Id == versionId).Select(v => v.PolicyTemplateId).FirstAsync();
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Outcome == PolicyPublishOutcome.Invalid && result.IssueList.Count > 0
                ? $"{result.MessageAr}: {string.Join(" | ", result.IssueList.Select(i => $"{i.JsonPath}: {i.MessageAr}"))}"
                : result.MessageAr;
            return RedirectToAction(nameof(Details), new { id = templateId });
        }

        TempData["Success"] = "تم النشر";
        return RedirectToAction(nameof(Details), new { id = templateId });
    }

    [HttpGet]
    public async Task<IActionResult> ShadowReport(string? domain)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var q = db.PolicyEnforcementLedgers.AsNoTracking()
            .Where(l => l.Mode == PolicyEnforcementModeValue.Shadow && l.OccurredAt >= since);
        if (!string.IsNullOrWhiteSpace(domain))
            q = q.Where(l => l.Domain == domain);
        var items = await q.OrderByDescending(l => l.OccurredAt).Take(200).ToListAsync();
        ViewBag.Domain = domain;
        // PLAN-67 (D-67-4, review finding 5): the differences' JSON for the policy roles only.
        ViewBag.CanSeeTechnical = await CanSeeTechnicalAsync();
        return View(items);
    }

    private async Task<IActionResult> CreateDraftForTemplate(Guid templateId)
    {
        var result = await drafts.CreateDraftAsync(templateId, copyFromVersionId: null, ParseUserId(User));
        if (!result.Succeeded)
        {
            TempData["Error"] = result.MessageAr;
            return RedirectToAction(nameof(Details), new { id = templateId });
        }

        return RedirectToAction(nameof(EditRules), new { versionId = result.Id });
    }

    private IActionResult DraftRedirect(PolicyDraftResult result, Guid versionId, string successAr)
    {
        if (result.Outcome == PolicyDraftOutcome.NotFound && result == PolicyDraftResult.VersionNotFound)
            return NotFound();
        if (result.Outcome == PolicyDraftOutcome.NotDraft)
            return Conflict(result.MessageAr);
        if (result.Succeeded)
            TempData["Success"] = successAr;
        else
            TempData["Error"] = result.MessageAr;
        return RedirectToAction(nameof(EditRules), new { versionId });
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    /// <summary>PLAN-67 (§5): the facts schema the rule sentences and the simulation form read (the agency's own first, then the national one).</summary>
    private async Task<PolicyFactsSchemaDefinition?> FactsSchemaAsync(string domain)
    {
        var entity = await db.PolicyFactsSchemas.OwnOrNational(db).AsNoTracking()
            .Where(s => s.Domain == domain)
            .OwnFirst(db.CurrentTenantId)
            .FirstOrDefaultAsync();
        return PolicyEntityMapper.ParseSchema(entity);
    }

    /// <summary>PLAN-67 (D-67-4): a rule's JSON is shown, under «عرض تقني», to policy authors, approvers and the admin only.</summary>
    private async Task<bool> CanSeeTechnicalAsync() =>
        User.IsInRole(HrManagementRoles.Admin)
        || (await authorization.AuthorizeAsync(User, HrManagementPolicies.PolicyAuthor)).Succeeded
        || (await authorization.AuthorizeAsync(User, HrManagementPolicies.PolicyApprover)).Succeeded;

    /// <summary>PLAN-67 (§5): what the simulation form is generated from: the schema's fields, and the actions the domain's subject takes.</summary>
    private async Task SimulationFormAsync(string domain, string subjectType)
    {
        var schema = await FactsSchemaAsync(domain);
        ViewBag.FactsSchema = schema;
        ViewBag.Actions = PolicyDomainCatalog.Find(domain)?.SubjectActions.GetValueOrDefault(subjectType)?.Order(StringComparer.Ordinal).ToList()
            ?? (IReadOnlyList<string>)[];
        ViewBag.CanSeeTechnical = await CanSeeTechnicalAsync();
    }
}

/// <summary>PLAN-49: the latest ledger row of a domain in the session's tenant, as the policy page shows it.</summary>
public sealed record PolicyDomainLastDecision(DateTime OccurredAt, string Action, string Mode, string? CallerClientId);

/// <summary>
/// PLAN-49 (G-POL-02): one row of the domain table on the policy page: the catalog's descriptor, the mode HR.Core applies and
/// the domain's latest ledger row. The page has no list of its own.
/// </summary>
public sealed record PolicyDomainRow(PolicyDomainDescriptor Descriptor, PolicyEnforcementMode Mode, PolicyDomainLastDecision? LastDecision);
