using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class BenefitPlansController(
    HRCoreDbContext context,
    ILegislationRegisterProvider legislationProvider) : Controller
{
    public async Task<IActionResult> Index()
    {
        var plans = await context.BenefitPlans
            .AsNoTracking()
            .OrderBy(p => p.PlanCode)
            .ToListAsync();

        var legalLabels = new Dictionary<string, string>();
        foreach (var legalSourceId in plans.Select(p => p.LegalSourceId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            var source = await legislationProvider.GetByIdAsync(legalSourceId!);
            legalLabels[legalSourceId!] = source?.DisplayLabel ?? legalSourceId!;
        }

        ViewBag.LegalSourceLabels = legalLabels;
        return View(plans);
    }
}
