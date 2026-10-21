using Microsoft.AspNetCore.Mvc;

namespace HR.Contracts.Web.Controllers;

/// <summary>
/// Entry point for contract renewal workflows until a dedicated renewal queue exists.
/// </summary>
public class ContractRenewalsController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(ContractsController.Index), "Contracts");
}
