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
using HRManagement.ServiceDefaults.Integration;

namespace HR.Contracts.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrContractsHireView)]
public class HireWizardController : Controller
{
    private readonly IContractsRepository _repo;
    private readonly IMediator _mediator;
    private readonly ContractsDbContext _db;
    private readonly IHrCoreLookupClient _hrCoreLookup;

    public HireWizardController(
        IContractsRepository repo,
        IMediator mediator,
        ContractsDbContext db,
        IHrCoreLookupClient hrCoreLookup)
    {
        _repo = repo;
        _mediator = mediator;
        _db = db;
        _hrCoreLookup = hrCoreLookup;
    }

    private Guid? ParseUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index(ServiceTransactionState? state, Guid? employeeId)
    {
        var query = _repo.ServiceTransactions
            .AsNoTracking()
            .Where(t => t.TransactionType == ServiceTransactionType.Appointment
                        && t.State != ServiceTransactionState.Archived
                        && t.State != ServiceTransactionState.Cancelled);
        if (state.HasValue)
            query = query.Where(t => t.State == state.Value);
        if (employeeId.HasValue)
            query = query.Where(t => t.EmployeeId == employeeId.Value);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .ToListAsync();

        ViewBag.StateFilter = state;
        ViewBag.EmployeeId = employeeId;
        var nameIds = items.Select(t => t.EmployeeId).ToList();
        if (employeeId.HasValue)
            nameIds.Add(employeeId.Value);
        ViewBag.EmployeeNames = await ResolveEmployeeNamesAsync(nameIds);
        return View(items);
    }

    private async Task<Dictionary<Guid, string>> ResolveEmployeeNamesAsync(IEnumerable<Guid> ids)
    {
        var result = ids.Distinct().ToDictionary(id => id, _ => "غير متاح");
        if (result.Count == 0)
            return result;

        try
        {
            foreach (var row in await _hrCoreLookup.GetEmployeesByIdsAsync(result.Keys.ToList()))
                result[row.Id] = row.Label;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<HireWizardController>>()
                .LogWarning(ex, "Employee name lookup failed for hire wizard.");
        }

        return result;
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
    [Authorize(Policy = HrManagementPolicies.HrContractsHireExecute)]
    public async Task<IActionResult> Step3SubmitForApproval(Guid transactionId)
    {
        var txn = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (txn is null) return NotFound();

        if (txn.State != ServiceTransactionState.InProcess)
        {
            TempData["Error"] = "لا يمكن رفع المعاملة للاعتماد من حالتها الحالية.";
            return RedirectToAction(nameof(Step3), new { transactionId });
        }

        try
        {
            txn.MoveTo(ServiceTransactionState.PendingApproval, ParseUserId(), "رفع معاملة التعيين للاعتماد");
            await _repo.SaveChangesAsync(default);
            TempData["Success"] = "تم رفع معاملة التعيين للاعتماد الإداري.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Step3), new { transactionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrManagementApprove)]
    public async Task<IActionResult> Step3Approve(Guid transactionId)
    {
        var txn = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (txn is null) return NotFound();

        if (txn.State != ServiceTransactionState.PendingApproval)
        {
            TempData["Error"] = "لا يمكن اعتماد المعاملة في حالتها الحالية أو تم اعتمادها مسبقاً.";
            return RedirectToAction(nameof(Step3), new { transactionId });
        }

        try
        {
            txn.MoveTo(ServiceTransactionState.Approved, ParseUserId(), "اعتماد الإدارة العليا");

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
            TempData["Success"] = "تم اعتماد المعاملة. يتولى منفذ التعيين بدء التنفيذ في الخطوة التالية.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Step3), new { transactionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrContractsHireExecute)]
    public async Task<IActionResult> Step3Execute(Guid transactionId)
    {
        var txn = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (txn is null) return NotFound();

        if (txn.State != ServiceTransactionState.Approved)
        {
            TempData["Error"] = "لا يمكن بدء التنفيذ قبل اعتماد المعاملة إدارياً.";
            return RedirectToAction(nameof(Step3), new { transactionId });
        }

        try
        {
            txn.MoveTo(ServiceTransactionState.Executing, ParseUserId(), "بدء تنفيذ معاملة التعيين");
            await _repo.SaveChangesAsync(default);
            TempData["Success"] = "بدأ تنفيذ معاملة التعيين. أكمل بنود قائمة التحقق قبل الإغلاق.";
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
        var transaction = await _repo.GetServiceTransactionAsync(transactionId, default);
        if (transaction is null) return NotFound();
        if (transaction.State != ServiceTransactionState.Executing)
        {
            TempData["Error"] = "لا يمكن إكمال المعاملة إلا بعد بدء التنفيذ وبقاء حالتها قيد التنفيذ.";
            return RedirectToAction(nameof(Step4), new { transactionId });
        }

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
