using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class StatusHistoryController : Controller
{
    private readonly HRCoreDbContext _context;
    private readonly OrgScopeService _orgScope;

    public StatusHistoryController(HRCoreDbContext context, OrgScopeService orgScope)
    {
        _context = context;
        _orgScope = orgScope;
    }

    public async Task<IActionResult> Index(Guid? employeeId, string? status, DateTime? from, DateTime? to, int page = 1, int pageSize = 30)
    {
        page = SearchFilterBase.ClampPage(page);
        pageSize = SearchFilterBase.ClampPageSize(pageSize);

        var scope = await _orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        if (employeeId is { } eid)
        {
            var employeeScope = await _context.Employees.AsNoTracking()
                .Where(e => e.Id == eid)
                .Select(e => new { e.PrimaryOrgUnitId })
                .FirstOrDefaultAsync();
            if (employeeScope is null)
                return NotFound();

            if (this.ForbidIfOrgUnitOutOfScope(scope, employeeScope.PrimaryOrgUnitId) is { } empDenied)
                return empDenied;
        }

        var query = _context.EmploymentStatusHistories
            .Include(h => h.Employee)
            .AsNoTracking()
            .AsQueryable();

        if (employeeId.HasValue)
            query = query.Where(h => h.EmployeeId == employeeId.Value);
        else if (scope.MustFilter)
            query = query.Where(h =>
                h.Employee != null
                && h.Employee.PrimaryOrgUnitId.HasValue
                && scope.AllowedOrgUnitIds.Contains(h.Employee.PrimaryOrgUnitId.Value));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(h => h.NewStatus == status);

        if (from.HasValue)
            query = query.Where(h => h.ChangedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(h => h.ChangedAt <= to.Value.AddDays(1));

        query = query.OrderByDescending(h => h.ChangedAt);

        var count = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.EmployeeId = employeeId;
        ViewBag.StatusFilter = status;
        ViewBag.FromDate = from?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = to?.ToString("yyyy-MM-dd");
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

        if (employeeId.HasValue)
        {
            var emp = await _context.Employees.FindAsync(employeeId.Value);
            ViewBag.Employee = emp;
        }

        ViewBag.Employees = new SelectList(
            await _context.Employees.OrderBy(e => e.FullName).Take(SearchFilterBase.LookupMaxPageSize).Select(e => new { e.Id, e.FullName }).ToListAsync(),
            "Id", "FullName", employeeId);

        return View(items);
    }

    public async Task<IActionResult> FullLog(string? search, string? status, DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
    {
        page = SearchFilterBase.ClampPage(page);
        pageSize = SearchFilterBase.ClampPageSize(pageSize);

        var query = _context.EmploymentStatusHistories
            .Include(h => h.Employee)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(h =>
                (h.Employee != null && h.Employee.FullName.Contains(term)) ||
                (h.Reason != null && h.Reason.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(h => h.NewStatus == status);

        if (from.HasValue)
            query = query.Where(h => h.ChangedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(h => h.ChangedAt <= to.Value.AddDays(1));

        query = query.OrderByDescending(h => h.ChangedAt);

        var count = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.SearchTerm = search;
        ViewBag.StatusFilter = status;
        ViewBag.FromDate = from?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = to?.ToString("yyyy-MM-dd");
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(count / (double)pageSize);

        return View(items);
    }
}
