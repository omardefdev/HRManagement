using System.Security.Claims;
using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Domain.Services;
using HR.Movements.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Movements.Web.Controllers;

public class MovementNoticesController(MovementsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.MovementNotices.AsNoTracking().OrderByDescending(x => x.IssueDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new MovementNotice { IssueDate = DateOnly.FromDateTime(DateTime.UtcNow) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MovementNotice model)
    {
        if (!ModelState.IsValid) return View(model);
        model.IssuedBy = ServerAssignedIdentity.ResolveActorEmployeeId(User);
        db.MovementNotices.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء نشرة التنقلات";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var n = await db.MovementNotices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound();
        return View(n);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var n = await db.MovementNotices.FindAsync(id);
        if (n == null) return NotFound();
        return View(n);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, MovementNotice model)
    {
        if (id != model.Id) return BadRequest();
        var n = await db.MovementNotices.FindAsync(id);
        if (n == null) return NotFound();
        if (!ModelState.IsValid) return View(model);
        n.Title = model.Title;
        n.IssueDate = model.IssueDate;
        n.Period = model.Period;
        n.Items = model.Items;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث نشرة التنقلات";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(DateOnly from, DateOnly to)
    {
        var executed = await db.MovementExecutionRecords
            .Include(r => r.TransferRequest)
            .Where(r => r.TransferRequest != null
                && DateOnly.FromDateTime(r.ExecutedAt) >= from
                && DateOnly.FromDateTime(r.ExecutedAt) <= to)
            .AsNoTracking()
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        var items = executed.Select(r => new
        {
            r.EmployeeId,
            From = r.TransferRequest!.FromOrgUnitId,
            To = r.TransferRequest.ToOrgUnitId,
            Date = r.ExecutedAt.ToString("yyyy-MM-dd")
        });

        var notice = new MovementNotice
        {
            Title = $"نشرة تنقلات {from} — {to}",
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Period = $"{from} — {to}",
            Items = System.Text.Json.JsonSerializer.Serialize(items),
            IssuedBy = ServerAssignedIdentity.ResolveActorEmployeeId(User)
        };
        db.MovementNotices.Add(notice);
        await db.SaveChangesAsync();
        TempData["Success"] = $"تم توليد نشرة تنقلات ({executed.Count} حركة)";
        return RedirectToAction(nameof(Details), new { id = notice.Id });
    }
}

public class DistributionPlansController(MovementsDbContext db) : Controller
{
    private async Task<IActionResult?> EnsurePlanInScopeAsync(Guid? orgUnitId)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        if (!scope.CanAccess(orgUnitId)) return Forbid();
        return null;
    }

    public async Task<IActionResult> Index()
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var query = db.DistributionPlans.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
            query = query.Where(x => scope.AllowedOrgUnitIds.Contains(x.OrgUnitId));
        return View(await query.OrderByDescending(x => x.EffectiveDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.TransferRequests = new SelectList(
            await db.TransferRequests.AsNoTracking().OrderByDescending(t => t.RequestDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "EmployeeId");
        return View(new DistributionPlan
        {
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = DistributionPlanStatus.Draft
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DistributionPlan model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.TransferRequests = new SelectList(await db.TransferRequests.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId");
            return View(model);
        }
        if (await EnsurePlanInScopeAsync(model.OrgUnitId) is { } denied) return denied;
        model.Status = DistributionPlanStatus.Draft;
        db.DistributionPlans.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء خطة التوزيع";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var p = await db.DistributionPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (await EnsurePlanInScopeAsync(p.OrgUnitId) is { } denied) return denied;
        return View(p);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var p = await db.DistributionPlans.FindAsync(id);
        if (p == null) return NotFound();
        if (await EnsurePlanInScopeAsync(p.OrgUnitId) is { } denied) return denied;
        if (p.Status != DistributionPlanStatus.Draft)
        {
            TempData["Error"] = "لا يمكن تعديل خطة غير مسودة";
            return RedirectToAction(nameof(Details), new { id });
        }
        ViewBag.TransferRequests = new SelectList(
            await db.TransferRequests.AsNoTracking().OrderByDescending(t => t.RequestDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "EmployeeId", p.TransferRequestId);
        return View(p);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DistributionPlan model)
    {
        if (id != model.Id) return BadRequest();
        var p = await db.DistributionPlans.FindAsync(id);
        if (p == null) return NotFound();
        if (await EnsurePlanInScopeAsync(p.OrgUnitId) is { } denied) return denied;
        if (await EnsurePlanInScopeAsync(model.OrgUnitId) is { } assignDenied) return assignDenied;
        if (p.Status != DistributionPlanStatus.Draft)
        {
            TempData["Error"] = "لا يمكن تعديل خطة غير مسودة";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!ModelState.IsValid)
        {
            ViewBag.TransferRequests = new SelectList(await db.TransferRequests.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId", model.TransferRequestId);
            return View(model);
        }
        p.OrgUnitId = model.OrgUnitId;
        p.EffectiveDate = model.EffectiveDate;
        p.PlannedPositions = model.PlannedPositions;
        p.TransferRequestId = model.TransferRequestId;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث خطة التوزيع";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> GapAnalysis(Guid id, int? actualHeadcount)
    {
        var p = await db.DistributionPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (await EnsurePlanInScopeAsync(p.OrgUnitId) is { } denied) return denied;

        var headcount = actualHeadcount ?? 0;
        var result = WorkforceGapAnalysisRules.Analyze(p.PlannedPositions, headcount);
        ViewBag.Plan = p;
        ViewBag.ActualHeadcount = headcount;
        return View(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GapAnalysis(Guid id, int actualHeadcount) =>
        RedirectToAction(nameof(GapAnalysis), new { id, actualHeadcount });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var p = await db.DistributionPlans.FindAsync(id);
        if (p == null) return NotFound();
        if (await EnsurePlanInScopeAsync(p.OrgUnitId) is { } denied) return denied;
        if (p.Status != DistributionPlanStatus.Draft)
        {
            TempData["Error"] = "الخطة ليست مسودة";
            return RedirectToAction(nameof(Details), new { id });
        }
        p.Status = DistributionPlanStatus.Approved;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد خطة التوزيع";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class SecondmentRequestsController(MovementsDbContext db, IPublishEndpoint publish) : Controller
{
    private static Guid? ParseUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index() =>
        View(await db.SecondmentRequests.AsNoTracking().OrderByDescending(x => x.StartDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new SecondmentRequest
    {
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
        Status = SecondmentStatus.Pending
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SecondmentRequest model)
    {
        if (!ModelState.IsValid) return View(model);
        model.RequestedBy ??= ParseUserId(User);
        model.Status = SecondmentStatus.Pending;
        db.SecondmentRequests.Add(model);
        await db.SaveChangesAsync();

        foreach (var step in MovementChecklistDefaults.CreateFor(MovementKind.Secondment, model.Id))
            db.MovementChecklists.Add(step);
        await db.SaveChangesAsync();
        var recorded = await db.MovementChecklists.FirstAsync(c =>
            c.CorrelationId == model.Id && c.Step == MovementChecklistStep.RequestRecorded);
        MovementWorkflowRules.MarkStep(recorded, ParseUserId(User), $"secondment:{model.Id}:recorded");
        await db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل طلب الانتداب";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var s = await db.SecondmentRequests
            .Include(x => x.ExecutionRecords)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        ViewBag.Checklist = await db.MovementChecklists.AsNoTracking()
            .Where(c => c.CorrelationId == id && c.MovementKind == MovementKind.Secondment)
            .OrderBy(c => c.Step)
            .ToListAsync();
        return View(s);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var s = await db.SecondmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (s.IsCancelled || s.Status != SecondmentStatus.Pending)
        {
            TempData["Error"] = "لا يمكن تعديل طلب غير معلّق";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(s);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SecondmentRequest model)
    {
        if (id != model.Id) return BadRequest();
        var s = await db.SecondmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (s.IsCancelled || s.Status != SecondmentStatus.Pending)
        {
            TempData["Error"] = "لا يمكن تعديل طلب غير معلّق";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!ModelState.IsValid) return View(model);
        s.EmployeeId = model.EmployeeId;
        s.HostOrg = model.HostOrg;
        s.StartDate = model.StartDate;
        s.EndDate = model.EndDate;
        s.Terms = model.Terms;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث طلب الانتداب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = HrManagementPolicies.HrManagementApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var s = await db.SecondmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (s.IsCancelled || s.Status != SecondmentStatus.Pending)
        {
            TempData["Error"] = "الطلب ليس قيد الانتظار";
            return RedirectToAction(nameof(Details), new { id });
        }

        var by = ParseUserId(User);
        if (by == null)
        {
            TempData["Error"] = "تعذر تحديد هوية المعتمد من التوكن";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (s.RequestedBy.HasValue && by.Value == s.RequestedBy.Value)
        {
            TempData["Error"] = "لا يمكن للمنشئ اعتماد عمله الخاص.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var sod = new HRManagement.SharedKernel.SeparationOfDuties.SeparationOfDutiesService();
        var sodResult = sod.ValidateAction(
            by.Value, null, "SecondmentRequest", s.Id,
            HRManagement.SharedKernel.SeparationOfDuties.SodAction.Check, s.RequestedBy, null);
        if (!sodResult.Allowed)
        {
            TempData["Error"] = sodResult.DenialReason;
            return RedirectToAction(nameof(Details), new { id });
        }

        s.Status = SecondmentStatus.Approved;
        var approved = await db.MovementChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == s.Id && c.Step == MovementChecklistStep.Approved);
        if (approved is not null && !approved.IsCompleted)
            MovementWorkflowRules.MarkStep(approved, by, $"secondment:{s.Id}:approved");
        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد الانتداب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(Guid id)
    {
        var s = await db.SecondmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (s.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (s.Status != SecondmentStatus.Approved)
        {
            TempData["Error"] = "يجب اعتماد الطلب قبل التفعيل";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!MovementWorkflowRules.IsEffectiveDateReached(s.StartDate))
        {
            TempData["Error"] = $"لا يمكن تفعيل الانتداب قبل تاريخ البداية {s.StartDate:yyyy-MM-dd}";
            return RedirectToAction(nameof(Details), new { id });
        }

        var checklist = await db.MovementChecklists
            .Where(c => c.CorrelationId == id && c.MovementKind == MovementKind.Secondment)
            .ToListAsync();
        if (!MovementWorkflowRules.PreExecuteChecklistComplete(checklist))
        {
            TempData["Error"] = "أكمل خطوات الحوكمة الإلزامية قبل التفعيل";
            return RedirectToAction(nameof(Details), new { id });
        }

        var transfers = await db.TransferRequests.AsNoTracking()
            .Where(x => x.EmployeeId == s.EmployeeId && !x.IsCancelled).ToListAsync();
        var secondments = await db.SecondmentRequests.AsNoTracking()
            .Where(x => x.EmployeeId == s.EmployeeId && !x.IsCancelled).ToListAsync();
        var loans = await db.LoanAssignmentRequests.AsNoTracking()
            .Where(x => x.EmployeeId == s.EmployeeId && !x.IsCancelled).ToListAsync();
        if (MovementWorkflowRules.HasConcurrentMovementConflict(s.EmployeeId, s.Id, transfers, secondments, loans))
        {
            TempData["Error"] = "يوجد حركة متزامنة نشطة لنفس الموظف";
            return RedirectToAction(nameof(Details), new { id });
        }

        s.Status = SecondmentStatus.Active;
        var executedAt = DateTime.UtcNow;
        var by = ParseUserId(User);
        db.MovementExecutionRecords.Add(new MovementExecutionRecord
        {
            SecondmentRequestId = s.Id,
            EmployeeId = s.EmployeeId,
            ExecutedAt = executedAt,
            Notes = $"تفعيل انتداب لدى {s.HostOrg}"
        });
        var executedStep = checklist.FirstOrDefault(c => c.Step == MovementChecklistStep.Executed);
        if (executedStep is not null && !executedStep.IsCompleted)
            MovementWorkflowRules.MarkStep(executedStep, by, $"secondment:{s.Id}:executed");
        await db.SaveChangesAsync();

        await publish.Publish<IStartSecondmentSaga>(new
        {
            SecondmentRequestId = s.Id,
            s.EmployeeId,
            ActivatedAt = executedAt,
            HostOrg = s.HostOrg,
            EndDate = s.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            CorrelationId = CorrelationIdFlow.Get() ?? Guid.NewGuid().ToString("D"),
            TenantId = TenantIdFlow.Get()
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم بدء تنسيق تفعيل الانتداب عبر الأنظمة المرتبطة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id)
    {
        var s = await db.SecondmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (s.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (s.Status == SecondmentStatus.Closed)
        {
            TempData["Error"] = "الطلب مغلق مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }

        s.Status = SecondmentStatus.Closed;
        var ended = await db.MovementChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == s.Id && c.Step == MovementChecklistStep.Ended);
        if (ended is not null && !ended.IsCompleted)
            MovementWorkflowRules.MarkStep(ended, ParseUserId(User), $"secondment:{s.Id}:ended");

        // FUN-003-D: light end restore via cancel/compensate for completed parties.
        await publish.Publish<ICancelSecondmentSaga>(new
        {
            SecondmentRequestId = s.Id,
            Reason = "إنهاء انتداب — استعادة أثر الأطراف",
            CorrelationId = CorrelationIdFlow.Get(),
            TenantId = TenantIdFlow.Get()
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إغلاق الانتداب واستعادة أثر الأطراف";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var s = await db.SecondmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (s.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (s.Status is SecondmentStatus.Pending or SecondmentStatus.Approved)
        {
            MovementWorkflowRules.SoftCancelSecondment(s, reason);
            await db.SaveChangesAsync();
            TempData["Success"] = "تم إلغاء طلب الانتداب";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (s.Status == SecondmentStatus.Active && !MovementWorkflowRules.IsEffectiveDateReached(s.StartDate))
        {
            MovementWorkflowRules.SoftCancelSecondment(s, reason);
            await publish.Publish<ICancelSecondmentSaga>(new
            {
                SecondmentRequestId = s.Id,
                Reason = s.CancellationReason ?? "إلغاء",
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            });
            await db.SaveChangesAsync();
            TempData["Success"] = "تم إلغاء الانتداب وتعويض الأطراف";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (s.Status == SecondmentStatus.Active)
        {
            MovementWorkflowRules.SoftCancelSecondment(s, reason);
            await publish.Publish<ICancelSecondmentSaga>(new
            {
                SecondmentRequestId = s.Id,
                Reason = s.CancellationReason ?? "إلغاء نشط",
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            });
            await db.SaveChangesAsync();
            TempData["Success"] = "تم إلغاء الانتداب النشط مع تعويض";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Error"] = "لا يمكن إلغاء الطلب في حالته الحالية";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class MovementExecutionsController(MovementsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.MovementExecutionRecords
            .Include(x => x.TransferRequest)
            .Include(x => x.SecondmentRequest)
            .AsNoTracking()
            .OrderByDescending(x => x.ExecutedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var rec = await db.MovementExecutionRecords
            .Include(x => x.TransferRequest)
            .Include(x => x.SecondmentRequest)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (rec == null) return NotFound();
        return View(rec);
    }
}

public class TransferFinancialImpactsController(MovementsDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.TransferFinancialImpacts.AsNoTracking().OrderByDescending(x => x.CalculatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        ViewBag.TransferRequests = new SelectList(
            await db.TransferRequests.AsNoTracking().OrderByDescending(t => t.RequestDate).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(),
            "Id", "EmployeeId");
        return View(new TransferFinancialImpact { CalculatedAt = DateTime.UtcNow });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransferFinancialImpact model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.TransferRequests = new SelectList(await db.TransferRequests.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "EmployeeId");
            return View(model);
        }
        db.TransferFinancialImpacts.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الأثر المالي";
        return RedirectToAction(nameof(Index));
    }
}

public class LoanAssignmentRequestsController(MovementsDbContext db, IPublishEndpoint publish) : Controller
{
    private static Guid? ParseUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    /// <summary>FUN-003: lending AND borrowing must both be in scope.</summary>
    private async Task<IActionResult?> EnsureLoanInScopeAsync(LoanAssignmentRequest s)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        if (scope.IsUnrestricted) return null;
        if (!scope.CanAccess(s.LendingOrgUnitId) || !scope.CanAccess(s.BorrowingOrgUnitId))
            return Forbid();
        return null;
    }

    private async Task<IActionResult?> EnsureOrgIdsAssignableAsync(Guid lending, Guid borrowing)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        if (!scope.CanAccess(lending) || !scope.CanAccess(borrowing))
            return Forbid();
        return null;
    }

    public async Task<IActionResult> Index()
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots);
        if (scope.IsDenied) return Forbid();
        var query = db.LoanAssignmentRequests.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
            query = query.Where(x => scope.AllowedOrgUnitIds.Contains(x.LendingOrgUnitId)
                && scope.AllowedOrgUnitIds.Contains(x.BorrowingOrgUnitId));
        return View(await query.OrderByDescending(x => x.StartDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    public IActionResult Create() => View(new LoanAssignmentRequest
    {
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
        Status = LoanAssignmentStatus.Pending
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LoanAssignmentRequest model)
    {
        if (!ModelState.IsValid) return View(model);
        if (await EnsureOrgIdsAssignableAsync(model.LendingOrgUnitId, model.BorrowingOrgUnitId) is { } denied) return denied;
        model.RequestedBy ??= ParseUserId(User);
        model.Status = LoanAssignmentStatus.Pending;
        db.LoanAssignmentRequests.Add(model);
        await db.SaveChangesAsync();

        foreach (var step in MovementChecklistDefaults.CreateFor(MovementKind.Loan, model.Id))
            db.MovementChecklists.Add(step);
        await db.SaveChangesAsync();
        var recorded = await db.MovementChecklists.FirstAsync(c =>
            c.CorrelationId == model.Id && c.Step == MovementChecklistStep.RequestRecorded);
        MovementWorkflowRules.MarkStep(recorded, ParseUserId(User), $"loan:{model.Id}:recorded");
        await db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل طلب الإعارة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var s = await db.LoanAssignmentRequests
            .Include(x => x.ExecutionRecords)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (await EnsureLoanInScopeAsync(s) is { } denied) return denied;
        ViewBag.Checklist = await db.MovementChecklists.AsNoTracking()
            .Where(c => c.CorrelationId == id && c.MovementKind == MovementKind.Loan)
            .OrderBy(c => c.Step)
            .ToListAsync();
        return View(s);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = HrManagementPolicies.HrManagementApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var s = await db.LoanAssignmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (await EnsureLoanInScopeAsync(s) is { } denied) return denied;
        if (s.IsCancelled || s.Status != LoanAssignmentStatus.Pending)
        {
            TempData["Error"] = "الطلب ليس قيد الانتظار";
            return RedirectToAction(nameof(Details), new { id });
        }

        var by = ParseUserId(User);
        if (by == null)
        {
            TempData["Error"] = "تعذر تحديد هوية المعتمد من التوكن";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (s.RequestedBy.HasValue && by.Value == s.RequestedBy.Value)
        {
            TempData["Error"] = "لا يمكن للمنشئ اعتماد عمله الخاص.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var sod = new HRManagement.SharedKernel.SeparationOfDuties.SeparationOfDutiesService();
        var sodResult = sod.ValidateAction(
            by.Value, null, "LoanAssignmentRequest", s.Id,
            HRManagement.SharedKernel.SeparationOfDuties.SodAction.Check, s.RequestedBy, null);
        if (!sodResult.Allowed)
        {
            TempData["Error"] = sodResult.DenialReason;
            return RedirectToAction(nameof(Details), new { id });
        }

        s.Status = LoanAssignmentStatus.Approved;
        var approved = await db.MovementChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == s.Id && c.Step == MovementChecklistStep.Approved);
        if (approved is not null && !approved.IsCompleted)
            MovementWorkflowRules.MarkStep(approved, by, $"loan:{s.Id}:approved");
        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد الإعارة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(Guid id)
    {
        var s = await db.LoanAssignmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (await EnsureLoanInScopeAsync(s) is { } denied) return denied;
        if (s.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (s.Status != LoanAssignmentStatus.Approved)
        {
            TempData["Error"] = "يجب اعتماد الطلب قبل التنفيذ";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!MovementWorkflowRules.IsEffectiveDateReached(s.StartDate))
        {
            TempData["Error"] = $"لا يمكن تنفيذ الإعارة قبل تاريخ البداية {s.StartDate:yyyy-MM-dd}";
            return RedirectToAction(nameof(Details), new { id });
        }

        var checklist = await db.MovementChecklists
            .Where(c => c.CorrelationId == id && c.MovementKind == MovementKind.Loan)
            .ToListAsync();
        if (!MovementWorkflowRules.PreExecuteChecklistComplete(checklist))
        {
            TempData["Error"] = "أكمل خطوات الحوكمة الإلزامية قبل التنفيذ";
            return RedirectToAction(nameof(Details), new { id });
        }

        var transfers = await db.TransferRequests.AsNoTracking()
            .Where(x => x.EmployeeId == s.EmployeeId && !x.IsCancelled).ToListAsync();
        var secondments = await db.SecondmentRequests.AsNoTracking()
            .Where(x => x.EmployeeId == s.EmployeeId && !x.IsCancelled).ToListAsync();
        var loans = await db.LoanAssignmentRequests.AsNoTracking()
            .Where(x => x.EmployeeId == s.EmployeeId && !x.IsCancelled).ToListAsync();
        if (MovementWorkflowRules.HasConcurrentMovementConflict(s.EmployeeId, s.Id, transfers, secondments, loans))
        {
            TempData["Error"] = "يوجد حركة متزامنة نشطة لنفس الموظف";
            return RedirectToAction(nameof(Details), new { id });
        }

        s.Status = LoanAssignmentStatus.Active;
        var executedAt = DateTime.UtcNow;
        var by = ParseUserId(User);
        db.MovementExecutionRecords.Add(new MovementExecutionRecord
        {
            LoanAssignmentRequestId = s.Id,
            EmployeeId = s.EmployeeId,
            ExecutedAt = executedAt,
            Notes = $"تفعيل إعارة من {s.LendingOrgUnitId} إلى {s.BorrowingOrgUnitId}"
        });
        var executedStep = checklist.FirstOrDefault(c => c.Step == MovementChecklistStep.Executed);
        if (executedStep is not null && !executedStep.IsCompleted)
            MovementWorkflowRules.MarkStep(executedStep, by, $"loan:{s.Id}:executed");
        await db.SaveChangesAsync();

        await publish.Publish<ILoanAssignmentExecuted>(new
        {
            LoanAssignmentRequestId = s.Id,
            s.EmployeeId,
            s.LendingOrgUnitId,
            s.BorrowingOrgUnitId,
            StartDate = s.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EndDate = s.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Movements"
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تنفيذ الإعارة ونشر الحدث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id)
    {
        var s = await db.LoanAssignmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (await EnsureLoanInScopeAsync(s) is { } denied) return denied;
        if (s.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (s.Status == LoanAssignmentStatus.Closed)
        {
            TempData["Error"] = "الطلب مغلق مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }

        s.Status = LoanAssignmentStatus.Closed;
        var ended = await db.MovementChecklists.FirstOrDefaultAsync(c =>
            c.CorrelationId == s.Id && c.Step == MovementChecklistStep.Ended);
        if (ended is not null && !ended.IsCompleted)
            MovementWorkflowRules.MarkStep(ended, ParseUserId(User), $"loan:{s.Id}:ended");

        await publish.Publish<ILoanAssignmentEnded>(new
        {
            LoanAssignmentRequestId = s.Id,
            s.EmployeeId,
            s.LendingOrgUnitId,
            s.BorrowingOrgUnitId,
            EndedAt = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Movements"
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إغلاق الإعارة واستعادة الحالة عبر ILoanAssignmentEnded";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var s = await db.LoanAssignmentRequests.FindAsync(id);
        if (s == null) return NotFound();
        if (await EnsureLoanInScopeAsync(s) is { } denied) return denied;
        if (s.IsCancelled)
        {
            TempData["Error"] = "الطلب ملغى مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (s.Status is LoanAssignmentStatus.Pending or LoanAssignmentStatus.Approved)
        {
            MovementWorkflowRules.SoftCancelLoan(s, reason);
            await db.SaveChangesAsync();
            TempData["Success"] = "تم إلغاء طلب الإعارة";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (s.Status == LoanAssignmentStatus.Active)
        {
            MovementWorkflowRules.SoftCancelLoan(s, reason);
            s.Status = LoanAssignmentStatus.Closed;
            await publish.Publish<ILoanAssignmentEnded>(new
            {
                LoanAssignmentRequestId = s.Id,
                s.EmployeeId,
                s.LendingOrgUnitId,
                s.BorrowingOrgUnitId,
                EndedAt = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid(),
                SourceSystem = "HR.Movements"
            });
            await db.SaveChangesAsync();
            TempData["Success"] = "تم إلغاء الإعارة النشطة مع نشر نهاية الاستعادة";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Error"] = "لا يمكن إلغاء الطلب في حالته الحالية";
        return RedirectToAction(nameof(Details), new { id });
    }
}
