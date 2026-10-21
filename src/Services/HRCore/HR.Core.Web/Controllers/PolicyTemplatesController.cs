using System.Security.Claims;
using HR.Core.Domain.Entities.Policy;
using HRManagement.SharedKernel.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class PolicyTemplatesController(
    HRCoreDbContext db,
    IPolicyOrchestrator orchestrator,
    IPolicyVersionPublisher publisher) : Controller
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
            .Include(x => x.Versions)
            .ThenInclude(v => v.Rules)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(Guid versionId, string factsJson)
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

        var request = new HRManagement.SharedKernel.Policy.PolicyEnforceRequest
        {
            Domain = (await db.PolicyTemplateVersions.Include(v => v.PolicyTemplate).FirstAsync(v => v.Id == versionId)).PolicyTemplate.Domain,
            Facts = facts,
            Mode = HRManagement.SharedKernel.Policy.PolicyEnforcementMode.Enforce
        };
        var decision = await orchestrator.EvaluateAsync(request);
        ViewBag.Decision = decision;
        ViewBag.VersionId = versionId;
        ViewBag.FactsJson = factsJson;
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
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public IActionResult CreateDraft(string domain, string subjectType)
    {
        ViewBag.Domain = domain;
        ViewBag.SubjectType = subjectType;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> CreateDraft(string domain, string subjectType, string name)
    {
        var template = await db.PolicyTemplates
            .Include(t => t.Versions)
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
        }

        var nextVer = (template.Versions.MaxBy(v => v.VersionNumber)?.VersionNumber ?? 0) + 1;
        var version = new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            VersionNumber = nextVer,
            Status = PolicyVersionStatus.Draft,
            CreatedBy = ParseUserId(User)
        };
        db.PolicyTemplateVersions.Add(version);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(EditRules), new { versionId = version.Id });
    }

    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
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
        return View(version);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> AddRule(Guid versionId, string ruleKey, string? name, int priority, string whenJson, string thenJson)
    {
        var version = await db.PolicyTemplateVersions.FindAsync(versionId);
        if (version is null || version.Status != PolicyVersionStatus.Draft) return BadRequest();
        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersionId = versionId,
            RuleKey = ruleKey,
            Name = name,
            Priority = priority,
            WhenJson = whenJson,
            ThenJson = thenJson
        });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(EditRules), new { versionId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> PublishFromUi(Guid versionId, string? evidenceRef, bool forceSameActorForLab = false)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .FirstOrDefaultAsync(v => v.Id == versionId);
        if (version is null) return NotFound();
        if (version.Status != PolicyVersionStatus.Draft)
        {
            TempData["Error"] = "يمكن نشر المسودات فقط";
            return RedirectToAction(nameof(Details), new { id = version.PolicyTemplateId });
        }

        var makerId = version.CreatedBy ?? ParseUserId(User);
        var checkerId = ParseUserId(User);
        if (makerId == checkerId && !forceSameActorForLab)
        {
            TempData["Error"] = "four-eyes: الناشر يجب أن يختلف عن المحرّر";
            return RedirectToAction(nameof(EditRules), new { versionId });
        }

        await publisher.PublishDraftAsync(version, makerId, checkerId, evidenceRef);
        TempData["Success"] = "تم النشر";
        return RedirectToAction(nameof(Details), new { id = version.PolicyTemplateId });
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
