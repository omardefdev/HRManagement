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
    public DashboardController(PayrollDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.TotalRuns = await _db.PayrollRuns.CountAsync();
        ViewBag.TotalProfiles = await _db.EmployeePayProfiles.CountAsync(p => p.IsActive);
        ViewBag.TotalPosted = await _db.PayrollRuns.CountAsync(r => r.Status == PayrollRunStatus.Posted);

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
