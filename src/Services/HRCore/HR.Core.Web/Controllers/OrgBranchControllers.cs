using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Attendance;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[OrgScoped(typeof(HrStatisticReport))]
public class HrStatisticReportsController(HRCoreDbContext db) : Controller
{
    // PLAN-52: a branch user's pick-list holds his scope's units only.
    private async Task<SelectList> OrgUnitsAsync(Guid? selected = null) =>
        new(await (await db.OrgUnits.AsNoTracking().InOrgScopeAsync(HttpContext, db))
            .OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", selected);

    public async Task<IActionResult> Index(Guid? orgUnitId, DateOnly? periodFrom, DateOnly? periodTo, string? q)
    {
        var query = await db.HrStatisticReports.AsNoTracking().Include(r => r.OrgUnit).AsQueryable().InOrgScopeAsync(HttpContext, db);
        if (orgUnitId is Guid ou)
            query = query.Where(r => r.OrgUnitId == ou);
        if (periodFrom is DateOnly pf)
            query = query.Where(r => r.PeriodEnd >= pf);
        if (periodTo is DateOnly pt)
            query = query.Where(r => r.PeriodStart <= pt);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(r =>
                r.Title.Contains(term) ||
                (r.MetricsSummary != null && r.MetricsSummary.Contains(term)));
        }

        var items = await query.OrderByDescending(x => x.PeriodEnd).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        ViewBag.OrgUnits = await OrgUnitsAsync(orgUnitId);
        return View(new HrStatisticReportsIndexViewModel { Items = items, OrgUnitId = orgUnitId, PeriodFrom = periodFrom, PeriodTo = periodTo, Q = q });
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.OrgUnits = await OrgUnitsAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return View(new HrStatisticReportCreateViewModel { PeriodStart = today, PeriodEnd = today });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HrStatisticReportCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.OrgUnits = await OrgUnitsAsync(vm.OrgUnitId);
            return View(vm);
        }
        db.HrStatisticReports.Add(new HrStatisticReport
        {
            Title = vm.Title,
            PeriodStart = vm.PeriodStart,
            PeriodEnd = vm.PeriodEnd,
            // PLAN-52 (D-52-2): a restricted user's report without a unit takes his scope root; the filter refused a foreign one.
            OrgUnitId = vm.OrgUnitId ?? await HttpContext.GetRestrictedRootAsync(),
            MetricsSummary = vm.MetricsSummary
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل التقرير الإحصائي";
        return RedirectToAction(nameof(Index));
    }
}

[OrgScoped(typeof(DailyAttendanceRecord))]
public class DailyAttendanceRecordsController(
    HRCoreDbContext db,
    AttendanceDayWriter writer,
    AttendanceLockService locks,
    AttendanceCalendar calendar,
    HR.Core.Infrastructure.OrgScopeService orgScope) : Controller
{
    public async Task<IActionResult> Index(Guid? employeeId, DateOnly? dateFrom, DateOnly? dateTo, DailyAttendanceStatus? status)
    {
        var query = db.DailyAttendanceRecords.AsNoTracking().Include(a => a.Employee).AsQueryable();
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (scope.IsDenied) return Forbid();
        if (scope.MustFilter)
            query = query.Where(a => a.Employee != null && a.Employee.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(a.Employee.PrimaryOrgUnitId.Value));

        if (employeeId is Guid eid)
            query = query.Where(a => a.EmployeeId == eid);
        if (dateFrom is DateOnly df)
            query = query.Where(a => a.AttendanceDate >= df);
        if (dateTo is DateOnly dt)
            query = query.Where(a => a.AttendanceDate <= dt);
        if (status is DailyAttendanceStatus st)
            query = query.Where(a => a.Status == st);

        string? filterEmployeeName = null;
        if (employeeId is Guid empId)
            filterEmployeeName = await db.Employees.AsNoTracking().Where(e => e.Id == empId).Select(e => e.FullName).FirstOrDefaultAsync();

        var items = await query.OrderByDescending(x => x.AttendanceDate).Take(SearchFilterBase.MaxPageSize).ToListAsync();

        // PLAN-57 (§2): a day in a closed period offers a correction request instead of an edit; one query for the page.
        var lockedIds = new HashSet<Guid>();
        if (items.Count > 0)
        {
            var locked = await locks.GetLockedDaysAsync(
                items.Select(i => i.EmployeeId).Distinct().ToList(), items.Min(i => i.AttendanceDate), items.Max(i => i.AttendanceDate));
            foreach (var item in items.Where(i => locked.IsLocked(i.EmployeeId, i.AttendanceDate)))
                lockedIds.Add(item.Id);
        }

        return View(new DailyAttendanceRecordsIndexViewModel
        {
            Items = items,
            LockedIds = lockedIds,
            EmployeeId = employeeId,
            FilterEmployeeName = filterEmployeeName,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Status = status
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var record = await db.DailyAttendanceRecords.AsNoTracking().Include(a => a.Employee).FirstOrDefaultAsync(x => x.Id == id);
        if (record is null) return NotFound();
        // PLAN-52 (AC-IAM-52a): the org-scope filter answered 404 before this line for a record, or an employee (Create, Edit before and
        // after the change), outside the scope.
        ViewBag.Locked = await locks.IsLockedAsync(record.EmployeeId, record.AttendanceDate);
        return View(record);
    }

    public IActionResult Create() =>
        View(new DailyAttendanceRecordCreateViewModel
        {
            AttendanceDate = calendar.Today,
            Status = DailyAttendanceStatus.Present
        });

    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DailyAttendanceRecordCreateViewModel vm)
    {
        if (!ModelState.IsValid)
            return await CreateFormAsync(vm);

        var result = await writer.CreateManualAsync(await ActorAsync(), vm.EmployeeId, vm.AttendanceDate, vm.Status, vm.Notes);
        if (Refusal(result) is { } refused)
            return refused;
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return await CreateFormAsync(vm);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            // Another request wrote the same day between the check and the save: the unique index (employee, date) refused this one.
            ModelState.AddModelError(string.Empty, AttendanceMessages.DuplicateDay);
            return await CreateFormAsync(vm);
        }

        TempData["Success"] = "تم تسجيل الحضور";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var record = await db.DailyAttendanceRecords.AsNoTracking().Include(a => a.Employee).FirstOrDefaultAsync(x => x.Id == id);
        if (record is null) return NotFound();
        // PLAN-52 (AC-IAM-52a): the org-scope filter answered 404 before this line for a record, or an employee (Create, Edit before and
        // after the change), outside the scope.
        if (record.Source == AttendanceRecordSource.Leave)
        {
            TempData["Error"] = AttendanceMessages.LeaveMadeRecord;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (await locks.IsLockedAsync(record.EmployeeId, record.AttendanceDate))
        {
            TempData["Error"] = AttendanceMessages.LockedDay;
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewBag.Record = record;
        return View(new DailyAttendanceRecordEditViewModel
        {
            Id = record.Id,
            // «في إجازة» is not offered by hand: an older record that holds it must take another status to be saved.
            Status = record.Status == DailyAttendanceStatus.OnLeave ? DailyAttendanceStatus.Present : record.Status,
            Notes = record.Notes
        });
    }

    /// <summary>
    /// PLAN-57 (§2, rule 2): the edit changes the status and the note. The view model binds no employee and no date, so a posted
    /// <c>EmployeeId</c> or <c>AttendanceDate</c> is ignored and the record stays its owner's.
    /// </summary>
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, DailyAttendanceRecordEditViewModel vm)
    {
        var record = await db.DailyAttendanceRecords.AsNoTracking().Include(a => a.Employee).FirstOrDefaultAsync(x => x.Id == id);
        if (record is null) return NotFound();
        // PLAN-52 (AC-IAM-52a): the org-scope filter answered 404 before this line for a record, or an employee (Create, Edit before and
        // after the change), outside the scope.
        if (!ModelState.IsValid)
        {
            ViewBag.Record = record;
            return View(vm);
        }

        var result = await writer.UpdateManualAsync(await ActorAsync(), id, vm.Status, vm.Notes);
        if (Refusal(result) is { } refused)
            return refused;
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            ViewBag.Record = record;
            vm.Id = id;
            return View(vm);
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث سجل الحضور";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        // PLAN-52 (AC-IAM-52a): the org-scope filter answered 404 before this line for a record outside the scope.
        var result = await writer.DeleteManualAsync(await ActorAsync(), id);
        if (Refusal(result) is { } refused)
            return refused;
        if (!result.Ok)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف سجل الحضور";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> BulkEntry()
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (scope.IsDenied) return Forbid();
        var units = db.OrgUnits.AsQueryable();
        if (scope.MustFilter)
            units = units.Where(o => scope.AllowedOrgUnitIds.Contains(o.Id));
        ViewBag.OrgUnits = new SelectList(await units.OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr");
        return View();
    }

    /// <summary>
    /// PLAN-57 (§2, rule 4): fills a month for the unit's tree: working days only, nothing after today (Damascus), never
    /// «في إجازة»; locked days and days that have a record are skipped, and the page says how many of each.
    /// </summary>
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkEntry(Guid orgUnitId, string month, DailyAttendanceStatus defaultStatus)
    {
        var actor = await ActorAsync();
        if (actor.Scope.IsDenied || !actor.Scope.CanAccess(orgUnitId)) return Forbid();

        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var monthStart))
        {
            TempData["Error"] = "صيغة الشهر غير صالحة";
            return RedirectToAction(nameof(BulkEntry));
        }

        var result = await writer.BulkFillAsync(actor, orgUnitId, monthStart, defaultStatus);
        if (result.Refusal == AttendanceRefusal.NotFound) return NotFound();
        if (result.Refusal == AttendanceRefusal.OutOfScope) return Forbid();
        if (!result.Ok)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(BulkEntry));
        }

