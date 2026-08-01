using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Movements.Infrastructure.Data;

namespace HR.Movements.Web.Controllers;

public class DashboardController : Controller
{
    private readonly MovementsDbContext _db;
    public DashboardController(MovementsDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.TransferRequests = await _db.TransferRequests.CountAsync();
        ViewBag.SecondmentRequests = await _db.SecondmentRequests.CountAsync();
        ViewBag.MovementNotices = await _db.MovementNotices.CountAsync();
        ViewBag.DistributionPlans = await _db.DistributionPlans.CountAsync();
        return View();
    }
}
