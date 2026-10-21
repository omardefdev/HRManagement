using HR.Health.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Health.Web.Controllers;

public class DashboardController(HealthDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.MedicalRecords = await db.MedicalRecords.CountAsync();
        ViewBag.PeriodicMedicalChecks = await db.PeriodicMedicalChecks.CountAsync();
        ViewBag.InsuranceCards = await db.InsuranceCards.CountAsync();
        ViewBag.WorkplaceAccidents = await db.WorkplaceAccidents.CountAsync();
        return View();
    }
}