        await db.SaveChangesAsync();
        TempData["Success"] =
            $"الإدخال الجماعي: {result.Employees} موظف، كُتب {result.Written} يوم عمل، "
            + $"وتُخطّي {result.SkippedLocked} يوم لأنه ضمن فترة مغلقة و{result.SkippedExisting} يوم لأن له سجلاً.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>PLAN-57 (§1): the old close is gone; its address leads to the attendance periods' close page.</summary>
    public IActionResult ClosePeriod() => RedirectToAction("Close", "AttendancePeriods");

    private async Task<AttendanceActor> ActorAsync()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return new AttendanceActor(Guid.TryParse(sub, out var id) ? id : null, await orgScope.GetAllowedOrgUnitIdsAsync(User));
    }

    /// <summary>The refusals that are an HTTP answer and not a message on the form: 404 for what is not there, 403 outside the scope.</summary>
    private IActionResult? Refusal(AttendanceWriteResult result) => result.Refusal switch
    {
        AttendanceRefusal.NotFound => NotFound(),
        AttendanceRefusal.OutOfScope => Forbid(),
        _ => null
    };

    private async Task<IActionResult> CreateFormAsync(DailyAttendanceRecordCreateViewModel vm)
    {
        if (vm.EmployeeId != Guid.Empty)
            ViewBag.EmployeeName = await db.Employees.AsNoTracking().Where(e => e.Id == vm.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync();
        return View(nameof(Create), vm);
    }
}

