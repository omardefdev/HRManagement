using System.Security.Claims;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Attendance;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-57 (§1, §4): the attendance periods. The screens list the periods and close a month for a unit's tree (or the whole agency);
/// the API does the same for an operator and for PLAN-59 (<c>POST api/attendance/periods/{yyyy-MM}/close</c>,
/// <c>GET api/attendance/periods/{yyyy-MM}</c>). The class reads (<c>HrCore.ReadOnly</c>); a close names
/// <c>HrCore.AttendanceClose</c> and validates an antiforgery token (<c>GET api/attendance/periods/antiforgery</c> for an API client).
/// <para>
/// Answers of the API: 200 a close done (also when everyone was closed already); 400 a month that cannot be read or has not ended;
/// 403 a unit of the caller's agency outside his scope, or the whole agency asked for by a branch; 404 a unit that is not in the
/// caller's agency; 409 a close refused because working days have no record (<c>RequireComplete</c>), or because another close
/// wrote the same period at the same time. Every refusal writes nothing.
/// </para>
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
[OrgScopeNotApplicable("the unit is checked by AttendancePeriodService (PLAN-57 §1 rule 3): a branch closes and reads its own tree only")]
public class AttendancePeriodsController(
    HRCoreDbContext db,
    AttendancePeriodService periods,
    AttendanceCalendar calendar,
    OrgScopeService orgScope) : Controller
{
    /// <summary>How many employees with missing days the close page and the API list; the total is always given.</summary>
    public const int IncompleteListed = 50;

    public sealed record ClosePeriodRequest(Guid? OrgUnitId, DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null);

    // ---------------------------------------------------------------- the screens

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (actor.Scope.IsDenied) return Forbid();

        var stored = await db.AttendancePeriods.AsNoTracking()
            .OrderByDescending(p => p.PeriodStart)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .Select(p => new { p.PeriodStart, p.PeriodEnd })
            .ToListAsync(ct);
        var rows = new List<AttendancePeriodView>(stored.Count);
        foreach (var period in stored)
            rows.Add(await periods.GetStatusAsync(period.PeriodStart, period.PeriodEnd, actor, ct));

        return View(new AttendancePeriodsIndexViewModel { Periods = rows, UnrecordedMode = periods.UnrecordedMode });
    }

    public async Task<IActionResult> Close(CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (actor.Scope.IsDenied) return Forbid();
        return View(await CloseFormAsync(actor, new AttendancePeriodCloseViewModel { Month = PreviousMonth() }, ct));
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceClose)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(string? month, Guid? orgUnitId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (actor.Scope.IsDenied) return Forbid();
        var form = new AttendancePeriodCloseViewModel { Month = month, OrgUnitId = orgUnitId };

        if (!AttendancePeriodService.TryParseMonth(month, out var start, out var end))
        {
            ModelState.AddModelError(string.Empty, AttendancePeriodMessages.InvalidMonth);
            return View(await CloseFormAsync(actor, form, ct));
        }

        var result = await periods.CloseAsync(start, end, orgUnitId, actor, ct);
        switch (result.Outcome)
        {
            case AttendanceCloseOutcome.UnitNotFound:
                return NotFound();
            case AttendanceCloseOutcome.OutOfScope:
                return Forbid();
            case AttendanceCloseOutcome.Closed:
                var holidays = calendar.HasHolidayIn(start, end) ? string.Empty : " تنبيه: لا عطل رسمية مضبوطة لهذه الفترة.";
                TempData["Success"] = result.ClosedEmployees == 0
                    ? $"لا جديد: كل موظفي النطاق ({result.SkippedAlreadyClosed}) مغلقون في هذه الفترة من قبل."
                    : $"أُغلقت فترة الحضور {month} لـ {result.ClosedEmployees} موظف، ونُشر ملخص كل منهم للرواتب. "
                      + $"تُخطّي {result.SkippedAlreadyClosed} موظف مغلق من قبل.{holidays}";
                return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError(string.Empty, result.Error!);
        if (result.Outcome == AttendanceCloseOutcome.Incomplete)
        {
            var listed = result.Incomplete.Take(IncompleteListed).ToList();
            var ids = listed.Select(i => i.EmployeeId).ToList();
            var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
            form.Incomplete = listed
                .Select(i => new AttendancePeriodCloseViewModel.MissingRow(names.GetValueOrDefault(i.EmployeeId, "—"), i.UnrecordedWorkingDays))
                .ToList();
            form.IncompleteTotal = result.Incomplete.Count;
        }

        form.NoHolidayInPeriod = !calendar.HasHolidayIn(start, end);
        return View(await CloseFormAsync(actor, form, ct));
    }

    // ---------------------------------------------------------------- the API

    [HttpGet("api/attendance/periods/antiforgery")]
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceClose)]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    [HttpPost("api/attendance/periods/{month}/close")]
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceClose)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseApi(
        string month, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ClosePeriodRequest? request, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (actor.Scope.IsDenied) return Forbid();
        if (!AttendancePeriodService.TryParseMonth(month, out var start, out var end))
            return BadRequest(new { error = AttendancePeriodMessages.InvalidMonth });

        // Custom bounds are read only when the agency's mode is Custom (D-LVA-11); otherwise the month in the address is the period.
        if (periods.Mode == AttendancePeriodMode.Custom && request?.PeriodStart is { } customStart && request.PeriodEnd is { } customEnd)
            (start, end) = (customStart, customEnd);

        var result = await periods.CloseAsync(start, end, request?.OrgUnitId, actor, ct);
        return result.Outcome switch
        {
            AttendanceCloseOutcome.Closed => Ok(new
            {
                periodId = result.PeriodId,
                status = (result.Status ?? AttendancePeriodStatus.Open).ToString(),
                closedEmployees = result.ClosedEmployees,
                skippedAlreadyClosed = result.SkippedAlreadyClosed
            }),
            AttendanceCloseOutcome.InvalidPeriod => BadRequest(new { error = result.Error }),
            AttendanceCloseOutcome.UnitNotFound => NotFound(new { error = result.Error }),
            AttendanceCloseOutcome.OutOfScope => Forbid(),
            AttendanceCloseOutcome.Incomplete => Conflict(new
            {
                error = result.Error,
                incomplete = result.Incomplete.Take(IncompleteListed).Select(i => new { employeeId = i.EmployeeId, unrecordedWorkingDays = i.UnrecordedWorkingDays }),
                total = result.Incomplete.Count
            }),
            _ => Conflict(new { error = result.Error })
        };
    }

    [HttpGet("api/attendance/periods/{month}")]
    public async Task<IActionResult> GetApi(string month, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        if (actor.Scope.IsDenied) return Forbid();
        if (!AttendancePeriodService.TryParseMonth(month, out var start, out _))
            return BadRequest(new { error = AttendancePeriodMessages.InvalidMonth });

        var view = await periods.GetStatusAsync(start, actor, ct);
        return Ok(new
        {
            periodId = view.PeriodId,
            periodStart = view.PeriodStart,
            periodEnd = view.PeriodEnd,
            status = view.Status.ToString(),
            closedEmployees = view.ClosedEmployees,
            remainingEmployees = view.RemainingEmployees,
            summaries = view.Summaries.Select(s => new
            {
                summaryId = s.SummaryId,
                employeeId = s.EmployeeId,
                version = s.Version,
                workingDays = s.WorkingDays,
                presentDays = s.PresentDays,
                absentDays = s.AbsentDays,
                onLeaveDays = s.OnLeaveDays,
                remoteDays = s.RemoteDays,
                unrecordedWorkingDays = s.UnrecordedWorkingDays,
                lastReason = s.LastReason.ToString()
            })
        });
    }

    // ---------------------------------------------------------------- helpers

    private string PreviousMonth() => calendar.Today.AddMonths(-1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<AttendanceActor> ActorAsync(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return new AttendanceActor(Guid.TryParse(sub, out var id) ? id : null, await orgScope.GetAllowedOrgUnitIdsAsync(User, ct));
    }

    private async Task<AttendancePeriodCloseViewModel> CloseFormAsync(AttendanceActor actor, AttendancePeriodCloseViewModel form, CancellationToken ct)
    {
        var units = db.OrgUnits.AsNoTracking().AsQueryable();
        if (actor.Scope.MustFilter)
            units = units.Where(o => actor.Scope.AllowedOrgUnitIds.Contains(o.Id));
        form.OrgUnits = new SelectList(
            await units.OrderBy(o => o.NameAr).Take(SearchFilterBase.LookupMaxPageSize).Select(o => new { o.Id, o.NameAr }).ToListAsync(ct),
            "Id", "NameAr", form.OrgUnitId);
        form.CanCloseWholeAgency = actor.Scope.IsUnrestricted;
        form.NoHolidayConfigured = calendar.Holidays.Count == 0;
        form.UnrecordedMode = periods.UnrecordedMode;
        return form;
    }
}
