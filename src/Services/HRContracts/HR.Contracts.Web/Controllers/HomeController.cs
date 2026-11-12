using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using HR.Contracts.Web.Models;

namespace HR.Contracts.Web.Controllers;

// PLAN-51 (review L7): the module's read policy, named, as on every HR.Contracts controller.
[Microsoft.AspNetCore.Authorization.Authorize(Policy = HRManagement.ServiceDefaults.HrManagementPolicies.ModuleHrContracts)]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
