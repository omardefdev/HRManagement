using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Web.ViewModels;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using System.Security.Claims;
using HRManagement.ServiceDefaults.Integration;

namespace Payroll.Web.Controllers;

public class PayrollRunsController : Controller
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;
    private readonly IHrCoreLookupClient _hrCoreLookup;

    public PayrollRunsController(PayrollDbContext db, IMediator mediator, IHrCoreLookupClient hrCoreLookup)
    {
        _db = db;
        _mediator = mediator;
        _hrCoreLookup = hrCoreLookup;
    }

    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public async Task<IActionResult> Index(
        PayrollRunStatus? status,
        DateTime? periodFrom,
        DateTime? periodTo,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is < 5 or > 100 ? SearchFilterBase.DefaultPageSize : pageSize;
        var query = _db.PayrollRuns.AsNoTracking().AsQueryable();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);
        if (periodFrom.HasValue)
            query = query.Where(r => r.PeriodEnd >= periodFrom.Value);
        if (periodTo.HasValue)
            query = query.Where(r => r.PeriodStart <= periodTo.Value);

        var totalCount = await query.CountAsync();
        var items = await query.OrderByDescending(r => r.RunDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        ViewBag.PayrollStatus = status;
        ViewBag.PeriodFrom = periodFrom;
        ViewBag.PeriodTo = periodTo;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        return View(items);
    }

    public IActionResult Create() => View(new PayrollRunFormViewModel
    {
        RunDate = DateTime.UtcNow,
        PeriodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month), 0, 0, 0, DateTimeKind.Utc)
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Idempotent]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(PayrollRunFormViewModel model)
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
        // PLAN-56 (G-PAY-06): the run's reversal requests, the pending one first.
        ViewBag.ReversalRequests = (IReadOnlyList<PayrollReversalRequest>)await _db.PayrollReversalRequests
            .AsNoTracking()
            .Where(r => r.PayrollRunId == id)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();
        // PLAN-56 (G-PAY-04): what each rejected version of the run was.
        ViewBag.Revisions = (IReadOnlyList<PayrollRunRevision>)await _db.PayrollRunRevisions
            .AsNoTracking()
            .Where(r => r.PayrollRunId == id)
            .OrderBy(r => r.Revision)
            .ToListAsync();
        // PLAN-54 (§3): what the pinned framework version is: interim (and under which exceptions), or older than legal packs.
        ViewBag.FrameworkNote = await Payroll.Application.Features.PayrollConfigurations.PayrollFrameworkIndicators.ForRunAsync(
            HttpContext.RequestServices.GetRequiredService<Payroll.Application.Abstractions.IPayrollRepository>(),
            run.PayrollConfigurationId,
            HttpContext.RequestAborted);
        // PLAN-57 (§5): the attendance panel: the gate's mode, who is not closed for the run's period (by name), the carried differences.
        var attendance = await HttpContext.RequestServices.GetRequiredService<Payroll.Application.Services.PayrollAttendanceGate>()
            .StatusAsync(run, HttpContext.RequestAborted);
        ViewBag.Attendance = attendance;
        ViewBag.EmployeeNames = await ResolveEmployeeNamesAsync(
            run.SalaryLines.Select(line => line.EmployeeId).Concat(attendance.MissingAttendance.Take(AttendanceNamesShown)));
        // PLAN-55 (§2): the accounts the run pinned, and the employees with none (shown by name).
        ViewBag.BankReadiness = await _mediator.Send(
            new Payroll.Application.Features.BankAccounts.GetRunBankReadinessQuery(id), HttpContext.RequestAborted);

        return View(run);
    }

    /// <summary>PLAN-57: how many employees with no closed attendance the run's page lists by name; the count is always shown.</summary>
    public const int AttendanceNamesShown = 50;

    private async Task<Dictionary<Guid, string>> ResolveEmployeeNamesAsync(IEnumerable<Guid> ids)
    {
        var result = ids.Distinct().ToDictionary(id => id, _ => "غير متاح");
        if (result.Count == 0)
            return result;

        try
        {
            var rows = await _hrCoreLookup.GetEmployeesByIdsAsync(result.Keys.ToList(), HttpContext.RequestAborted);
            foreach (var row in rows)
                result[row.Id] = row.Label;
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<PayrollRunsController>>()
                .LogWarning(ex, "Employee name lookup failed for payroll details.");
        }
        return result;
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
    public async Task<IActionResult> CancelJob(Guid jobId, Guid runId, string? reason, [FromServices] IAuthorizationService authorization)
    {
        // PLAN-56 (G-PAY-04): cancelling the job is cancelling the run, with its reason, its rule (D-PAY-18) and the release of its inputs.
        var (mayProcess, mayApprove) = await PayrollRunActHttp.CancelRightsAsync(authorization, User);
        var (success, error) = await _mediator.Send(
            new CancelPayrollRunJobCommand(jobId, RequireUserId(), reason, mayProcess, mayApprove));
        TempData[success ? "Success" : "Error"] = success ? "تم إلغاء التشغيل وتحرير مدخلاته" : error;
        return RedirectToAction(nameof(Details), new { id = runId });
    }

    /// <summary>
    /// PLAN-56 (G-PAY-04, D-PAY-18): cancels a run that is not posted, with a reason. The processor cancels what was not sent for
    /// review; a run under review or approved is cancelled by the holder of the approval policy.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollRunCancel)]
    public async Task<IActionResult> CancelRun(Guid id, string? reason, [FromServices] IAuthorizationService authorization)
    {
        var (mayProcess, mayApprove) = await PayrollRunActHttp.CancelRightsAsync(authorization, User);
        var result = await _mediator.Send(new CancelPayrollRunCommand(id, RequireUserId(), reason, mayProcess, mayApprove));
        if (result.Outcome == Payroll.Application.Features.PayrollRuns.Lifecycle.RunActOutcome.NotFound)
            return NotFound();
        if (result.Outcome == Payroll.Application.Features.PayrollRuns.Lifecycle.RunActOutcome.Forbidden)
            return Forbid();
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "تم إلغاء التشغيل وتحرير فترته ومدخلاته" : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>PLAN-56 (G-PAY-07): gives up a failed or paused calculation and starts a new one with a new snapshot.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> RestartJob(Guid jobId, Guid runId)
    {
        var (result, lines, _) = await _mediator.Send(new RestartCalculationCommand(jobId, RequireUserId()));
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? $"أُعيد بدء الحساب بلقطة جديدة ({lines} بند)"
            : result.Error;
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
        var (success, error) = await _mediator.Send(new ProcessPaymentBatchCommand(batchId, RequireUserId()));
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
        var (success, error) = await _mediator.Send(
            new Payroll.Application.Features.PayrollRuns.Lifecycle.RejectPayrollRunCommand(id, userId, notes));
        TempData[success ? "Success" : "Warning"] = success ? "تم رفض التشغيل وإعادة فتحه مسودةً للتصحيح وإعادة الحساب" : error;
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
    [Authorize(Policy = HrManagementPolicies.PayrollReverse)]
    public async Task<IActionResult> ReverseRun(Guid id, string? reason)
    {
        // PLAN-56 (G-PAY-06): a reversal is a request with a reason; another person decides it («طلبات العكس»).
        var result = await _mediator.Send(
            new Payroll.Application.Features.PayrollRuns.Reversal.RequestPayrollReversalCommand(id, RequireUserId(), reason));
        if (result.Outcome == Payroll.Application.Features.PayrollRuns.Lifecycle.RunActOutcome.NotFound)
            return NotFound();
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "سُجّل طلب عكس التشغيل؛ يقرر فيه معتمد آخر غير طالبه وغير من رحّل التشغيل."
            : result.Error;
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
