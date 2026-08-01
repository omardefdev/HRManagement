using HR.Health.Domain.Entities;
using HR.Health.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Health.Web.Controllers;

public class WorkplaceSafetyController(HealthDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.WorkplaceAccidents.AsNoTracking().OrderByDescending(x => x.AccidentDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() =>
        View(new WorkplaceAccident { AccidentDate = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkplaceAccident model)
    {
        if (!ModelState.IsValid) return View(model);
        db.WorkplaceAccidents.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل حادث العمل";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var accident = await db.WorkplaceAccidents.FirstOrDefaultAsync(x => x.Id == id);
        if (accident is null) return NotFound();
        return View(accident);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var accident = await db.WorkplaceAccidents.FindAsync(id);
        if (accident is null) return NotFound();
        return View(accident);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, WorkplaceAccident model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var accident = await db.WorkplaceAccidents.FindAsync(id);
        if (accident is null) return NotFound();

        accident.EmployeeId = model.EmployeeId;
        accident.EmployeeName = model.EmployeeName;
        accident.AccidentDate = model.AccidentDate;
        accident.Location = model.Location;
        accident.Description = model.Description;
        accident.Severity = model.Severity;
        accident.RelatedSickLeaveDays = model.RelatedSickLeaveDays;
        accident.InvestigationNotes = model.InvestigationNotes;
        accident.MedicalRecordId = model.MedicalRecordId;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الحادث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var accident = await db.WorkplaceAccidents.FindAsync(id);
        if (accident is null) return NotFound();
        db.WorkplaceAccidents.Remove(accident);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الحادث";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Inspections() =>
        View(await db.SafetyInspections.AsNoTracking().OrderByDescending(x => x.InspectionDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInspection(SafetyInspection model)
    {
        if (ModelState.IsValid)
        {
            db.SafetyInspections.Add(model);
            await db.SaveChangesAsync();
            TempData["Success"] = "تم تسجيل التفتيش";
        }
        else
        {
            TempData["Error"] = "تعذّر حفظ التفتيش — تحقق من الحقول";
        }
        return RedirectToAction(nameof(Inspections));
    }

    public async Task<IActionResult> FirstAid() =>
        View(await db.FirstAidTrainingRecords.AsNoTracking().OrderByDescending(x => x.TrainingDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFirstAid(FirstAidTrainingRecord model)
    {
        if (ModelState.IsValid)
        {
            db.FirstAidTrainingRecords.Add(model);
            await db.SaveChangesAsync();
            TempData["Success"] = "تم تسجيل تدريب الإسعافات";
        }
        else
        {
            TempData["Error"] = "تعذّر حفظ السجل — تحقق من الحقول";
        }
        return RedirectToAction(nameof(FirstAid));
    }
}
