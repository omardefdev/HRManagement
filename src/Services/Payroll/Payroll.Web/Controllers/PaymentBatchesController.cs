using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PartialDisbursement;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-58: the batches' screens. The list, and a batch's page: its orders by state, its funding, its sendings and their bank files (each
/// downloaded as it was stored, by a holder of <c>Payroll.Post</c> or <c>Payroll.Approve</c>, audited as a bulk export), its held
/// employees by name, and the acts of the one path: a funding decision (the approver), <c>process</c> and <c>submit-held</c> (the
/// poster). A batch belongs to the whole agency.
/// </summary>
[OrgScopeCentralOnly("a payment batch pays a whole agency's run, not a unit")]
public class PaymentBatchesController(PayrollDbContext db, IMediator mediator, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(Guid? payrollRunId, PaymentBatchStatus? status, DateTime? processedFrom, DateTime? processedTo, bool? hasBankReference)
    {
        var query = db.PaymentBatches.AsNoTracking().Include(p => p.PayrollRun).AsQueryable();
        if (payrollRunId.HasValue) query = query.Where(x => x.PayrollRunId == payrollRunId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (processedFrom.HasValue) query = query.Where(x => x.ProcessedAt >= processedFrom.Value);
        if (processedTo.HasValue) query = query.Where(x => x.ProcessedAt <= processedTo.Value);
        if (hasBankReference == true) query = query.Where(x => x.BankTransferReference != null && x.BankTransferReference != "");
        if (hasBankReference == false) query = query.Where(x => x.BankTransferReference == null || x.BankTransferReference == "");

        var items = await query.OrderByDescending(x => x.ProcessedAt ?? x.CreatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.PayrollRunId = payrollRunId;
        ViewBag.Status = status;
        ViewBag.ProcessedFrom = processedFrom;
        ViewBag.ProcessedTo = processedTo;
        ViewBag.HasBankReference = hasBankReference;
        ViewBag.PayrollRuns = new SelectList(
            await db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "RunDate", payrollRunId);
        return View(items);
    }

    public async Task<IActionResult> Details(Guid id, [FromServices] IAuthorizationService authorization)
    {
        var batch = await db.PaymentBatches.AsNoTracking().Include(b => b.PayrollRun).FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null) return NotFound();

        var orders = await db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == id).ToListAsync();
        var files = await db.PaymentBatchFiles.AsNoTracking().Where(f => f.PaymentBatchId == id).OrderBy(f => f.Sequence)
            .Select(f => new PaymentBatchFileRowViewModel(f.Id, f.Sequence, f.Kind, f.ControlCount, f.ControlTotal, f.Sha256, f.GeneratedBy, f.GeneratedAt, f.SupersededById, f.ReissueReason))
            .ToListAsync();
        var funding = batch.FundingDecisionId is Guid decisionId
            ? await db.FundingDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == decisionId)
            : null;
        var held = orders.Where(o => o.Status == PaymentOrderStatus.Held).ToList();
        var names = await employeeLookup.GetEmployeeDisplayNamesAsync(held.Select(o => o.EmployeeId).Distinct(), HttpContext.RequestAborted);

        var mayPost = (await authorization.AuthorizeAsync(User, HrManagementPolicies.PayrollPost)).Succeeded;
        var mayApprove = (await authorization.AuthorizeAsync(User, HrManagementPolicies.PayrollApprove)).Succeeded;
        return View(new PaymentBatchDetailsViewModel(
            batch,
            orders.GroupBy(o => o.Status).Select(g => new PaymentOrderTally(g.Key, g.Count(), g.Sum(o => o.Amount))).OrderBy(t => t.Status).ToList(),
            files,
            funding,
            held.Select(o => new HeldOrderViewModel(o.EmployeeId, names.GetValueOrDefault(o.EmployeeId), o.HoldReason, o.Amount)).ToList(),
            mayPost,
            mayApprove));
    }

    /// <summary>A stored bank file, as it was handed over (never built again). The poster's or the approver's; audited as a bulk export.</summary>
    public async Task<IActionResult> DownloadFile(Guid id, Guid fileId, [FromServices] IAuthorizationService authorization)
    {
        if (!(await authorization.AuthorizeAsync(User, HrManagementPolicies.PayrollPost)).Succeeded
            && !(await authorization.AuthorizeAsync(User, HrManagementPolicies.PayrollApprove)).Succeeded)
            return Forbid();
        var file = await db.PaymentBatchFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId && f.PaymentBatchId == id);
        if (file is null) return NotFound();
        PaymentBatchFileAudit.Record(HttpContext, file);
        return File(file.Content, "text/csv", file.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Fund(Guid id, decimal fundingRatio, decimal? approvedAmount, string? notes)
    {
        var result = await mediator.Send(new CreateFundingDecisionCommand(
            id, approvedAmount, fundingRatio, null, PayProfileChangeHttp.RequireUserId(User), notes));
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "سُجّل قرار التمويل: وُزّع المبلغ المعتمد على الأوامر بقواعد الأولوية وقُيّد الباقي متأخرات"
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> Process(Guid id)
    {
        var result = await mediator.Send(new ProcessPaymentBatchCommand(id, PayProfileChangeHttp.RequireUserId(User)));
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "أُرسلت الدفعة إلى القناة مع ملفها المصرفي؛ أوامر المحجوزين خارج الإرسال"
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> SubmitHeld(Guid id)
    {
        var result = await mediator.Send(new SubmitHeldPaymentOrdersCommand(id, PayProfileChangeHttp.RequireUserId(User)));
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "أُرسلت أوامر المُفرَج عنهم بملف تكميلي"
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }
}

/// <summary>PLAN-58 (§2): a download of a stored bank file is a bulk export in the security audit; it names the file, never an account.</summary>
internal static class PaymentBatchFileAudit
{
    public static void Record(HttpContext context, PaymentBatchFile file) =>
        SecurityAudit.Log(
            context,
            SecurityAudit.EventTypes.BulkExport,
            FormattableString.Invariant($"payment-batch-file:{file.Id:N}"),
            JsonSerializer.Serialize(new
            {
                paymentBatchId = file.PaymentBatchId,
                sequence = file.Sequence,
                kind = file.Kind.ToString(),
                controlCount = file.ControlCount,
                sha256 = file.Sha256,
            }));
}
