using Microsoft.AspNetCore.Mvc;

namespace HR.Contracts.Web.Controllers;

/// <summary>
/// Entry point for service-extension workflows (probation / contract term extensions).
/// </summary>
public class ServiceExtensionsController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(ProbationRecordsController.Index), "ProbationRecords");
}
