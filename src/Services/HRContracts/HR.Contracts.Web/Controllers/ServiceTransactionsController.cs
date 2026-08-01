using System.Security.Claims;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
public class ServiceTransactionsController : Controller
{
    private readonly IContractsRepository _repo;
    public ServiceTransactionsController(IContractsRepository repo) => _repo = repo;

    private Guid? ParseUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index(ServiceTransactionState? state, ServiceTransactionType? type)
    {
        var query = _repo.ServiceTransactions.AsNoTracking();
        if (state.HasValue) query = query.Where(t => t.State == state.Value);
        if (type.HasValue) query = query.Where(t => t.TransactionType == type.Value);

        var items = await query.OrderByDescending(t => t.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.StateFilter = state;
        ViewBag.TypeFilter = type;
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var txn = await _repo.GetServiceTransactionAsync(id, default);
        if (txn is null) return NotFound();
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
            TempData["Success"] = $"تم نقل المعاملة إلى حالة {nextState}";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
