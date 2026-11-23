using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// The legal frameworks of payroll (PAY-002, PLAN-54). A draft is created from a regime's template or a clone, or imported from a legal
/// pack (<c>Payroll.MasterData</c>); its tables are edited by export and import, its numeric fields and its effective date on the edit
/// form. Every write names its policy.
/// </summary>
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScopeNotApplicable("a payroll legal framework belongs to the agency, not to a branch")]
public class PayrollConfigurationsController(
    PayrollDbContext db,
    IMediator mediator,
    IPayrollRepository repository,
    IPayrollCalendar calendar) : Controller
{
    public async Task<IActionResult> Index(string? q, bool? isActive, PayrollConfigPublicationStatus? publicationStatus)
    {
        var query = db.PayrollConfigurations.AsNoTracking().Include(c => c.TaxBrackets).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c => c.Code.Contains(term) || c.NameAr.Contains(term));
        }
        if (isActive.HasValue) query = query.Where(c => c.IsActive == isActive.Value);
        if (publicationStatus.HasValue) query = query.Where(c => c.PublicationStatus == publicationStatus.Value);
        var items = await query.OrderByDescending(c => c.IsActive).ThenBy(c => c.Code).ThenByDescending(c => c.VersionNumber)
            .Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Q = q;
        ViewBag.IsActive = isActive;
        ViewBag.PublicationStatus = publicationStatus;
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var model = await DetailsModelAsync(id, refusal: null);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>The version with every table that governs pay, its editors and its active attestation.</summary>
    private async Task<PayrollFrameworkDetailsViewModel?> DetailsModelAsync(Guid id, string? refusal)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .Include(c => c.StatutoryDeductions)
            .Include(c => c.Holidays)
            .Include(c => c.PriorityRules)
            .Include(c => c.FieldSources)
            .Include(c => c.Editors)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return null;

        var attestation = await db.LegalAttestations.AsNoTracking()
            .Where(a => a.PayrollConfigurationId == id && a.Status == "Active")
            .OrderByDescending(a => a.AttestedAt)
            .FirstOrDefaultAsync();
        return new PayrollFrameworkDetailsViewModel { Version = config, Attestation = attestation, Refusal = refusal };
    }

    // ------------------------------------------------------------------ create (PLAN-54 §1)

    private async Task FillCreateListsAsync(PayrollFrameworkCreateViewModel model)
    {
        ViewBag.Regimes = new SelectList(
            LegalRegimeCodes.All.Select(code => new { Code = code, Name = LegalRegimeCodes.DisplayNameAr(code) }), "Code", "Name", model.RegimeCode);
        var versions = await db.PayrollConfigurations.AsNoTracking()
            .OrderBy(c => c.Code).ThenByDescending(c => c.VersionNumber)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .Select(c => new { c.Id, c.Code, c.VersionNumber, c.NameAr })
            .ToListAsync();
        ViewBag.Versions = new SelectList(
            versions.Select(v => new { v.Id, Name = FormattableString.Invariant($"{v.Code} v{v.VersionNumber} — {v.NameAr}") }), "Id", "Name", model.CloneFromId);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Create(Guid? cloneFromId)
    {
        var model = new PayrollFrameworkCreateViewModel
        {
            CloneFromId = cloneFromId,
            EffectiveFrom = calendar.FirstDayOfCurrentMonth.AddMonths(1),
        };
        await FillCreateListsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Create(PayrollFrameworkCreateViewModel model)
    {
        var result = await mediator.Send(new CreatePayrollFrameworkCommand(
            model.RegimeCode, model.CloneFromId, model.Code, model.NameAr, model.EffectiveFrom, PayProfileChangeHttp.RequireUserId(User)));
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? PayrollFrameworkMessages.NotFound);
            await FillCreateListsAsync(model);
            Response.StatusCode = result.StatusCode;
            return View(model);
        }

        TempData["Success"] = FormattableString.Invariant($"أُنشئت مسودة الإطار {result.Code} (v{result.VersionNumber}).");
        return RedirectToAction(nameof(Details), new { id = result.FrameworkId });
    }

    // ------------------------------------------------------------------ import and export (PLAN-54 §1)

    private async Task FillImportListsAsync(PayrollFrameworkImportViewModel model)
    {
        var drafts = await db.PayrollConfigurations.AsNoTracking()
            .Where(c => c.PublicationStatus == PayrollConfigPublicationStatus.Draft)
            .OrderBy(c => c.Code).ThenByDescending(c => c.VersionNumber)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .Select(c => new { c.Id, c.Code, c.VersionNumber })
            .ToListAsync();
        ViewBag.Drafts = new SelectList(
            drafts.Select(d => new { d.Id, Name = FormattableString.Invariant($"{d.Code} v{d.VersionNumber}") }), "Id", "Name", model.TargetDraftId);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Import(Guid? targetDraftId)
    {
        var model = new PayrollFrameworkImportViewModel { TargetDraftId = targetDraftId };
        await FillImportListsAsync(model);
        return View(model);
    }

    /// <summary>
    /// The upload of a legal pack: one JSON file, no larger than the reader's limit. The file's text goes to the importer and nowhere
    /// else: it is not logged, and a refusal names the field and the row, never the content.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    [RequestSizeLimit(PayrollFrameworkUpload.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = PayrollFrameworkUpload.MaxRequestBytes)]
    public async Task<IActionResult> Import(PayrollFrameworkImportViewModel model)
    {
        var (json, uploadError) = await PayrollFrameworkUpload.ReadAsync(model.PackFile, HttpContext.RequestAborted);
        var result = uploadError is not null
            ? PayrollFrameworkResult.Invalid(uploadError)
            : await mediator.Send(new ImportPayrollFrameworkPackCommand(
                json,
                string.IsNullOrWhiteSpace(model.Code) ? null : model.Code.Trim(),
                model.EffectiveFrom,
                model.TargetDraftId,
                PayProfileChangeHttp.RequireUserId(User)));
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? PayrollFrameworkMessages.NotFound);
            ViewBag.PackErrors = result.Errors;
            await FillImportListsAsync(model);
            Response.StatusCode = result.StatusCode;
            return View(model);
        }

        TempData["Success"] = FormattableString.Invariant(
            $"استُوردت الحزمة مسودةً للإطار {result.Code} (v{result.VersionNumber}). الخطوات التالية: اعتماد، ثم تصديق قانوني، ثم نشر، بثلاثة أشخاص.");
        return RedirectToAction(nameof(Details), new { id = result.FrameworkId });
    }

    /// <summary>The version as a legal pack: a download, never rendered.</summary>
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Export(Guid id)
    {
        var export = await mediator.Send(new ExportPayrollFrameworkQuery(id));
        return export is null
            ? NotFound()
            : File(Encoding.UTF8.GetBytes(export.Json), "application/json", export.FileName);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var config = await db.PayrollConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        var model = PayrollFrameworkEditViewModel.From(config);
        // An immutable version is edited as a new draft, and a draft never inherits a date (P-6): the form proposes the first day of
        // next month, and the user decides.
        if (config.IsImmutable)
            model.EffectiveFrom = calendar.FirstDayOfCurrentMonth.AddMonths(1);
        return View(model);
    }

    /// <summary>
    /// The numeric fields and the effective date of a draft; the four tables are edited by export and import. An approved, published or
    /// superseded version is not changed: the same form creates a new draft from it, with every table carried over and the date given.
    /// The form is a view model: nothing of the cycle's state can be posted.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Edit(Guid id, PayrollFrameworkEditViewModel model)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.Editors)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        model.Id = id;
        model.IsImmutable = config.IsImmutable;
        if (model.EffectiveFrom is not { } date || date == default)
            ModelState.AddModelError(nameof(model.EffectiveFrom), PayrollFrameworkMessages.EffectiveFromRequired);
        if (ModelState.IsValid && !PackContract.TaxableBaseModes.Contains(model.TaxableBaseMode))
            ModelState.AddModelError(nameof(model.TaxableBaseMode), "أساس الضريبة: GrossPay أو NetOfStatutory");
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var actor = PayProfileChangeHttp.RequireUserId(User);
        var effectiveFrom = PayrollFrameworkQueries.Day(model.EffectiveFrom!.Value);

        if (config.IsImmutable)
        {
            var source = await repository.GetConfigurationByIdAsync(id, HttpContext.RequestAborted);
            if (source is null) return NotFound();
            var legacy = source.IsLegacyFramework
                ? await PayrollFrameworkQueries.LegacyReferenceDataAsync(repository, HttpContext.RequestAborted)
                : null;
            var draft = PayrollConfigurationVersioning.CloneAsDraft(
                source,
                await PayrollFrameworkQueries.NextVersionNumberAsync(repository, source.Code, HttpContext.RequestAborted),
                effectiveFrom,
                model.ApplyTo,
                legacy);
            draft.RecordEditor(actor, PayrollFrameworkEditKind.Created, calendar.UtcNow);
            db.PayrollConfigurations.Add(draft);
            await db.SaveChangesAsync();
            TempData["Success"] = $"تم إنشاء إصدار مسودة جديد (v{draft.VersionNumber}) — لا تُعدَّل النسخ المنشورة.";
            return RedirectToAction(nameof(Details), new { id = draft.Id });
        }

        model.ApplyTo(config);
        config.EffectiveFrom = effectiveFrom;
        config.ContentHash = null;
        config.UpdatedAt = calendar.UtcNow;
        var editorsBefore = config.Editors.Count;
        config.RecordEditor(actor, PayrollFrameworkEditKind.Edited, calendar.UtcNow);
        // A row added to a tracked version's collection is a new row, whatever key it was given.
        db.PayrollConfigurationEditors.AddRange(config.Editors.Skip(editorsBefore));
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث مسودة الإعدادات القانونية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        var actor = ParseUserId(User) ?? Guid.Empty;
        var (ok, error) = PayrollConfigurationVersioning.TryApprove(config, actor);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد النسخة القانونية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> LegalAttest(Guid id)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        if (config.PublicationStatus is not (PayrollConfigPublicationStatus.Draft
            or PayrollConfigPublicationStatus.Approved))
        {
            TempData["Error"] = "التصديق القانوني للمسودات/المعتمدة فقط قبل النشر.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var actor = ParseUserId(User) ?? Guid.Empty;
        config.ContentHash ??= PayrollConfigurationHashService.ComputeContentHash(config);

        var prior = await db.LegalAttestations
            .Where(a => a.PayrollConfigurationId == id && a.Status == "Active")
            .ToListAsync();
        foreach (var p in prior)
            p.Status = "Voided";

        db.LegalAttestations.Add(PayrollLegalGovernance.CreateAttestation(config, actor));
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل التصديق القانوني الإلكتروني للنسخة.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Publish(Guid id, string? impactSummary)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        var sameCode = await db.PayrollConfigurations
            .Where(c => c.Code == config.Code)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        var actor = ParseUserId(User) ?? Guid.Empty;

        var attestation = await db.LegalAttestations
            .Where(a => a.PayrollConfigurationId == id && a.Status == "Active")
            .OrderByDescending(a => a.AttestedAt)
            .FirstOrDefaultAsync();

        var latestPosted = await PayrollFrameworkQueries.LatestPostedPeriodEndAsync(repository, config.RegimeCode, HttpContext.RequestAborted);
        var (ok, error, notice) = PayrollLegalGovernance.TryPublishWithGovernance(
            config, sameCode, actor, attestation, impactSummary, latestPosted);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (notice is not null)
            db.PayrollConfigChangeNotices.Add(notice);

        await db.SaveChangesAsync();
        TempData["Success"] = $"تم نشر الإطار القانوني: {config.NameAr} (v{config.VersionNumber}) مع إشعار تغيير.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Activate(Guid id)
    {
        // PLAN-54 (§3, rule 3): Activate works on a published version only; it never publishes.
        var target = await db.PayrollConfigurations.FirstOrDefaultAsync(c => c.Id == id);
        if (target is null) return NotFound();

        var all = await db.PayrollConfigurations.Where(c => c.Code == target.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        var (ok, error) = PayrollConfigurationVersioning.TryActivate(target, all);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = $"تم تفعيل الإطار القانوني: {target.NameAr}";
        return RedirectToAction(nameof(Index));
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
