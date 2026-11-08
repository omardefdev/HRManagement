using System.Globalization;
using System.Security.Claims;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
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
public class PolicyTemplatesController(
    HRCoreDbContext db,
    IPolicyOrchestrator orchestrator,
    PolicyPublishService publishService,
    PolicyDraftService drafts,
    IAuthorizationService authorization) : Controller
{
    public async Task<IActionResult> Index()
    {
        var templates = await db.PolicyTemplates.AsNoTracking()
            .Include(t => t.Versions)
            .OrderBy(t => t.Domain)
            .ToListAsync();
        ViewBag.Domains = PolicyDomainRegistry.All;
        return View(templates);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var t = await db.PolicyTemplates
            .AsNoTracking()
            .Include(x => x.Versions)
            .ThenInclude(v => v.Rules)
            .Include(x => x.Versions)
            .ThenInclude(v => v.Publications)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        ViewBag.CanAuthor = (await authorization.AuthorizeAsync(User, HrManagementPolicies.PolicyAuthor)).Succeeded;
        ViewBag.EffectiveVersionId = PolicyDraftService.EffectiveVersion(t, DateTime.UtcNow)?.Id;
        return View(t);
    }

    [HttpGet]
    public async Task<IActionResult> Simulate(Guid versionId)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId);
        if (version is null) return NotFound();
        ViewBag.VersionId = versionId;
        ViewBag.Domain = version.PolicyTemplate.Domain;
        return View();
    }

    /// <summary>Evaluates the published policy of the version's domain, now or as of a date (PLAN-47 §6).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(Guid versionId, string factsJson, string? asOfUtc = null)
    {
        Dictionary<string, string>? facts;
        try
        {
            facts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(factsJson)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            TempData["Error"] = "JSON غير صالح";
            return RedirectToAction(nameof(Simulate), new { versionId });
        }

        DateTime? asOf = null;
        if (!string.IsNullOrWhiteSpace(asOfUtc))
        {
            if (!DateTime.TryParse(asOfUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                TempData["Error"] = "التاريخ غير صالح";
                return RedirectToAction(nameof(Simulate), new { versionId });
            }
            asOf = parsed;
        }

        var request = new PolicyEnforceRequest
        {
            Domain = (await db.PolicyTemplateVersions.Include(v => v.PolicyTemplate).FirstAsync(v => v.Id == versionId)).PolicyTemplate.Domain,
            Facts = facts,
            Mode = PolicyEnforcementMode.Enforce,
            AsOfUtc = asOf
        };
        var decision = await orchestrator.EvaluateAsync(request);
        ViewBag.Decision = decision;
        ViewBag.VersionId = versionId;
        ViewBag.FactsJson = factsJson;
        ViewBag.AsOfUtc = asOfUtc;
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
    public IActionResult CreateDraft(string domain, string subjectType)
    {
        ViewBag.Domain = domain;
        ViewBag.SubjectType = subjectType;
        return View();
    }

    /// <summary>A draft of the domain's national template: a copy of its effective version (§5). A missing template is created.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> CreateDraft(string domain, string subjectType, string name)
    {
        var template = await db.PolicyTemplates
            .FirstOrDefaultAsync(t => t.Domain == domain && t.Scope == PolicyTemplateScope.National);
        if (template is null)
        {
            template = new PolicyTemplate
            {
                Domain = domain,
                Name = name,
                SubjectType = subjectType,
                Scope = PolicyTemplateScope.National,
                Description = "مسودة جديدة"
            };
            db.PolicyTemplates.Add(template);
            await db.SaveChangesAsync();
        }

        return await CreateDraftForTemplate(template.Id);
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
            if (!DateTime.TryParse(effectiveFromUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
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
}

public static class PolicyDomainRegistry
{
    public static IReadOnlyList<(string Domain, string Service, string MigrationStatus)> All { get; } =
    [
        (PolicyDomains.AssetsCustody, "Admin.Logistics", "Shadow"),
        (PolicyDomains.AssetsProcurement, "Admin.Logistics", "Shadow"),
        (PolicyDomains.IDCardLifecycle, "IDCard", "Shadow"),
        (PolicyDomains.ContractsLeave, "HR.Contracts", "Shadow"),
        (PolicyDomains.ContractsAdvance, "HR.Contracts", "Static"),
        (PolicyDomains.ContractsHire, "HR.Contracts", "Static"),
        (PolicyDomains.ContractsSeparation, "HR.Contracts", "Static"),
        (PolicyDomains.ContractsContract, "HR.Contracts", "Static"),
        (PolicyDomains.MovementsTransfer, "HR.Movements", "Shadow"),
        (PolicyDomains.MovementsWorkforce, "HR.Movements", "Shadow"),
        (PolicyDomains.CoreRecruitment, "HR.Core", "Shadow"),
        (PolicyDomains.CoreEmployment, "HR.Core", "Shadow"),
        (PolicyDomains.HealthInsurance, "HR.Health", "Shadow"),
        (PolicyDomains.ComplianceDisciplinary, "Compliance", "Shadow"),
        (PolicyDomains.PayrollPeriod, "Payroll", "Shadow"),
        (PolicyDomains.ITTickets, "IT.Infra", "Shadow"),
        (PolicyDomains.ArchiveRetention, "Records.Archive", "Shadow"),
        (PolicyDomains.TrainingLearning, "HR.Training", "Shadow"),
        (PolicyDomains.AnalyticsEtl, "Dev.Analytics", "Shadow")
    ];
}
