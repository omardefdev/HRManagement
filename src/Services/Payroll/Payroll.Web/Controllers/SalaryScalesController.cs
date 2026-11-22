using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// Salary scales are read here. PLAN-53 (§4, D-53-4): Create, Edit and Delete submit a request (<c>Payroll.MasterData</c>, with a
/// reason and a legal reference) that a different payroll approver decides on «طلبات البيانات المرجعية»; nothing is written in place.
/// </summary>
public class SalaryScalesController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public SalaryScalesController(PayrollDbContext db, IMediator mediator, IConfiguration configuration)
    {
        _db = db;
        _mediator = mediator;
        _configuration = configuration;
    }

    public async Task<IActionResult> Index(string? q, bool? isActive, DateTime? effectiveFrom, DateTime? effectiveTo)
    {
        var query = _db.SalaryScales.AsNoTracking().Include(s => s.PayGrades).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(s => s.Code.Contains(term) || s.NameAr.Contains(term));
        }
        if (isActive.HasValue) query = query.Where(s => s.IsActive == isActive.Value);
        if (effectiveFrom.HasValue) query = query.Where(s => s.EffectiveFrom >= effectiveFrom.Value);
        if (effectiveTo.HasValue) query = query.Where(s => s.EffectiveFrom <= effectiveTo.Value);
        var items = await query.OrderBy(s => s.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.Q = q;
        ViewBag.IsActive = isActive;
        ViewBag.EffectiveFrom = effectiveFrom;
        ViewBag.EffectiveTo = effectiveTo;
        ViewBag.CanChange = PayrollPolicyRules.CanChangeMasterData(User, _configuration);
        ViewBag.OpenRequests = await PayrollReferenceChangeHttp.OpenRequestsAsync(_db, PayrollReferenceEntityType.SalaryScale, HttpContext.RequestAborted);
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var scale = await _db.SalaryScales.AsNoTracking().Include(s => s.PayGrades).FirstOrDefaultAsync(s => s.Id == id);
        if (scale is null) return NotFound();
        return View(new SalaryScaleDetailsViewModel
        {
            Scale = scale,
            OpenRequest = await _db.PayrollReferenceChangeRequests.AsNoTracking().FirstOrDefaultAsync(
                r => r.EntityType == PayrollReferenceEntityType.SalaryScale && r.EntityId == id && r.State == PayrollReferenceChangeState.PendingApproval),
            CanChange = PayrollPolicyRules.CanChangeMasterData(User, _configuration),
        });
    }

    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public IActionResult Create() => View(new SalaryScaleChangeViewModel { EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Create(SalaryScaleChangeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var result = await this.SubmitAsync(_mediator, PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Create, null, null, model.ToChange(), model);
        return this.Submitted(result) ?? View(model);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await _db.SalaryScales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (e is null) return NotFound();
        return View(SalaryScaleChangeViewModel.From(e));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Edit(Guid id, SalaryScaleChangeViewModel model)
    {
        model.Id = id;
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var result = await this.SubmitAsync(_mediator, PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Update, id, null, model.ToChange(), model);
        return this.Submitted(result) ?? View(model);
    }

    /// <summary>The page of the delete request: the reason and the legal reference a deletion needs.</summary>
    [HttpGet]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await _db.SalaryScales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (e is null) return NotFound();
        return View(new PayrollReferenceDeleteViewModel { Id = id, Label = $"{e.Code} — {e.NameAr}" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Delete(Guid id, PayrollReferenceDeleteViewModel model)
    {
        var e = await _db.SalaryScales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (e is null) return NotFound();
        model.Id = id;
        model.Label = $"{e.Code} — {e.NameAr}";
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(model);
        }

        var result = await this.SubmitAsync(_mediator, PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Delete, id, null, null, model);
        return this.Submitted(result) ?? View(model);
    }
}
