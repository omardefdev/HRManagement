using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// Grades are read here. PLAN-53 (§4, D-53-4): Create, Edit and Delete submit a request (<c>Payroll.MasterData</c>, with a reason and
/// a legal reference) that a different payroll approver decides; a grade a pay profile names is not deleted; new bounds change no pay
/// profile (the profiles they leave outside are counted here and listed on the pay profiles screen).
/// </summary>
public class PayGradesController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public PayGradesController(PayrollDbContext db, IMediator mediator, IConfiguration configuration)
    {
        _db = db;
        _mediator = mediator;
        _configuration = configuration;
    }

    private async Task PopulateScalesAsync()
    {
        var scales = await _db.SalaryScales.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        ViewBag.Scales = new SelectList(scales, "Id", "NameAr");
    }

    public async Task<IActionResult> Index(Guid? scaleId, string? q)
    {
        var query = _db.PayGrades.Include(g => g.SalaryScale).AsNoTracking().AsQueryable();
        if (scaleId.HasValue) query = query.Where(g => g.SalaryScaleId == scaleId.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(g => g.GradeCode.Contains(term) || g.GradeNameAr.Contains(term));
        }
        var items = await query.OrderBy(g => g.SalaryScale.Code).ThenBy(g => g.GradeCode).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.ScaleId = scaleId;
        ViewBag.Q = q;
        ViewBag.Scales = new SelectList(
            await _db.SalaryScales.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "NameAr", scaleId);
        ViewBag.CanChange = PayrollPolicyRules.CanChangeMasterData(User, _configuration);
        ViewBag.OpenRequests = await PayrollReferenceChangeHttp.OpenRequestsAsync(_db, PayrollReferenceEntityType.PayGrade, HttpContext.RequestAborted);
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var grade = await _db.PayGrades.AsNoTracking().Include(g => g.SalaryScale).FirstOrDefaultAsync(g => g.Id == id);
        if (grade is null) return NotFound();
        return View(new PayGradeDetailsViewModel
        {
            Grade = grade,
            OpenRequest = await _db.PayrollReferenceChangeRequests.AsNoTracking().FirstOrDefaultAsync(
                r => r.EntityType == PayrollReferenceEntityType.PayGrade && r.EntityId == id && r.State == PayrollReferenceChangeState.PendingApproval),
            ProfilesNamingIt = await _db.EmployeePayProfiles.AsNoTracking().CountAsync(p => p.PayGradeId == id && p.IsActive),
            ProfilesOutsideBounds = await _db.EmployeePayProfiles.AsNoTracking().Where(p => p.PayGradeId == id)
                .CountAsync(EmployeePayProfileRules.OutsideGradeBounds),
            CanChange = PayrollPolicyRules.CanChangeMasterData(User, _configuration),
        });
    }

    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Create(Guid? scaleId)
    {
        await PopulateScalesAsync();
        return View(new PayGradeChangeViewModel { SalaryScaleId = scaleId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Create(PayGradeChangeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateScalesAsync();
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var result = await this.SubmitAsync(_mediator, PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Create, null, model.ToChange(), null, model);
        if (this.Submitted(result) is { } done) return done;
        await PopulateScalesAsync();
        return View(model);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.PayGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (e is null) return NotFound();
        await PopulateScalesAsync();
        return View(PayGradeChangeViewModel.From(e));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Edit(Guid id, PayGradeChangeViewModel model)
    {
        model.Id = id;
        if (!ModelState.IsValid)
        {
            await PopulateScalesAsync();
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var result = await this.SubmitAsync(_mediator, PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, id, model.ToChange(), null, model);
        if (this.Submitted(result) is { } done) return done;
        await PopulateScalesAsync();
        return View(model);
    }

    /// <summary>The page of the delete request: the reason and the legal reference a deletion needs.</summary>
    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.PayGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (e is null) return NotFound();
        return View(new PayrollReferenceDeleteViewModel { Id = id, Label = $"{e.GradeCode} — {e.GradeNameAr}" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Delete(Guid id, PayrollReferenceDeleteViewModel model)
    {
        var e = await _db.PayGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (e is null) return NotFound();
        model.Id = id;
        model.Label = $"{e.GradeCode} — {e.GradeNameAr}";
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var result = await this.SubmitAsync(_mediator, PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Delete, id, null, null, model);
        return this.Submitted(result) ?? View(model);
    }
}
