using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Contracts.Web.Controllers;

/// <summary>
/// Entry point for service-extension workflows (probation / contract term extensions).
/// </summary>
// PLAN-51 (§4, P-6): reads name the module policy (the management approver reads HR.Contracts); every write names HrContracts.Write or HrContracts.Approve.
[Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
public class ServiceExtensionsController : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(ProbationRecordsController.Index), "ProbationRecords");
}
