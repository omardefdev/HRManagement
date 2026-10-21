using System.Security.Claims;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.SharedKernel.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
public class ServiceTransactionsController : Controller
{
    private readonly IContractsRepository _repo;
    private readonly IHrCoreLookupClient _hrCoreLookup;
    public ServiceTransactionsController(IContractsRepository repo, IHrCoreLookupClient hrCoreLookup)
    {
        _repo = repo;
        _hrCoreLookup = hrCoreLookup;
    }

    private Guid? ParseUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index(ServiceTransactionState? state, ServiceTransactionType? type, Guid? employeeId)
    {
        var query = _repo.ServiceTransactions.AsNoTracking();
        if (state.HasValue) query = query.Where(t => t.State == state.Value);
        if (type.HasValue) query = query.Where(t => t.TransactionType == type.Value);
        if (employeeId.HasValue) query = query.Where(t => t.EmployeeId == employeeId.Value);

        var items = await query.OrderByDescending(t => t.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.StateFilter = state;
        ViewBag.TypeFilter = type;
        ViewBag.EmployeeId = employeeId;
        ViewBag.EmployeeNames = await ResolveEmployeeNamesAsync(items.Select(x => x.EmployeeId).Append(employeeId ?? Guid.Empty).Where(id => id != Guid.Empty));
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var txn = await _repo.GetServiceTransactionAsync(id, default);
        if (txn is null) return NotFound();
        ViewBag.EmployeeName = (await ResolveEmployeeNamesAsync([txn.EmployeeId]))
            .GetValueOrDefault(txn.EmployeeId, "غير متاح");
        ViewBag.ActorNames = await ResolveActorNamesAsync(txn.StateHistory.Select(h => h.ChangedBy));
        return View(txn);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Advance(Guid id, ServiceTransactionState nextState, string? reason)
    {
        var txn = await _repo.GetServiceTransactionAsync(id, default);
        if (txn is null) return NotFound();

        try
        {
            if (txn.TransactionType == ServiceTransactionType.Appointment
                && nextState == ServiceTransactionState.PendingApproval)
            {
                var hasContract = await _repo.Contracts.AnyAsync(c => c.EmployeeId == txn.EmployeeId, default);
                var hasCommencement = await _repo.CommencementRecords.AnyAsync(c => c.EmployeeId == txn.EmployeeId, default);
                if (!HireWorkflowRules.CanAdvanceToPendingApproval(txn, hasContract, hasCommencement))
                {
                    TempData["Error"] = "يتطلب التعيين عقداً مسجلاً وإثبات مباشرة قبل الاعتماد.";
                    return RedirectToAction(nameof(Details), new { id });
                }
            }

            txn.MoveTo(nextState, ParseUserId(), reason);
            await _repo.SaveChangesAsync(default);
            TempData["Success"] = $"تم نقل المعاملة إلى حالة {EnumDisplayExtensions.GetDisplayName(nextState)}";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<Dictionary<Guid, string>> ResolveEmployeeNamesAsync(IEnumerable<Guid> ids)
    {
        var result = ids.Distinct().ToDictionary(id => id, _ => "غير متاح");
        if (result.Count == 0)
            return result;

        try
        {
            var rows = await _hrCoreLookup.GetEmployeesByIdsAsync(result.Keys.ToList());
            foreach (var row in rows)
                result[row.Id] = row.Label;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ServiceTransactionsController>>()
                .LogWarning(ex, "Employee name lookup failed for service transactions.");
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveActorNamesAsync(IEnumerable<Guid?> actorIds)
    {
        var ids = actorIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var result = ids.ToDictionary(id => id, _ => "مستخدم النظام");
        try
        {
            foreach (var row in await _hrCoreLookup.GetEmployeesByIdsAsync(ids))
                result[row.Id] = row.Label;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ServiceTransactionsController>>()
                .LogWarning(ex, "Actor name lookup failed for service transaction history.");
        }

        var currentUserId = ParseUserId();
        if (currentUserId.HasValue && result.ContainsKey(currentUserId.Value))
        {
            var username = User.FindFirstValue("preferred_username") ?? User.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(username))
                result[currentUserId.Value] = username;
        }

        return result;
    }
}
