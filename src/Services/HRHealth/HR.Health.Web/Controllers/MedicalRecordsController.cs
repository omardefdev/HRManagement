using HR.Health.Domain.Entities;
using HR.Health.Domain.Services;
using HR.Health.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Health.Web.Controllers;

public class MedicalRecordsController(HealthDbContext db) : Controller
{
    private async Task<IActionResult?> ForbidIfRecordEmployeeOutOfScopeAsync(Guid employeeId) =>
        await this.ForbidIfEmployeeOutOfScopeAsync(
            User,
            employeeId,
            db.EmployeeOrgUnitSnapshots,
            db.OrgUnitSnapshots);

    private async Task<IActionResult?> ForbidIfRecordOutOfScopeAsync(Guid recordId)
    {
        var employeeId = await db.MedicalRecords.AsNoTracking()
            .Where(r => r.Id == recordId)
            .Select(r => (Guid?)r.EmployeeId)
            .FirstOrDefaultAsync();
        if (employeeId is null)
            return NotFound();
        return await ForbidIfRecordEmployeeOutOfScopeAsync(employeeId.Value);
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Index()
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (this.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;

        var query = db.MedicalRecords.AsNoTracking().Where(x => !x.IsCancelled);
        if (scope.MustFilter)
        {
            var allowedEmployees = db.EmployeeOrgUnitSnapshots.AsNoTracking()
                .Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value))
                .Select(e => e.EmployeeId);
            query = query.Where(r => allowedEmployees.Contains(r.EmployeeId));
        }

        return View(await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
    }

    /// <summary>
    /// FUN-004: non-clinical summary — no diagnosis fields.
    /// <para>
    /// Still gated: the response confirms whether a named employee has a medical record and when
    /// they were last examined, which is health information about an identified person even
    /// without a diagnosis. Leaving it open let any module user enumerate that by employee id.
    /// </para>
    /// </summary>
    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Summary(Guid employeeId)
    {
        if (await ForbidIfRecordEmployeeOutOfScopeAsync(employeeId) is { } scopeDenied)
            return scopeDenied;

        var record = await db.MedicalRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && !r.IsCancelled);
        if (record is null) return NotFound();
        return Json(new
        {
            record.Id,
            record.EmployeeId,
            HasRecord = true,
            HasActiveConsent = HealthPrivacyRules.HasActiveConsent(record),
            record.LastExamDate
        });
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public IActionResult Create() => View(new MedicalRecord());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Create(MedicalRecord model)
    {
        if (!ModelState.IsValid) return View(model);
        db.MedicalRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء الملف الطبي";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Details(Guid id)
    {
        if (await ForbidIfRecordOutOfScopeAsync(id) is { } scopeDenied)
            return scopeDenied;

        var record = await db.MedicalRecords
            .Include(r => r.CommitteeReports)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (record is null) return NotFound();
        SensitiveDataAudit.Log(HttpContext, "HR.Health", "MedicalRecordView", $"MedicalRecordId={id}");
        return View(record);
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var record = await db.MedicalRecords.FindAsync(id);
        if (record is null) return NotFound();
        if (record.IsCancelled)
        {
            TempData["Error"] = "السجل ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(record);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Edit(Guid id, MedicalRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var record = await db.MedicalRecords.FindAsync(id);
        if (record is null) return NotFound();
        if (record.IsCancelled)
        {
            TempData["Error"] = "السجل ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }

        record.EmployeeId = model.EmployeeId;
        record.MedicalHistory = model.MedicalHistory;
        record.CurrentConditions = model.CurrentConditions;
        record.Allergies = model.Allergies;
        record.Disabilities = model.Disabilities;
        record.LastExamDate = model.LastExamDate;
        record.ConsentGivenAt = model.ConsentGivenAt;
        record.ConsentPurpose = model.ConsentPurpose;
        record.ConsentExpiresAt = model.ConsentExpiresAt;
        record.ConsentWithdrawnAt = model.ConsentWithdrawnAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الملف الطبي";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> RecordConsent(Guid id, string purpose, int validDays = 365)
    {
        var record = await db.MedicalRecords.FindAsync(id);
        if (record is null) return NotFound();
        if (record.IsCancelled)
        {
            TempData["Error"] = "السجل ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        record.ConsentGivenAt = DateTime.UtcNow;
        record.ConsentPurpose = string.IsNullOrWhiteSpace(purpose) ? "رعاية طبية وظيفية" : purpose.Trim();
        record.ConsentExpiresAt = DateTime.UtcNow.AddDays(Math.Clamp(validDays, 1, 3650));
        record.ConsentWithdrawnAt = null;
        await db.SaveChangesAsync();
        SensitiveDataAudit.Log(HttpContext, "HR.Health", "ConsentRecorded", $"MedicalRecordId={id}");
        TempData["Success"] = "تم تسجيل الموافقة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var record = await db.MedicalRecords.FindAsync(id);
        if (record is null) return NotFound();
        if (record.IsCancelled)
        {
            TempData["Error"] = "السجل ملغى مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }
        HealthPrivacyRules.SoftCancel(record, reason);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء الملف الطبي (soft-cancel) — لا حذف";
        return RedirectToAction(nameof(Index));
    }
}
