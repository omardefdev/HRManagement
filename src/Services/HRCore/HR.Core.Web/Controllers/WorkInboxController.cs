using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Controllers;

/// <summary>HCM-028 (F4): HrCoreWorkInbox — the HR.Core read roles plus the compliance reviewer (appeals only).</summary>
[Authorize(Policy = HrManagementPolicies.HrCoreWorkInbox)]
public class WorkInboxController(WorkflowInboxQueryService queryService) : Controller
{
    public async Task<IActionResult> Index(string? entityType, string? source, CancellationToken ct)
    {
        var userId = WorkflowPanelBuilder.ParseUserId(User);
        if (userId is null)
            return Challenge();

        var items = await queryService.GetActionableItemsAsync(User, ct);
        var workflowCount = items.Count(i => i.SourceSystem == WorkInboxItemMapper.SourceSystemHrCore);
        var legacyCount = items.Count(i => i.SourceSystem == WorkInboxItemMapper.SourceLegacy);

        var vm = new WorkInboxIndexViewModel
        {
            Items = items,
            PendingCount = items.Count,
            WorkflowCount = workflowCount,
            LegacyCount = legacyCount,
            EntityTypeFilter = entityType,
            SourceFilter = source
        };

        return View(vm);
    }
}
