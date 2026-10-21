using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Services.Legal;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class ServiceRegimeExceptionsController(
    ServiceRegimeExceptionService exceptionService,
    IServiceRegimeProvider regimeProvider,
    ILegislationRegisterProvider legislationProvider) : Controller
{
    public async Task<IActionResult> Index(ServiceRegimeExceptionType? filter)
    {
        var items = await exceptionService.ListOpenAsync(filter);
        var legalLabels = new Dictionary<string, string>();
        foreach (var legalSourceId in items.Select(i => i.LegalSourceId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            var source = await legislationProvider.GetByIdAsync(legalSourceId!);
            legalLabels[legalSourceId!] = source?.DisplayLabel ?? legalSourceId!;
        }

        ViewBag.LegalSourceLabels = legalLabels;
        ViewBag.RegimeOptions = new SelectList(
            (await regimeProvider.ListActiveAsync()).Select(r => new { r.LegalRegimeId, Label = r.DisplayLabel }),
            "LegalRegimeId",
            "Label");
        ViewBag.LegalDecisionOptions = new SelectList(
            LegalDecisionRegisterProvider.ListAll().Select(d => new { d.DecisionId, Label = $"{d.DecisionId} — {d.OfficialSource}" }),
            "DecisionId",
            "Label");
        ViewBag.Filter = filter;
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Resolve(
        Guid id,
        string resolvedRegimeId,
        string transitionDecisionRef,
        DateTime effectiveFrom,
        string? conditionCode,
        string resolution,
        string? conflictNotes = null,
        string? legalDecisionId = null)
    {
        var error = await exceptionService.ResolveAsync(
            id,
            resolvedRegimeId,
            transitionDecisionRef,
            effectiveFrom,
            conditionCode,
            resolution,
            ParseUserId(User),
            conflictNotes);

        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "تم حل استثناء نظام الخدمة وتطبيق الانتقال.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> ResolveArticle159(
        Guid id,
        string legalDecisionId,
        string resolution,
        string? conflictNotes = null,
        string? resolvedRegimeId = null,
        string? transitionDecisionRef = null,
        DateTime? effectiveFrom = null,
        string? conditionCode = null)
    {
        var error = await exceptionService.ResolveArticle159Async(
            id,
            legalDecisionId,
            resolution,
            ParseUserId(User),
            conflictNotes,
            resolvedRegimeId,
            transitionDecisionRef,
            effectiveFrom,
            conditionCode);

        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index), new { filter = ServiceRegimeExceptionType.Article159Conflict });
        }

        TempData["Success"] = "تم حل استثناء المادة 159.";
        return RedirectToAction(nameof(Index), new { filter = ServiceRegimeExceptionType.Article159Conflict });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Dismiss(Guid id, string resolution)
    {
        var error = await exceptionService.DismissAsync(id, resolution, ParseUserId(User));
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "تم رفض/إغلاق الاستثناء.";
        return RedirectToAction(nameof(Index));
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
