using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Web.Controllers;

public class DashboardController : Controller
{
    private readonly ContractsDbContext _db;
    public DashboardController(ContractsDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.ActiveContracts = await _db.Contracts.CountAsync(c => c.Status == ContractStatus.Active);
        ViewBag.PendingLeaves = await _db.LeaveRequests.CountAsync(l => l.Status == ApprovalStatus.Pending);
        ViewBag.PendingAdvances = await _db.AdvanceRequests.CountAsync(a => a.Status == ApprovalStatus.Pending);
        ViewBag.SeparationRecords = await _db.SeparationRecords.CountAsync();

        var leaveByType = await _db.LeaveRequests
            .GroupBy(l => l.LeaveType)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        ViewBag.LeaveLabels = System.Text.Json.JsonSerializer.Serialize(leaveByType.Select(l => l.Type).ToArray());
        ViewBag.LeaveData = System.Text.Json.JsonSerializer.Serialize(leaveByType.Select(l => l.Count).ToArray());

        return View();
    }
}
