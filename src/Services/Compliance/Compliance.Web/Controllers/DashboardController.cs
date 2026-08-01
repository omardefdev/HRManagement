using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Compliance.Infrastructure.Data;
using Compliance.Domain.Enums;

namespace Compliance.Web.Controllers;

public class DashboardController : Controller
{
    private readonly ComplianceDbContext _db;
    public DashboardController(ComplianceDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.OpenComplaints = await _db.Complaints.CountAsync(c => c.Status != ComplaintStatus.Closed && c.Status != ComplaintStatus.Resolved);
        ViewBag.AuditFindings = await _db.AuditFindings.CountAsync();
        ViewBag.DisciplinaryActions = await _db.DisciplinaryActions.CountAsync();
        ViewBag.LegalCases = await _db.LegalCases.CountAsync();
        return View();
    }
}
