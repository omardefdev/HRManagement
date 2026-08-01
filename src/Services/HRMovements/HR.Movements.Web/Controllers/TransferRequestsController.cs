using System.Security.Claims;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Domain.Services;
using HR.Movements.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Movements.Web.Controllers;

public class TransferRequestsController : Controller
{
    private readonly MovementsDbContext _db;
    private readonly IPublishEndpoint _publish;

    public TransferRequestsController(MovementsDbContext db, IPublishEndpoint publish)
    {
        _db = db;
        _publish = publish;
    }

    /// <summary>FUN-003: source AND target must both be in scope (closes IDOR OR-gap).</summary>
    private async Task<IActionResult?> EnsureTransferInScopeAsync(TransferRequest t)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        if (scope.IsUnrestricted) return null;
        if (!scope.CanAccess(t.FromOrgUnitId) || !scope.CanAccess(t.ToOrgUnitId))
            return Forbid();
        return null;
    }

    private async Task<IActionResult?> EnsureOrgIdsAssignableAsync(Guid fromId, Guid toId)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        if (!scope.CanAccess(fromId) || !scope.CanAccess(toId))
            return Forbid();
        return null;
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index()
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, _db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var query = _db.TransferRequests.AsNoTracking();
        if (scope.MustFilter)
            query = query.Where(x => scope.AllowedOrgUnitIds.Contains(x.FromOrgUnitId)
                                     && scope.AllowedOrgUnitIds.Contains(x.ToOrgUnitId));
        return View(await query.OrderByDescending(x => x.RequestDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    public IActionResult Create() => View(new TransferRequest
    {
        RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
        EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = ApprovalStatus.Pending
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransferRequest model)
    {
        if (!ModelState.IsValid) return View(model);
        if (await EnsureOrgIdsAssignableAsync(model.FromOrgUnitId, model.ToOrgUnitId) is { } denied) return denied;
        if (model.EffectiveDate == default)
            model.EffectiveDate = model.RequestDate;
        // Always the authenticated caller. The null-coalescing form kept a client-supplied value,
        // which let a request be attributed to someone else and skew the four-eyes check that
        // compares approver against requester.
        model.RequestedBy = ParseUserId(User);
        model.Status = ApprovalStatus.Pending;
        _db.TransferRequests.Add(model);
        await _db.SaveChangesAsync();

        var by = ParseUserId(User);
        foreach (var step in MovementChecklistDefaults.CreateFor(MovementKind.Transfer, model.Id))
            _db.MovementChecklists.Add(step);
        await _db.SaveChangesAsync();

        var recorded = await _db.MovementChecklists.FirstAsync(c =>
            c.CorrelationId == model.Id && c.Step == MovementChecklistStep.RequestRecorded);
        MovementWorkflowRules.MarkStep(recorded, by, $"transfer:{model.Id}:recorded");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل طلب النقل";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var t = await _db.TransferRequests
            .Include(x => x.ExecutionRecords)
            .Include(x => x.Approvals.OrderByDescending(a => a.ApprovedAt))
            .Include(x => x.FinancialImpacts)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (await EnsureTransferInScopeAsync(t) is { } denied) return denied;
        ViewBag.Checklist = await _db.MovementChecklists.AsNoTracking()
            .Where(c => c.CorrelationId == id && c.MovementKind == MovementKind.Transfer)
            .OrderBy(c => c.Step)
            .ToListAsync();
        return View(t);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var t = await _db.TransferRequests.FindAsync(id);
        if (t == null) return NotFound();
        if (await EnsureTransferInScopeAsync(t) is { } editDenied) return editDenied;
        if (!TransferWorkflowRules.CanEdit(t))
        {
            TempData["Error"] = "لا يمكن تعديل طلب غير معلّق";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(t);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, TransferRequest model)
    {
        if (id != model.Id) return BadRequest();
        var t = await _db.TransferRequests.FindAsync(id);
        if (t == null) return NotFound();
        if (!TransferWorkflowRules.CanEdit(t))
        {
            TempData["Error"] = "لا يمكن تعديل طلب غير معلّق";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!ModelState.IsValid) return View(model);
        if (await EnsureOrgIdsAssignableAsync(model.FromOrgUnitId, model.ToOrgUnitId) is { } orgDenied) return orgDenied;
        t.EmployeeId = model.EmployeeId;
        t.FromOrgUnitId = model.FromOrgUnitId;
        t.ToOrgUnitId = model.ToOrgUnitId;
        t.Reason = model.Reason;
        t.EffectiveDate = model.EffectiveDate == default ? t.EffectiveDate : model.EffectiveDate;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث طلب النقل";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrManagementApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var t = await _db.TransferRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (await EnsureTransferInScopeAsync(t) is { } denied) return denied;

        if (!TransferWorkflowRules.CanApprove(t))
        {
            TempData["Error"] = "لا يمكن اعتماد الطلب في حالته الحالية";
            return RedirectToAction(nameof(Details), new { id });
        }

        var by = ParseUserId(User);
        if (by == null)
        {
            TempData["Error"] = "تعذر تحديد هوية المعتمد من التوكن؛ سجّل الدخول بمستخدم يحمل معرفاً GUID";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (t.RequestedBy.HasValue && by.Value == t.RequestedBy.Value)
        {
            TempData["Error"] = "لا يمكن للمنشئ اعتماد عمله الخاص.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var sod = new SeparationOfDutiesService();
        var sodResult = sod.ValidateAction(
            by.Value, null, "TransferRequest", t.Id, SodAction.Check, t.RequestedBy, null);
        if (!sodResult.Allowed)
        {
            TempData["Error"] = sodResult.DenialReason;
            return RedirectToAction(nameof(Details), new { id });
        }

        var now = DateTime.UtcNow;
        t.Status = ApprovalStatus.Approved;
        t.ApprovedBy = by;
        t.ApprovedDate = DateOnly.FromDateTime(now);
        _db.TransferApprovals.Add(new TransferApproval
        {
            TransferRequestId = t.Id,
            ApprovedBy = by.Value,
            ApprovedAt = now,
            Notes = "اعتماد طلب نقل"
        });

        var approved = await _db.MovementChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == t.Id && c.Step == MovementChecklistStep.Approved);
        if (approved is not null && !approved.IsCompleted)
            MovementWorkflowRules.MarkStep(approved, by, $"transfer:{t.Id}:approved");

        await _db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد طلب النقل";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrManagementApprove)]
    public async Task<IActionResult> Reject(Guid id, string? notes)
    {
        var t = await _db.TransferRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (await EnsureTransferInScopeAsync(t) is { } denied) return denied;

        if (!TransferWorkflowRules.CanReject(t))
        {
            TempData["Error"] = "لا يمكن رفض الطلب في حالته الحالية";
            return RedirectToAction(nameof(Details), new { id });
        }

        var by = ParseUserId(User);
        if (by == null)
        {
            TempData["Error"] = "تعذر تحديد هوية المعتمد من التوكن";
            return RedirectToAction(nameof(Details), new { id });
        }

        t.Status = ApprovalStatus.Rejected;
        t.ApprovedBy = by;
        t.ApprovedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await _db.SaveChangesAsync();
        TempData["Success"] = string.IsNullOrWhiteSpace(notes)
            ? "تم رفض طلب النقل"
            : $"تم رفض طلب النقل: {notes.Trim()}";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Idempotent]
    public async Task<IActionResult> Execute(Guid id, decimal? salaryChange, decimal? allowanceChange, string? financialNotes)
    {
        var t = await _db.TransferRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (await EnsureTransferInScopeAsync(t) is { } denied) return denied;

        if (t.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!MovementWorkflowRules.IsEffectiveDateReached(t.EffectiveDate))
        {
            TempData["Error"] = $"لا يمكن بدء النقل قبل تاريخ النفاذ {t.EffectiveDate:yyyy-MM-dd}";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!TransferWorkflowRules.CanExecute(t))
        {
            TempData["Error"] = "يجب اعتماد الطلب وبلوغ تاريخ النفاذ قبل التنفيذ";
            return RedirectToAction(nameof(Details), new { id });
        }

        var checklist = await _db.MovementChecklists
            .Where(c => c.CorrelationId == id && c.MovementKind == MovementKind.Transfer)
            .ToListAsync();
        if (!MovementWorkflowRules.PreExecuteChecklistComplete(checklist))
        {
            TempData["Error"] = "أكمل خطوات الحوكمة الإلزامية (تسجيل + اعتماد بدليل) قبل التنفيذ";
            return RedirectToAction(nameof(Details), new { id });
        }

        var transfers = await _db.TransferRequests.AsNoTracking()
            .Where(x => x.EmployeeId == t.EmployeeId && !x.IsCancelled).ToListAsync();
        var secondments = await _db.SecondmentRequests.AsNoTracking()
            .Where(x => x.EmployeeId == t.EmployeeId && !x.IsCancelled).ToListAsync();
        var loans = await _db.LoanAssignmentRequests.AsNoTracking()
            .Where(x => x.EmployeeId == t.EmployeeId && !x.IsCancelled).ToListAsync();
        if (MovementWorkflowRules.HasConcurrentMovementConflict(t.EmployeeId, t.Id, transfers, secondments, loans))
        {
            TempData["Error"] = "يوجد حركة متزامنة نشطة لنفس الموظف (نقل/انتداب/إعارة)";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (TransferWorkflowRules.IsExecuted(t))
        {
            TempData["Warning"] = "تم تنفيذ هذا الطلب مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }

        var executedAt = DateTime.UtcNow;
        var by = ParseUserId(User);

        var transitioned = await _db.TransferRequests
            .Where(x => x.Id == id && x.Status == ApprovalStatus.Approved && !x.IsCancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ApprovalStatus.Executed)
                .SetProperty(x => x.UpdatedAt, executedAt));

        if (transitioned == 0)
        {
            TempData["Warning"] = "تم تنفيذ هذا الطلب مسبقاً أو حالته لم تعد تسمح بالتنفيذ";
            return RedirectToAction(nameof(Details), new { id });
        }

        _db.MovementExecutionRecords.Add(new MovementExecutionRecord
        {
            TransferRequestId = t.Id,
            EmployeeId = t.EmployeeId,
            ExecutedAt = executedAt,
            ExecutedBy = by
        });

        if (salaryChange.GetValueOrDefault() != 0 || allowanceChange.GetValueOrDefault() != 0)
        {
            _db.TransferFinancialImpacts.Add(new TransferFinancialImpact
            {
                TransferRequestId = t.Id,
                EmployeeId = t.EmployeeId,
                SalaryChange = salaryChange ?? 0,
                AllowanceChange = allowanceChange ?? 0,
                Notes = financialNotes,
                CalculatedAt = executedAt
            });
        }

        var executedStep = checklist.FirstOrDefault(c => c.Step == MovementChecklistStep.Executed);
        if (executedStep is not null && !executedStep.IsCompleted)
            MovementWorkflowRules.MarkStep(executedStep, by, $"transfer:{t.Id}:executed");

        await _db.SaveChangesAsync();

        var correlation = CorrelationIdFlow.Get() ?? Guid.NewGuid().ToString("D");
        await _publish.Publish<IStartTransferSaga>(new
        {
            TransferRequestId = t.Id,
            t.EmployeeId,
            t.FromOrgUnitId,
            t.ToOrgUnitId,
            ExecutedAt = executedAt,
            SalaryChange = salaryChange,
            AllowanceChange = allowanceChange,
            CorrelationId = correlation,
            TenantId = TenantIdFlow.Get()
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم بدء تنسيق النقل عبر الأنظمة المرتبطة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var t = await _db.TransferRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (await EnsureTransferInScopeAsync(t) is { } denied) return denied;

        if (MovementWorkflowRules.CanCancelTransfer(t))
        {
            MovementWorkflowRules.SoftCancelTransfer(t, reason);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إلغاء طلب النقل (soft-cancel)";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (MovementWorkflowRules.CanCancelAfterExecutePreEffective(t))
        {
            MovementWorkflowRules.SoftCancelTransfer(t, reason);
            await _publish.Publish<ICancelTransferSaga>(new
            {
                TransferRequestId = t.Id,
                Reason = t.CancellationReason ?? "إلغاء قبل النفاذ",
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إلغاء النقل وتعويض الأطراف المكتملة";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Error"] = "لا يمكن إلغاء الطلب في حالته الحالية";
        return RedirectToAction(nameof(Details), new { id });
    }
}
