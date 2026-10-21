using HR.Training.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Training.Web.Controllers;

public class DashboardController(TrainingDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.TrainingCourses = await db.TrainingCourses.CountAsync();
        ViewBag.TrainingEnrollments = await db.TrainingEnrollments.CountAsync();
        ViewBag.TrainingPlans = await db.TrainingPlans.CountAsync();
        ViewBag.ELearningCourses = await db.ELearningCourses.CountAsync();
        return View();
    }
}
