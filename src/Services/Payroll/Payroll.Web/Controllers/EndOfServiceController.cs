using MediatR;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.EndOfService;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using System.Security.Claims;

namespace Payroll.Web.Controllers;

public class EndOfServiceController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;

    public EndOfServiceController(PayrollDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }
    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public async Task<IActionResult> Index()
    {
        var items = await _db.EndOfServiceSettlements.AsNoTracking()
            .OrderByDescending(e => e.CalculatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await _db.EndOfServiceSettlements.FindAsync(id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new EndOfServiceSettlement());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(EndOfServiceSettlement model)
    {
        if (!ModelState.IsValid) return View(model);
        model.Status = EosStatus.Draft;
        model.CalculatedAt = DateTime.UtcNow;
        _db.EndOfServiceSettlements.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء مسودة التسوية";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var userId = RequireUserId();
        var (success, error) = await _mediator.Send(new ApproveEosSettlementCommand(id, userId));
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Success"] = "تم اعتماد التسوية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> MarkPaid(Guid id)
    {
        var (success, error) = await _mediator.Send(new MarkEosSettlementPaidCommand(id, RequireUserId()));
        TempData[success ? "Success" : "Error"] = success ? "تم تسجيل الصرف" : error;
        return RedirectToAction(nameof(Details), new { id });
    }
}
