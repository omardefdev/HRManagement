using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using System.Security.Claims;

namespace Payroll.Web.Controllers;

public class PayrollRunsController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;

    public PayrollRunsController(PayrollDbContext db, IMediator mediator)
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
        var items = await _db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.RunDate).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(items);
    }

    public IActionResult Create() => View(new PayrollRun
    {
        RunDate = DateTime.UtcNow,
        PeriodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month), 0, 0, 0, DateTimeKind.Utc),
        Status = PayrollRunStatus.Draft
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Idempotent]
    public async Task<IActionResult> Create(PayrollRun model)
    {
        if (!ModelState.IsValid) return View(model);

        var (success, id, error) = await _mediator.Send(new CreatePayrollRunCommand(
            model.PeriodStart, model.PeriodEnd, model.RunDate));

        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "فشل إنشاء التشغيل");
            return View(model);
        }

        TempData["Success"] = "تم إنشاء تشغيل الرواتب";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var run = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .Include(r => r.Approvals)
            .Include(r => r.PayrollConfiguration)
            .Include(r => r.PaymentBatches)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound();

        var lineIds = run.SalaryLines.Select(l => l.Id).ToList();
        var components = await _db.SalaryLineComponents
            .Where(c => lineIds.Contains(c.SalaryLineId))
            .OrderBy(c => c.ComponentType)
            .AsNoTracking()
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        ViewBag.Components = components.GroupBy(c => c.SalaryLineId)
            .ToDictionary(g => g.Key, g => g.ToList());
        ViewBag.ComponentCounts = components.GroupBy(c => c.SalaryLineId)
            .ToDictionary(g => g.Key, g => g.Count());

        var job = await _db.PayrollRunJobs
            .Include(j => j.Partitions)
            .AsNoTracking()
            .Where(j => j.PayrollRunId == id)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync();
        ViewBag.Job = job;

        return View(run);
    }

    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> AddSalaryLine(Guid payrollRunId)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == payrollRunId);
        if (run == null) return NotFound();

        var error = run.ValidateCanModifyLines();
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id = payrollRunId });
        }

        return View(new SalaryLine { PayrollRunId = payrollRunId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> AddSalaryLine([Bind("PayrollRunId,EmployeeId,GrossPay,TotalDeductions,PayComponents")] SalaryLine line)
    {
        if (!ModelState.IsValid) return View(line);

        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == line.PayrollRunId);
        if (run == null) return NotFound();

        var stateError = run.ValidateCanModifyLines();
        if (stateError != null)
        {
            ModelState.AddModelError(string.Empty, stateError);
            return View(line);
        }

        if (await _db.SalaryLines.AnyAsync(s =>
                s.PayrollRunId == line.PayrollRunId && s.EmployeeId == line.EmployeeId))
        {
            ModelState.AddModelError(string.Empty, "يوجد بالفعل بند راتب لهذا الموظف في نفس التشغيل.");
            return View(line);
        }

        PayrollCalculation.ApplyNetFromGrossAndDeductions(line);
        _db.SalaryLines.Add(line);
        await _db.SaveChangesAsync();
        SecurityAudit.Log(
            HttpContext,
            SecurityAudit.EventTypes.PayrollOverride,
            resource: $"PayrollRun:{line.PayrollRunId}/Employee:{line.EmployeeId}",
            detailJson: "{\"action\":\"AddSalaryLine\"}");
        TempData["Success"] = "تم إضافة بند الراتب";
        return RedirectToAction(nameof(Details), new { id = line.PayrollRunId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> GenerateLines(Guid id)
    {
        var (success, count, error) = await _mediator.Send(new CalculatePayrollRunCommand(id));
        if (!success)
            TempData["Error"] = error;
        else
            TempData["Success"] = $"تم توليد {count} بند راتب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> PauseJob(Guid jobId, Guid runId)
    {
        var (success, error) = await _mediator.Send(new PausePayrollRunJobCommand(jobId));
        TempData[success ? "Success" : "Error"] = success ? "تم إيقاف مهمة الحساب" : error;
        return RedirectToAction(nameof(Details), new { id = runId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> ResumeJob(Guid jobId, Guid runId)
    {
        var (success, count, _, error) = await _mediator.Send(new ResumePayrollRunJobCommand(jobId));
        TempData[success ? "Success" : "Error"] = success
            ? $"تم استئناف المهمة ({count} بند جديد)"
            : error;
        return RedirectToAction(nameof(Details), new { id = runId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> CancelJob(Guid jobId, Guid runId)
    {
        var (success, error) = await _mediator.Send(new CancelPayrollRunJobCommand(jobId));
        TempData[success ? "Success" : "Error"] = success ? "تم إلغاء مهمة التشغيل" : error;
        return RedirectToAction(nameof(Details), new { id = runId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> ValidateRun(Guid id)
    {
        var (success, error) = await _mediator.Send(new ValidatePayrollRunCommand(id));
        TempData[success ? "Success" : "Error"] = success ? "تم التحقق وتجميد النتائج" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> ProcessPayment(Guid batchId, Guid runId)
    {
        var (success, error) = await _mediator.Send(new ProcessPaymentBatchCommand(batchId));
        TempData[success ? "Success" : "Error"] = success ? "تم صرف الدفعة" : error;
        return RedirectToAction(nameof(Details), new { id = runId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> SubmitForReview(Guid id)
    {
        var (success, error) = await _mediator.Send(new SubmitPayrollRunForReviewCommand(id));
        TempData[success ? "Success" : "Error"] = success ? "تم الإرسال للمراجعة" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> ApproveRun(Guid id)
    {
        var userId = RequireUserId();
        var (success, error) = await _mediator.Send(new ApprovePayrollRunCommand(id, userId, "PayrollApprover", null));
        TempData[success ? "Success" : "Error"] = success ? "تم اعتماد التشغيل" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> RejectRun(Guid id, string? notes)
    {
        var userId = RequireUserId();
        var (success, error) = await _mediator.Send(new RejectPayrollRunCommand(id, userId, notes));
        TempData[success ? "Success" : "Warning"] = success ? "تم رفض التشغيل وإعادته للحساب" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> PostPayroll(Guid id)
    {
        var (success, error) = await _mediator.Send(new PostPayrollRunCommand(id, RequireUserId()));
        TempData[success ? "Success" : "Warning"] = success ? "تم ترحيل الرواتب ونشر الحدث" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> ReverseRun(Guid id)
    {
        var (success, error) = await _mediator.Send(new ReversePayrollRunCommand(id));
        TempData[success ? "Success" : "Error"] = success ? "تم عكس التشغيل" : error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> CreateCorrection(Guid sourceRunId)
    {
        var (success, correctionId, error) = await _mediator.Send(
            new CreateCorrectionRunCommand(sourceRunId, DateTime.UtcNow));
        if (!success)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id = sourceRunId });
        }
        TempData["Success"] = "تم إنشاء تشغيل تصحيح رجعي";
        return RedirectToAction(nameof(Details), new { id = correctionId });
    }
}
