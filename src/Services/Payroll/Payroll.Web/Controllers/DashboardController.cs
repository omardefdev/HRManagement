using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

public class DashboardController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly Payroll.Application.Abstractions.IPayrollRepository _repository;

    public DashboardController(PayrollDbContext db, Payroll.Application.Abstractions.IPayrollRepository repository)
    {
        _db = db;
        _repository = repository;
    }

    public async Task<IActionResult> Index()
    {
        // PLAN-54 (§1): an agency with no published framework cannot calculate; the dashboard says so and where to start.
        ViewBag.NoPublishedFramework = await _repository.GetActiveConfigurationAsync(HttpContext.RequestAborted) is null;
        // PLAN-54 (§3, P-10): from 60 days before an exception a published framework cites ends, the dashboard names it and its date.
        ViewBag.ExceptionWarnings = await Payroll.Application.Features.PayrollConfigurations.PayrollFrameworkIndicators.ExpiringAsync(
            _repository,
            HttpContext.RequestServices.GetRequiredService<Payroll.Application.Abstractions.IPayrollLegalControlGuard>(),
            HttpContext.RequestServices.GetRequiredService<Payroll.Application.Services.IPayrollCalendar>(),
            HttpContext.RequestAborted);

        ViewBag.TotalRuns = await _db.PayrollRuns.CountAsync();
        ViewBag.TotalProfiles = await _db.EmployeePayProfiles.CountAsync(p => p.IsActive);
        ViewBag.TotalPosted = await _db.PayrollRuns.CountAsync(r => r.Status == PayrollRunStatus.Posted);

        // PLAN-53: the salary changes that wait for an approver, and the scheduled ones a cycle could not apply; the caller's branch only.
        var changes = await _db.PayProfileChangeRequests.AsNoTracking().InOrgScopeAsync(HttpContext, _db);
        ViewBag.PendingSalaryChanges = await changes.CountAsync(r => r.State == PayProfileChangeState.PendingApproval);
        ViewBag.BlockedSalaryChanges = await changes.CountAsync(r => r.State == PayProfileChangeState.Scheduled && r.LastApplyError != null);

        var lastRun = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .OrderByDescending(r => r.RunDate)
            .FirstOrDefaultAsync();
        ViewBag.LastRunNet = lastRun?.SalaryLines.Sum(l => l.NetPay) ?? 0;

        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var monthlyData = await _db.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Posted && r.PeriodStart >= sixMonthsAgo)
            .Include(r => r.SalaryLines)
            .OrderBy(r => r.PeriodStart)
            .Take(SearchFilterBase.MaxPageSize)
            .Select(r => new { r.PeriodStart, Total = r.SalaryLines.Sum(l => l.NetPay) })
            .ToListAsync();

        ViewBag.ChartLabels = System.Text.Json.JsonSerializer.Serialize(
            monthlyData.Select(d => d.PeriodStart.ToString("yyyy/MM")).ToArray());
        ViewBag.ChartData = System.Text.Json.JsonSerializer.Serialize(
            monthlyData.Select(d => d.Total).ToArray());

        return View();
    }
}
