using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

/// <summary>
/// FUN-029 (F4): contract type per (service regime, employment type). Read: HrContractsHireView (the hire roles that
/// see the banner); write: HrContractsApprove (the contracts approver — the same policy the gateway applies to every
/// mutating hr-contracts request outside the hire wizard).
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrContractsHireView)]
public class ContractTypeRegimeMapsController(ContractsDbContext db) : Controller
{
    /// <summary>SYR-003 regime ids (docs/syr/service-regimes.json).</summary>
    public static IReadOnlyList<(string Id, string Label)> KnownRegimes { get; } =
    [
        ("law50", "القانون الأساسي للعاملين في الدولة 50/2004"),
        ("law53", "القانون 53/2021"),
        ("special_banking", "نظام خاص — المصارف"),
        ("special_judiciary", "نظام خاص — القضاء")
    ];

    public static IReadOnlyList<(string Id, string Label)> EmploymentTypes { get; } =
    [
        (HireEmploymentTypes.Permanent, "دائم"),
        (HireEmploymentTypes.Administrative, "إداري"),
        (HireEmploymentTypes.Temporary, "متعاقد مؤقت")
    ];

    public const string DuplicateMessage = "يوجد ربط مسبق لنفس نظام الخدمة ونوع التوظيف.";

    public async Task<IActionResult> Index()
    {
        var items = await db.ContractTypeRegimeMaps.AsNoTracking()
            .Include(m => m.ContractType)
            .OrderByDescending(m => m.IsActive)
            .ThenBy(m => m.LegalRegimeId)
            .ThenBy(m => m.EmploymentType)
            .ToListAsync();
        return View(items);
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Create() => View("Form", await WithListsAsync(new ContractTypeRegimeMapForm()));

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Create(ContractTypeRegimeMapForm form)
    {
        await ValidateAsync(form, null);
        if (!ModelState.IsValid)
            return View("Form", await WithListsAsync(form));

        db.ContractTypeRegimeMaps.Add(new ContractTypeRegimeMap
        {
            LegalRegimeId = Clean(form.LegalRegimeId),
            EmploymentType = Clean(form.EmploymentType),
            ContractTypeId = form.ContractTypeId!.Value,
            IsActive = true
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة الربط";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var map = await db.ContractTypeRegimeMaps.FindAsync(id);
        if (map is null) return NotFound();
        return View("Form", await WithListsAsync(new ContractTypeRegimeMapForm
        {
            Id = map.Id,
            LegalRegimeId = map.LegalRegimeId,
            EmploymentType = map.EmploymentType,
            ContractTypeId = map.ContractTypeId,
            IsActive = map.IsActive
        }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Edit(Guid id, ContractTypeRegimeMapForm form)
    {
        if (form.Id != id) return BadRequest();
        var map = await db.ContractTypeRegimeMaps.FindAsync(id);
        if (map is null) return NotFound();

        await ValidateAsync(form, id);
        if (!ModelState.IsValid)
            return View("Form", await WithListsAsync(form));

        map.LegalRegimeId = Clean(form.LegalRegimeId);
        map.EmploymentType = Clean(form.EmploymentType);
        map.ContractTypeId = form.ContractTypeId!.Value;
        map.IsActive = form.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الربط";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var map = await db.ContractTypeRegimeMaps.FindAsync(id);
        if (map is null) return NotFound();
        map.IsActive = false;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إيقاف الربط";
        return RedirectToAction(nameof(Index));
    }

    public static string RegimeLabel(string? id) =>
        id is null ? "أي نظام" : KnownRegimes.FirstOrDefault(r => r.Id == id).Label ?? id;

    public static string EmploymentTypeLabel(string? id) =>
        id is null ? "أي نوع" : EmploymentTypes.FirstOrDefault(t => t.Id == id).Label ?? id;

    private async Task ValidateAsync(ContractTypeRegimeMapForm form, Guid? id)
    {
        if (form.ContractTypeId is not Guid typeId || !await db.ContractTypes.AnyAsync(t => t.Id == typeId))
            ModelState.AddModelError(nameof(form.ContractTypeId), "اختر نوع عقد موجوداً.");

        var regime = Clean(form.LegalRegimeId);
        var type = Clean(form.EmploymentType);
        if (type is not null && !HireEmploymentTypes.All.Contains(type))
            ModelState.AddModelError(nameof(form.EmploymentType), "نوع التوظيف غير معروف.");

        // NULLS NOT DISTINCT enforces this in PostgreSQL; checked here for a readable message.
        if (await db.ContractTypeRegimeMaps.AnyAsync(m =>
                m.Id != id && m.LegalRegimeId == regime && m.EmploymentType == type))
            ModelState.AddModelError(string.Empty, DuplicateMessage);
    }

    private async Task<ContractTypeRegimeMapForm> WithListsAsync(ContractTypeRegimeMapForm form)
    {
        var types = await db.ContractTypes.AsNoTracking().OrderBy(t => t.Code).ToListAsync();
        form.ContractTypes = new SelectList(
            types.Select(t => new { t.Id, Label = $"{t.Code} — {t.NameAr}" }), "Id", "Label", form.ContractTypeId);
        return form;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "*" ? null : value.Trim();
}

public class ContractTypeRegimeMapForm
{
    public Guid? Id { get; set; }

    [Display(Name = "نظام الخدمة")]
    [StringLength(50)]
    public string? LegalRegimeId { get; set; }

    [Display(Name = "نوع التوظيف")]
    [StringLength(20)]
    public string? EmploymentType { get; set; }

    [Display(Name = "نوع العقد")]
    [Required(ErrorMessage = "نوع العقد مطلوب")]
    public Guid? ContractTypeId { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    public SelectList? ContractTypes { get; set; }
}