[OrgScoped(typeof(FieldInspectionVisit))]
public class FieldInspectionVisitsController(HRCoreDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? status, DateOnly? visitFrom, DateOnly? visitTo, string? q)
    {
        var query = await db.FieldInspectionVisits.AsNoTracking().AsQueryable().InOrgScopeAsync(HttpContext, db);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(v => v.Status == status);
        if (visitFrom is DateOnly vf)
            query = query.Where(v => v.VisitDate >= vf);
        if (visitTo is DateOnly vt)
            query = query.Where(v => v.VisitDate <= vt);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(v =>
                v.Title.Contains(term) ||
                (v.Location != null && v.Location.Contains(term)) ||
                (v.Summary != null && v.Summary.Contains(term)));
        }

        var items = await query.OrderByDescending(x => x.VisitDate).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(new FieldInspectionVisitsIndexViewModel { Items = items, Status = status, VisitFrom = visitFrom, VisitTo = visitTo, Q = q });
    }

    // PLAN-52: a branch user's pick-list holds his scope's units only.
    private async Task<SelectList> OrgUnitsAsync(Guid? selected = null) =>
        new(await (await db.OrgUnits.AsNoTracking().InOrgScopeAsync(HttpContext, db))
            .OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", selected);

    public async Task<IActionResult> Create()
    {
        ViewBag.OrgUnits = await OrgUnitsAsync();
        return View(new FieldInspectionVisitCreateViewModel { VisitDate = DateOnly.FromDateTime(DateTime.UtcNow) });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FieldInspectionVisitCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.OrgUnits = await OrgUnitsAsync(vm.OrgUnitId);
            return View(vm);
        }
        db.FieldInspectionVisits.Add(new FieldInspectionVisit
        {
            Title = vm.Title,
            Location = vm.Location,
            VisitDate = vm.VisitDate,
            LeadInspectorId = vm.LeadInspectorId,
            Summary = vm.Summary,
            Status = "planned",
            // PLAN-52 (D-52-2): the restricted creator's scope root, else the central user's choice (empty = central).
            OrgUnitId = vm.OrgUnitId ?? await HttpContext.GetRestrictedRootAsync()
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل زيارة التفتيش";
        return RedirectToAction(nameof(Index));
    }
}

[OrgScoped(typeof(DiwanCorrespondence))]
public class DiwanCorrespondencesController(HRCoreDbContext db, HR.Core.Infrastructure.Integration.DiwanIntegrationService integration) : Controller
{
    public async Task<IActionResult> Index(
        CorrespondenceDirection? direction,
        string? q,
        DateTime? registeredFrom,
        DateTime? registeredTo,
        Guid? relatedEmployeeId)
    {
        var query = await db.DiwanCorrespondences.AsNoTracking().Include(c => c.RelatedEmployee).AsQueryable().InOrgScopeAsync(HttpContext, db);
        if (direction is CorrespondenceDirection dir)
            query = query.Where(c => c.Direction == dir);
        if (registeredFrom is DateTime rf)
            query = query.Where(c => c.RegisteredAt >= rf);
        if (registeredTo is DateTime rt)
            query = query.Where(c => c.RegisteredAt <= rt);
        if (relatedEmployeeId is Guid reid)
            query = query.Where(c => c.RelatedEmployeeId == reid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c =>
                c.ReferenceNumber.Contains(term) ||
                c.Subject.Contains(term) ||
                (c.ExternalParty != null && c.ExternalParty.Contains(term)));
        }

        string? filterEmployeeName = null;
        if (relatedEmployeeId is Guid empId)
            filterEmployeeName = await db.Employees.AsNoTracking().Where(e => e.Id == empId).Select(e => e.FullName).FirstOrDefaultAsync();

        var items = await query.OrderByDescending(x => x.RegisteredAt).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        return View(new DiwanCorrespondencesIndexViewModel
        {
            Items = items,
            Direction = direction,
            Q = q,
            RegisteredFrom = registeredFrom,
            RegisteredTo = registeredTo,
            RelatedEmployeeId = relatedEmployeeId,
            FilterEmployeeName = filterEmployeeName
        });
    }

    // PLAN-52: a branch user's pick-lists hold his scope's employees and units only.
    private async Task PopulateAsync(Guid? orgUnitId = null)
    {
        ViewBag.Employees = new SelectList(
            await (await db.Employees.AsNoTracking().InOrgScopeAsync(HttpContext, db))
                .OrderBy(e => e.FullName).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "FullName");
        ViewBag.OrgUnits = new SelectList(
            await (await db.OrgUnits.AsNoTracking().InOrgScopeAsync(HttpContext, db))
                .OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "NameAr", orgUnitId);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateAsync();
        return View(new DiwanCorrespondenceCreateViewModel { Direction = CorrespondenceDirection.Inbound });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DiwanCorrespondenceCreateViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateAsync(vm.OrgUnitId);
            return View(vm);
        }
        // PLAN-52 (D-52-2): the related employee's unit, else the unit chosen on the form (the filter refused one outside a restricted
        // creator's scope), else a restricted creator's scope root; a central user who chose nothing makes a central record.
        // The filter refused a foreign employee before this line.
        var orgUnitId = vm.RelatedEmployeeId is Guid related
            ? await db.Employees.AsNoTracking().Where(e => e.Id == related).Select(e => e.PrimaryOrgUnitId).FirstOrDefaultAsync()
            : null;
        orgUnitId ??= vm.OrgUnitId ?? await HttpContext.GetRestrictedRootAsync();
        await integration.RegisterCorrespondenceAsync(new DiwanCorrespondence
        {
            OrgUnitId = orgUnitId,
            ReferenceNumber = vm.ReferenceNumber,
            Subject = vm.Subject,
            Direction = vm.Direction,
            ExternalParty = vm.ExternalParty,
            Body = vm.Body,
            RelatedEmployeeId = vm.RelatedEmployeeId,
            RegisteredAt = DateTime.UtcNow
        });
        TempData["Success"] = "تم تسجيل المكاتبة";
        return RedirectToAction(nameof(Index));
    }
}
