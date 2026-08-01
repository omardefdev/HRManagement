using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class EvaluationsController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly IEmployeeDetailsViewBuilder _detailsBuilder;
    private readonly OrgScopeService _orgScope;

    public EvaluationsController(
        HRCoreDbContext context,
        IEmployeeDetailsViewBuilder detailsBuilder,
        OrgScopeService orgScope)
    {
        _context = context;
        _detailsBuilder = detailsBuilder;
        _orgScope = orgScope;
    }

    public async Task<IActionResult> Index(Guid? employeeId, SearchFilterBase? filter, bool showCreate = false)
    {
        filter ??= new SearchFilterBase();
        filter.NormalizePaging();

        if (employeeId is { } eid)
        {
            var employeeScope = await _context.Employees.AsNoTracking()
                .Where(e => e.Id == eid)
                .Select(e => new { e.PrimaryOrgUnitId })
                .FirstOrDefaultAsync();
            if (employeeScope is null)
                return NotFound();

            var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
            if (this.ForbidIfOrgUnitOutOfScope(scope, employeeScope.PrimaryOrgUnitId) is { } denied)
                return denied;
        }

        var query = _context.PerformanceEvaluations
            .Include(e => e.Employee)
            .AsNoTracking()
            .AsQueryable();

        if (employeeId.HasValue)
            query = query.Where(e => e.EmployeeId == employeeId.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(e =>
                e.Employee!.FullName.Contains(term) ||
                (e.Summary != null && e.Summary.Contains(term)));
        }

        query = query.OrderByDescending(e => e.Year);

        var count = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        Employee? employee = null;
        if (employeeId.HasValue)
            employee = await _context.Employees.FindAsync(employeeId.Value);

        var vm = new EvaluationListViewModel
        {
            Evaluations = new PaginatedList<PerformanceEvaluation>(items, count, filter.Page, filter.PageSize),
            Filter = filter,
            EmployeeId = employeeId,
            Employee = employee,
            ShowCreateModal = showCreate
        };

        if (employeeId.HasValue && employee != null)
        {
            vm.CreateModel = new EvaluationCreateViewModel
            {
                EmployeeId = employeeId.Value,
                EmployeeName = employee.FullName,
                ReturnTo = "index"
            };
        }

        ViewBag.Employee = employee;
        return View(vm);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public IActionResult Create(Guid employeeId, string? returnTo = null) =>
        returnTo == "details"
            ? RedirectToAction("Details", "Employees", new { id = employeeId, openModal = "evaluation" })
            : RedirectToAction(nameof(Index), new { employeeId, showCreate = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(EvaluationCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            if (vm.ReturnTo == "details")
            {
                var detailsVm = await _detailsBuilder.BuildAsync(vm.EmployeeId, modalState: new EmployeeDetailsModalState
                {
                    EvaluationCreate = vm,
                    ShowEvaluationCreateModal = true
                });
                if (detailsVm == null) return NotFound();
                return View("~/Views/Employees/Details.cshtml", detailsVm);
            }

            return View("Index", await BuildIndexViewModelAsync(vm.EmployeeId, vm, showCreateModal: true));
        }

        var evaluation = new PerformanceEvaluation
        {
            EmployeeId = vm.EmployeeId,
            Year = vm.Year,
            Score = vm.Score,
            Summary = vm.Summary,
            Status = "draft"
        };

        _context.PerformanceEvaluations.Add(evaluation);
        await _context.SaveChangesAsync();

        TempData["Success"] = "ØªÙ… Ø¥Ø¶Ø§ÙØ© Ø§Ù„ØªÙ‚ÙŠÙŠÙ… Ø¨Ù†Ø¬Ø§Ø­";
        return vm.ReturnTo == "details"
            ? RedirectToAction("Details", "Employees", new { id = vm.EmployeeId })
            : RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var evaluation = await _context.PerformanceEvaluations
            .Include(e => e.Employee)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evaluation == null) return NotFound();

        return View(new EvaluationEditViewModel
        {
            Id = evaluation.Id,
            EmployeeId = evaluation.EmployeeId,
            EmployeeName = evaluation.Employee?.FullName,
            Year = evaluation.Year,
            Score = evaluation.Score,
            Summary = evaluation.Summary,
            Status = evaluation.Status
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Edit(Guid id, EvaluationEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        if (!ModelState.IsValid) return View(vm);

        var evaluation = await _context.PerformanceEvaluations.FindAsync(id);
        if (evaluation == null) return NotFound();

        evaluation.Year = vm.Year;
        evaluation.Score = vm.Score;
        evaluation.Summary = vm.Summary;
        evaluation.Status = vm.Status;

        await _context.SaveChangesAsync();

        TempData["Success"] = "ØªÙ… ØªØ­Ø¯ÙŠØ« Ø§Ù„ØªÙ‚ÙŠÙŠÙ… Ø¨Ù†Ø¬Ø§Ø­";
        return RedirectToAction(nameof(Index), new { employeeId = vm.EmployeeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var evaluation = await _context.PerformanceEvaluations.FindAsync(id);
        if (evaluation == null) return NotFound();

        var employeeId = evaluation.EmployeeId;
        _context.PerformanceEvaluations.Remove(evaluation);
        await _context.SaveChangesAsync();

        TempData["Success"] = "ØªÙ… Ø­Ø°Ù Ø§Ù„ØªÙ‚ÙŠÙŠÙ… Ø¨Ù†Ø¬Ø§Ø­";
        return RedirectToAction(nameof(Index), new { employeeId });
    }

    private async Task<EvaluationListViewModel> BuildIndexViewModelAsync(
        Guid employeeId,
        EvaluationCreateViewModel createModel,
        bool showCreateModal = false)
    {
        var filter = new SearchFilterBase();
        var employee = await _context.Employees.FindAsync(employeeId)
            ?? throw new InvalidOperationException("Employee not found");

        var query = _context.PerformanceEvaluations
            .Include(e => e.Employee)
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.Year)
            .AsNoTracking();

        var items = await query.Take(SearchFilterBase.MaxPageSize).ToListAsync();

        createModel.EmployeeId = employeeId;
        createModel.EmployeeName = employee.FullName;
        createModel.ReturnTo = "index";

        return new EvaluationListViewModel
        {
            Evaluations = new PaginatedList<PerformanceEvaluation>(items, items.Count, filter.Page, filter.PageSize),
            Filter = filter,
            EmployeeId = employeeId,
            Employee = employee,
            CreateModel = createModel,
            ShowCreateModal = showCreateModal
        };
    }
}
