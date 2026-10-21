using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

public class SuccessionPlansController(HRCoreDbContext db) : Controller
{
    private async Task PopulateLookups()
    {
        ViewBag.KeyPositions = new SelectList(
            await db.KeyPositions.AsNoTracking().OrderBy(k => k.Title).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title");
        ViewBag.Employees = new SelectList(
            await db.Employees.AsNoTracking().OrderBy(e => e.FullName).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "FullName");
    }

    public async Task<IActionResult> Index(Guid? keyPositionId, int? year)
    {
        var query = db.SuccessionPlans.AsNoTracking()
            .Include(p => p.KeyPosition)
            .Include(p => p.Candidates)
            .AsQueryable();
        if (keyPositionId is Guid kpid)
            query = query.Where(p => p.KeyPositionId == kpid);
        if (year is int y)
            query = query.Where(p => p.Year == y);

        var items = await query.OrderByDescending(p => p.Year).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.KeyPositions = new SelectList(
            await db.KeyPositions.AsNoTracking().OrderBy(k => k.Title).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Title", keyPositionId);
        return View(new SuccessionPlansIndexViewModel { Items = items, KeyPositionId = keyPositionId, Year = year });
    }

    public async Task<IActionResult> Create()
    {
        await PopulateLookups();
        return View(new SuccessionPlanCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SuccessionPlanCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLookups();
            return View(vm);
        }
        var model = new SuccessionPlan
        {
            KeyPositionId = vm.KeyPositionId,
            Year = vm.Year,
            Notes = vm.Notes
        };
        db.SuccessionPlans.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء خطة التعاقب";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var plan = await db.SuccessionPlans
            .Include(p => p.KeyPosition)
            .Include(p => p.Candidates).ThenInclude(c => c.Employee)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
        if (plan == null) return NotFound();
        await PopulateLookups();
        return View(plan);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCandidate(Guid successionPlanId, Guid employeeId, decimal? latestEvaluationScore, int yearsInService, bool meetsQualification)
    {
        var plan = await db.SuccessionPlans.FindAsync(successionPlanId);
        if (plan == null) return NotFound();

        var score = latestEvaluationScore ?? await db.PerformanceEvaluations
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.Year)
            .Select(e => (decimal?)e.Score)
            .FirstOrDefaultAsync() ?? 0m;

        var readiness = SuccessionReadinessRules.Classify(score, yearsInService, meetsQualification);

        db.SuccessorCandidates.Add(new SuccessorCandidate
        {
            SuccessionPlanId = successionPlanId,
            EmployeeId = employeeId,
            LatestEvaluationScore = score,
            YearsInService = yearsInService < 0 ? 0 : yearsInService,
            MeetsQualification = meetsQualification,
            Readiness = readiness
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة المرشّح وتصنيف جاهزيته";
        return RedirectToAction(nameof(Details), new { id = successionPlanId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCandidate(Guid id, Guid successionPlanId)
    {
        var candidate = await db.SuccessorCandidates.FindAsync(id);
        if (candidate != null)
        {
            db.SuccessorCandidates.Remove(candidate);
            await db.SaveChangesAsync();
            TempData["Success"] = "تم حذف المرشّح";
        }
        return RedirectToAction(nameof(Details), new { id = successionPlanId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var plan = await db.SuccessionPlans.FindAsync(id);
        if (plan == null) return NotFound();
        db.SuccessionPlans.Remove(plan);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الخطة";
        return RedirectToAction(nameof(Index));
    }
}
