using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HR.Health.Domain.Entities;
using HR.Health.Domain.Services;
using HR.Health.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Health.Web.Controllers;

public class MedicalRecommendationsController : Controller
{
    private readonly HealthDbContext _db;
    private readonly IPublishEndpoint _publish;

    public MedicalRecommendationsController(HealthDbContext db, IPublishEndpoint publish)
    {
        _db = db;
        _publish = publish;
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Index()
    {
        return View(await _db.MedicalRecommendations.AsNoTracking().OrderByDescending(x => x.IssuedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public IActionResult Create() => View(new MedicalRecommendation
    {
        IssuedAt = DateTime.UtcNow
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Create(MedicalRecommendation model, bool emergencyOverride = false)
    {
        if (!ModelState.IsValid) return View(model);

        var medical = await _db.MedicalRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == model.EmployeeId && !r.IsCancelled);
        if (medical is null)
        {
            TempData["Error"] = "لا يوجد ملف طبي نشط للموظف";
            return View(model);
        }
        if (!HealthPrivacyRules.CanIssueClinicalAction(medical, emergencyOverride))
        {
            TempData["Error"] = "موافقة طبية سارية مطلوبة قبل إصدار التوصية";
            return View(model);
        }
        if (emergencyOverride && !HealthPrivacyRules.HasActiveConsent(medical))
            SensitiveDataAudit.Log(HttpContext, "HR.Health", "EmergencyOverride", $"EmployeeId={model.EmployeeId}");

        model.IssuedBy = ServerAssignedIdentity.ResolveActorName(User);
        _db.MedicalRecommendations.Add(model);
        await _db.SaveChangesAsync();
        SensitiveDataAudit.Log(HttpContext, "HR.Health", "MedicalRecommendationCreated", $"EmployeeId={model.EmployeeId}");

        var issuedAt = model.IssuedAt ?? DateTime.UtcNow;
        await _publish.Publish<IMedicalRecommendationIssued>(new
        {
            EmployeeId = model.EmployeeId,
            RecommendationType = model.RecommendationType.ToString(),
            Details = string.Empty,
            IssuedAt = issuedAt,
            ReportId = model.ReportId,
            RecommendationEnum = model.RecommendationType.ToString(),
            RecommendationId = model.Id,
            SourceSystem = "HR.Health"
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم حفظ التوصية ونشر الحدث";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Details(Guid id)
    {
        var rec = await _db.MedicalRecommendations
            .Include(r => r.Report)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rec is null) return NotFound();
        SensitiveDataAudit.Log(HttpContext, "HR.Health", "MedicalRecommendationView", $"RecommendationId={id}");
        return View(rec);
    }

    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var rec = await _db.MedicalRecommendations.FindAsync(id);
        if (rec is null) return NotFound();
        return View(rec);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrHealthClinical)]
    public async Task<IActionResult> Edit(Guid id, MedicalRecommendation model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var rec = await _db.MedicalRecommendations.FindAsync(id);
        if (rec is null) return NotFound();

        rec.EmployeeId = model.EmployeeId;
        rec.RecommendationType = model.RecommendationType;
        rec.Details = model.Details;
        rec.IssuedAt = model.IssuedAt;
        // IssuedBy is server-assigned at creation and must not be spoofed on edit.
        rec.ReportId = model.ReportId;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث التوصية";
        return RedirectToAction(nameof(Details), new { id });
    }
}
