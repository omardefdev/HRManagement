using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
[OrgScopeNotApplicable("reference data: the code policies; the issuance log is central")]
public class CodePoliciesController(HRCoreDbContext db, ICodeGenerator generator) : Controller
{
    /// <summary>
    /// PLAN-48 (§2): the agency's own overlays («تجاوز الجهة») and the national templates it inherits («وطني (موروث — للقراءة)»);
    /// an agency used to see an empty list.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var templates = await db.ReadableCodePolicyTemplates().AsNoTracking()
            .Include(t => t.Versions)
            .OrderBy(t => t.EntityKind)
            .ThenBy(t => t.PolicyKey)
            .ThenBy(t => t.Name)
            .ToListAsync();
        ViewBag.SessionTenantId = db.CurrentTenantId;
        return View(templates);
    }

    [OrgScopeCentralOnly("the issuance log lists the codes of every branch")]
    public async Task<IActionResult> Issuances(CodeEntityKind? entityKind)
    {
        var q = db.CodeIssuances.AsNoTracking().AsQueryable();
        if (entityKind.HasValue)
            q = q.Where(i => i.EntityKind == entityKind.Value);
        var items = await q.OrderByDescending(i => i.IssuedAt).Take(100).ToListAsync();
        ViewBag.EntityKind = entityKind;
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(CodeEntityKind entityKind, string? parentCode, string? entityPrefix, string? policyKey)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(parentCode))
            facts["ParentCode"] = parentCode;
        if (!string.IsNullOrWhiteSpace(entityPrefix))
            facts["EntityPrefix"] = entityPrefix;

        try
        {
            var result = await generator.GenerateAsync(new CodeGenerationRequest
            {
                EntityKind = entityKind,
                PolicyKey = string.IsNullOrWhiteSpace(policyKey) ? null : policyKey,
                Facts = facts,
                Preview = true
            });
            TempData["PreviewCode"] = result.Code;
        }
        catch (CodeFactMissingException ex)
        {
            // PLAN-48: a fact the number shows is required (it is never an empty segment); the page says which.
            TempData["PreviewError"] = ex.MessageAr;
        }
        catch (CodePolicyMissingException ex)
        {
            TempData["PreviewError"] = ex.MessageAr;
        }

        return RedirectToAction(nameof(Index));
    }
}
