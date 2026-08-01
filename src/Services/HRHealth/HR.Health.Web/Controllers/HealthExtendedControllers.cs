using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;
using HR.Health.Domain.Services;
using HR.Health.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Health.Web.Controllers;

public class MedicalCommitteeReportsController : Controller
{
    private readonly HealthDbContext _db;
    private readonly IPublishEndpoint _publish;

    public MedicalCommitteeReportsController(HealthDbContext db, IPublishEndpoint publish)
    {
        _db = db;
        _publish = publish;
    }

    public async Task<IActionResult> Index() =>
        View(await _db.MedicalCommitteeReports.AsNoTracking().OrderByDescending(x => x.CommitteeDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        ViewBag.MedicalRecords = new SelectList(
            await _db.MedicalRecords.AsNoTracking().OrderByDescending(m => m.UpdatedAt).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "EmployeeId");
        return View(new MedicalCommitteeReport { CommitteeDate = DateOnly.FromDateTime(DateTime.UtcNow) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MedicalCommitteeReport model, bool emergencyOverride = false)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.MedicalRecords = new SelectList(await _db.MedicalRecords.AsNoTracking().Where(r => !r.IsCancelled).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId");
            return View(model);
        }

        var medicalRecord = await _db.MedicalRecords.FindAsync(model.MedicalRecordId);
        if (medicalRecord is null || medicalRecord.IsCancelled)
        {
            TempData["Error"] = "الملف الطبي غير موجود أو ملغى";
            ViewBag.MedicalRecords = new SelectList(await _db.MedicalRecords.AsNoTracking().Where(r => !r.IsCancelled).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId");
            return View(model);
        }
        if (!HealthPrivacyRules.CanIssueClinicalAction(medicalRecord, emergencyOverride))
        {
            TempData["Error"] = "موافقة طبية سارية مطلوبة قبل قرار اللجنة";
            ViewBag.MedicalRecords = new SelectList(await _db.MedicalRecords.AsNoTracking().Where(r => !r.IsCancelled).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId");
            return View(model);
        }
        if (emergencyOverride && !HealthPrivacyRules.HasActiveConsent(medicalRecord))
            SensitiveDataAudit.Log(HttpContext, "HR.Health", "EmergencyOverride", $"MedicalRecordId={model.MedicalRecordId}");

        model.IssuedBy = ServerAssignedIdentity.ResolveActorName(User);
        _db.MedicalCommitteeReports.Add(model);

        var employeeId = medicalRecord.EmployeeId;

        if (MedicalDecisionWorkflow.RequiresRetirement(model.Recommendation))
        {
            var retirement = MedicalDecisionWorkflow.CreateRetirementRecord(
                employeeId, model.CommitteeDate, model.Findings);
            _db.HealthRetirementRecords.Add(retirement);
        }

        if (MedicalDecisionWorkflow.RequiresStatusUpdate(model.Recommendation))
        {
            var statusEntry = MedicalDecisionWorkflow.CreateStatusEntry(employeeId, model.Recommendation);
            _db.HealthStatusEntries.Add(statusEntry);
        }

        await _db.SaveChangesAsync();

        if (MedicalDecisionWorkflow.ShouldPublishDecision(model.Recommendation))
        {
            await _publish.Publish<IMedicalDecisionIssued>(new
            {
                EmployeeId = employeeId,
                EffectiveDate = model.CommitteeDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                MedicalRef = MedicalDecisionWorkflow.MapRecommendationToMedicalRef(model.Recommendation),
                Notes = model.Findings,
                SourceSystem = "HR.Health",
                CorrelationId = model.Id.ToString()
            });
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = "تم تسجيل تقرير اللجنة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var report = await _db.MedicalCommitteeReports
            .Include(r => r.MedicalRecord)
            .Include(r => r.Recommendations)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (report is null) return NotFound();
        return View(report);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var report = await _db.MedicalCommitteeReports.FindAsync(id);
        if (report is null) return NotFound();
        ViewBag.MedicalRecords = new SelectList(
            await _db.MedicalRecords.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId");
        return View(report);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, MedicalCommitteeReport model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.MedicalRecords = new SelectList(await _db.MedicalRecords.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId");
            return View(model);
        }
        var report = await _db.MedicalCommitteeReports.FindAsync(id);
        if (report is null) return NotFound();

        report.MedicalRecordId = model.MedicalRecordId;
        report.CommitteeDate = model.CommitteeDate;
        report.Findings = model.Findings;
        report.Recommendation = model.Recommendation;
        report.RelatedDocuments = model.RelatedDocuments;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث التقرير";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class InsuranceContractsController(HealthDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.InsuranceContracts.AsNoTracking().OrderByDescending(x => x.StartDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new InsuranceContract { StartDate = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(InsuranceContract model)
    {
        if (!ModelState.IsValid) return View(model);
        db.InsuranceContracts.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل عقد التأمين";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var contract = await db.InsuranceContracts.FirstOrDefaultAsync(x => x.Id == id);
        if (contract is null) return NotFound();
        return View(contract);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var contract = await db.InsuranceContracts.FindAsync(id);
        if (contract is null) return NotFound();
        return View(contract);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, InsuranceContract model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var contract = await db.InsuranceContracts.FindAsync(id);
        if (contract is null) return NotFound();

        contract.Provider = model.Provider;
        contract.CoverageDetails = model.CoverageDetails;
        contract.StartDate = model.StartDate;
        contract.EndDate = model.EndDate;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث عقد التأمين";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var contract = await db.InsuranceContracts.FindAsync(id);
        if (contract is null) return NotFound();
        db.InsuranceContracts.Remove(contract);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف عقد التأمين";
        return RedirectToAction(nameof(Index));
    }
}

public class HealthRetirementRecordsController(HealthDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.HealthRetirementRecords.AsNoTracking().OrderByDescending(x => x.EffectiveDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new HealthRetirementRecord { EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HealthRetirementRecord model)
    {
        if (!ModelState.IsValid) return View(model);
        db.HealthRetirementRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل ملف التقاعد الصحي";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var record = await db.HealthRetirementRecords.FirstOrDefaultAsync(x => x.Id == id);
        if (record is null) return NotFound();
        return View(record);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var record = await db.HealthRetirementRecords.FindAsync(id);
        if (record is null) return NotFound();
        return View(record);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, HealthRetirementRecord model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var record = await db.HealthRetirementRecords.FindAsync(id);
        if (record is null) return NotFound();

        record.EmployeeId = model.EmployeeId;
        record.EffectiveDate = model.EffectiveDate;
        record.Reason = model.Reason;
        record.Documents = model.Documents;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث ملف التقاعد";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class HealthStatusEntriesController(HealthDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.HealthStatusEntries.AsNoTracking().OrderByDescending(x => x.EntryDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new HealthStatusEntry { EntryDate = DateTime.UtcNow });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HealthStatusEntry model)
    {
        if (!ModelState.IsValid) return View(model);
        db.HealthStatusEntries.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الحالة الصحية";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var entry = await db.HealthStatusEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null) return NotFound();
        return View(entry);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var entry = await db.HealthStatusEntries.FindAsync(id);
        if (entry is null) return NotFound();
        return View(entry);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, HealthStatusEntry model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var entry = await db.HealthStatusEntries.FindAsync(id);
        if (entry is null) return NotFound();

        entry.EmployeeId = model.EmployeeId;
        entry.EntryDate = model.EntryDate;
        entry.StatusCode = model.StatusCode;
        entry.Notes = model.Notes;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الحالة الصحية";
        return RedirectToAction(nameof(Details), new { id });
    }
}
