using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
public class CodePoliciesController(HRCoreDbContext db, ICodeGenerator generator) : Controller
{
    public async Task<IActionResult> Index()
    {
        var templates = await db.CodePolicyTemplates.AsNoTracking()
            .Include(t => t.Versions)
            .OrderBy(t => t.EntityKind)
            .ThenBy(t => t.Name)
            .ToListAsync();
        return View(templates);
    }

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
    public async Task<IActionResult> Preview(CodeEntityKind entityKind, string? parentCode, string? entityPrefix)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(parentCode))
            facts["ParentCode"] = parentCode;
        if (!string.IsNullOrWhiteSpace(entityPrefix))
            facts["EntityPrefix"] = entityPrefix;

        var result = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = entityKind,
            Facts = facts,
            Preview = true
        });

        TempData["PreviewCode"] = result.Code;
        return RedirectToAction(nameof(Index));
    }
}
