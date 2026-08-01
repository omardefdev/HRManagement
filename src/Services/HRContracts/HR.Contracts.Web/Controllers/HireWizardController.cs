using System.Security.Claims;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;

namespace HR.Contracts.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
public class HireWizardController : Controller
{
    private readonly IContractsRepository _repo;
    private readonly IMediator _mediator;
    private readonly ContractsDbContext _db;

    public HireWizardController(IContractsRepository repo, IMediator mediator, ContractsDbContext db)
    {
        _repo = repo;
        _mediator = mediator;
        _db = db;
    }

    private Guid? ParseUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _repo.ServiceTransactions
            .AsNoTracking()
            .Where(t => t.TransactionType == ServiceTransactionType.Appointment
                        && t.State != ServiceTransactionState.Archived
                        && t.State != ServiceTransactionState.Cancelled)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .ToListAsync();
        return View(items);
    }

    public IActionResult Step1() => RedirectToAction(nameof(Index));

    [HttpGet]
    public async Task<IActionResult> Continue(Guid employeeId)
    {
        if (await this.ForbidIfEmployeeOutOfScopeAsync(
                User, employeeId, _db.EmployeeOrgUnitSnapshots, _db.OrgUnitSnapshots) is { } denied)
            return denied;

        var txn = await HireCaseSupport.GetOpenAppointmentTransactionAsync(_repo, employeeId, default);
        if (txn is null)
        {
            TempData["Error"] = "لا توجد معاملة تعيين جاهزة بعد. انتظر لحظات ثم أعد المحاولة، أو افتح القائمة.";
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Step2), new { transactionId = txn.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Step1(Guid employeeId) => Continue(employeeId);

    public async Task<IActionResult> Step2(Guid transactionId)
    {
        var txn = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (txn is null) return NotFound();

        var checklist = await _mediator.Send(new GetHireChecklistQuery(transactionId));
        ViewBag.ChecklistLabels = checklist.ToDictionary(c => c.Step, c => HireChecklistDefaults.StepLabel(c.Step));
        return View((txn, checklist));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Step2Confirm(Guid transactionId) =>
        RedirectToAction(nameof(Step3), new { transactionId });

    public async Task<IActionResult> Step3(Guid transactionId)
    {
        var txn = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (txn is null) return NotFound();
        return View(txn);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrManagementApprove)]
    public async Task<IActionResult> Step3Approve(Guid transactionId)
    {
        var txn = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (txn is null) return NotFound();

        try
        {
            if (txn.State == ServiceTransactionState.InProcess)
                txn.MoveTo(ServiceTransactionState.PendingApproval, ParseUserId(), "رفع للاعتماد");
            if (txn.State == ServiceTransactionState.PendingApproval)
                txn.MoveTo(ServiceTransactionState.Approved, ParseUserId(), "اعتماد الإدارة العليا");
            if (txn.State == ServiceTransactionState.Approved)
                txn.MoveTo(ServiceTransactionState.Executing, ParseUserId(), "بدء التنفيذ");

            var (ok, stepError) = await _mediator.Send(new CompleteHireChecklistStepCommand(
                transactionId, HireStep.CareerRecordApproved, "اعتماد السيرة",
                EvidenceRef: "wizard:career-approved",
                CompletedBy: ParseUserId()));
            if (!ok)
            {
                TempData["Error"] = stepError;
                return RedirectToAction(nameof(Step3), new { transactionId });
            }

            await _repo.SaveChangesAsync(default);
            TempData["Success"] = "تم الاعتماد والانتقال للتنفيذ";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Step3), new { transactionId });
    }

    public async Task<IActionResult> Step4(Guid transactionId)
    {
        var txn = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (txn is null) return NotFound();

        var checklist = await _mediator.Send(new GetHireChecklistQuery(transactionId));
        ViewBag.ChecklistLabels = checklist.ToDictionary(c => c.Step, c => HireChecklistDefaults.StepLabel(c.Step));
        return View((txn, checklist));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsApprove)]
    public async Task<IActionResult> Step4Complete(Guid transactionId)
    {
        var (success, error) = await _mediator.Send(new PublishHireCaseCommand(transactionId));
        if (!success)
        {
            TempData["Error"] = error ?? "تعذر إكمال التعيين";
            return RedirectToAction(nameof(Step4), new { transactionId });
        }

        await HireCaseSupport.MarkHireArchivedAsync(_repo, transactionId, default);
        await _repo.SaveChangesAsync(default);

        TempData["Success"] = "تم إكمال معاملة التعيين. سيُؤرشف السجل تلقائياً بعد تسجيل الديوان.";
        return RedirectToAction(nameof(Index));
    }
}
