using System.Security.Claims;
using HR.Core.Domain.Entities;
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
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// PLAN-57 (§3): corrections of locked attendance days. A person who enters attendance asks for one with a reason
/// (<c>HrCore.AttendanceWrite</c>, the employee in his scope); a central person who is not the requester approves or rejects it
/// (<c>HrCore.AttendanceCorrectionApprove</c>). The screens and the API (<c>POST api/attendance/corrections</c>,
/// <c>…/{id}/approve</c>, <c>…/{id}/reject</c>) call the same service; every write validates an antiforgery token
/// (<c>GET api/attendance/corrections/antiforgery</c> for an API client).
/// <para>
/// Answers of the API: 200 done; 400 a broken rule (a reason under 15 characters, the same status, «في إجازة» by hand, a day that
/// is not locked, a rejection with no note) and the requester deciding his own request; 403 an employee of the caller's agency
/// outside his scope, and a branch-scoped caller on a decision (the policy); 404 a correction or an employee that is not in the
/// caller's agency or scope; 409 a second request waiting for the same day, and a correction decided already. Every refusal writes nothing.
/// </para>
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
[OrgScoped(typeof(AttendanceCorrection))]
public class AttendanceCorrectionsController(
    HRCoreDbContext db,
    AttendanceCorrectionService corrections,
    AttendanceLockService locks,
    OrgScopeService orgScope) : Controller
{
    /// <summary>The stance of the two decisions (D-LVA-4): the policy refuses a branch-scoped caller, and so does the org scope.</summary>
    private const string CentralDecision = "PLAN-57 (D-LVA-4): an attendance correction is decided by a central user only; a branch asks and never decides";

    public sealed record CorrectionRequest(Guid EmployeeId, DateOnly AttendanceDate, string? NewStatus, string? Reason);

    public sealed record DecisionRequest(string? Note);

    // ---------------------------------------------------------------- the screens

    public async Task<IActionResult> Index(AttendanceCorrectionStatus? status, bool mine = false, CancellationToken ct = default)
    {
        var query = await db.AttendanceCorrections.AsNoTracking().Include(c => c.Employee).AsQueryable().InOrgScopeAsync(HttpContext, db);
        var shown = status ?? (mine ? null : AttendanceCorrectionStatus.Pending);
        if (shown is { } wanted)
            query = query.Where(c => c.Status == wanted);
        var me = UserId();
        if (mine)
            query = query.Where(c => c.RequestedBy == me);

        var items = await query.OrderByDescending(c => c.RequestedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync(ct);
        return View(new AttendanceCorrectionsIndexViewModel
        {
            Items = items,
            Status = shown,
            Mine = mine,
            CanDecide = CanDecide(),
            CurrentUserId = me
        });
    }

    public async Task<IActionResult> Create(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        // PLAN-52: the org-scope filter answered 404 before this line for an employee outside the scope.
        var form = new AttendanceCorrectionCreateViewModel { EmployeeId = employeeId, AttendanceDate = date, NewStatus = DailyAttendanceStatus.Present };
        return await CreateFormAsync(form, ct);
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AttendanceCorrectionCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return await CreateFormAsync(vm, ct);

        var result = await corrections.RequestAsync(await ActorAsync(ct), vm.EmployeeId, vm.AttendanceDate, vm.NewStatus, vm.Reason, ct);
        switch (result.Outcome)
        {
            case AttendanceCorrectionOutcome.NotFound:
                return NotFound();
            case AttendanceCorrectionOutcome.OutOfScope:
                return Forbid();
            case AttendanceCorrectionOutcome.Done:
                TempData["Success"] = "قُدّم طلب التصحيح، وهو بانتظار قرار مستخدم مركزي غير مقدّمه.";
                return RedirectToAction(nameof(Details), new { id = result.Correction!.Id });
        }

        ModelState.AddModelError(string.Empty, result.Error!);
        return await CreateFormAsync(vm, ct);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var correction = await db.AttendanceCorrections.AsNoTracking().Include(c => c.Employee).Include(c => c.Period).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (correction is null) return NotFound();
        // PLAN-52: the org-scope filter answered 404 before this line for a correction outside the scope.
        var me = UserId();
        return View(new AttendanceCorrectionDetailsViewModel
        {
            Correction = correction,
            CanDecide = CanDecide() && correction.Status == AttendanceCorrectionStatus.Pending && correction.RequestedBy != me,
            IsOwnRequest = correction.RequestedBy is not null && correction.RequestedBy == me
        });
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceCorrectionApprove)]
    [OrgScopeCentralOnly(CentralDecision)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, string? note, CancellationToken ct) =>
        Decided(id, await corrections.ApproveAsync(await ActorAsync(ct), id, note, ct), "اعتُمد التصحيح، ونُشرت نسخة جديدة من ملخص الحضور للرواتب.");

    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceCorrectionApprove)]
    [OrgScopeCentralOnly(CentralDecision)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? note, CancellationToken ct) =>
        Decided(id, await corrections.RejectAsync(await ActorAsync(ct), id, note, ct), "رُفض طلب التصحيح، ولم يتغير اليوم.");

    private IActionResult Decided(Guid id, AttendanceCorrectionResult result, string done)
    {
        switch (result.Outcome)
        {
            case AttendanceCorrectionOutcome.NotFound:
                return NotFound();
            case AttendanceCorrectionOutcome.OutOfScope:
                return Forbid();
            case AttendanceCorrectionOutcome.Done:
                TempData["Success"] = done;
                break;
            default:
                TempData["Error"] = result.Error;
                break;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------- the API

    [HttpGet("api/attendance/corrections/antiforgery")]
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceWrite)]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    [HttpPost("api/attendance/corrections")]
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceWrite)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestApi([FromBody] CorrectionRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<DailyAttendanceStatus>(request.NewStatus, ignoreCase: true, out var status) || !Enum.IsDefined(status))
            return BadRequest(new { error = AttendanceMessages.UnknownStatus });
        return ToResponse(await corrections.RequestAsync(await ActorAsync(ct), request.EmployeeId, request.AttendanceDate, status, request.Reason, ct));
    }

    [HttpPost("api/attendance/corrections/{id:guid}/approve")]
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceCorrectionApprove)]
    [OrgScopeCentralOnly(CentralDecision)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveApi(
        Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DecisionRequest? request, CancellationToken ct) =>
        ToResponse(await corrections.ApproveAsync(await ActorAsync(ct), id, request?.Note, ct));

    [HttpPost("api/attendance/corrections/{id:guid}/reject")]
    [Authorize(Policy = HrManagementPolicies.HrCoreAttendanceCorrectionApprove)]
    [OrgScopeCentralOnly(CentralDecision)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectApi(
        Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DecisionRequest? request, CancellationToken ct) =>
        ToResponse(await corrections.RejectAsync(await ActorAsync(ct), id, request?.Note, ct));

    private IActionResult ToResponse(AttendanceCorrectionResult result) => result.Outcome switch
    {
        AttendanceCorrectionOutcome.Done => Ok(new
        {
            id = result.Correction!.Id,
            status = result.Correction.Status.ToString(),
            resultingVersion = result.Correction.ResultingVersion
        }),
        AttendanceCorrectionOutcome.NotFound => NotFound(new { error = result.Error }),
        AttendanceCorrectionOutcome.OutOfScope => Forbid(),
        AttendanceCorrectionOutcome.Conflict => Conflict(new { error = result.Error }),
        // A broken rule and a separation-of-duties refusal answer 400, as the payroll run's acts do (PLAN-56).
        _ => BadRequest(new { error = result.Error })
    };

    // ---------------------------------------------------------------- helpers

    private Guid? UserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private bool CanDecide() =>
        (User.IsInRole(HrManagementRoles.Admin) || User.IsInRole(HrManagementRoles.HrCore)) && !HrOrgScopeVisibility.IsBranchScoped(User);

    private async Task<AttendanceActor> ActorAsync(CancellationToken ct) =>
        new(UserId(), await orgScope.GetAllowedOrgUnitIdsAsync(User, ct),
            User.FindFirstValue("name") ?? User.FindFirstValue("preferred_username") ?? User.Identity?.Name);

    private async Task<IActionResult> CreateFormAsync(AttendanceCorrectionCreateViewModel form, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == form.EmployeeId).Select(e => new { e.FullName }).FirstOrDefaultAsync(ct);
        if (employee is null) return NotFound();
        var record = await db.DailyAttendanceRecords.AsNoTracking()
            .Where(r => r.EmployeeId == form.EmployeeId && r.AttendanceDate == form.AttendanceDate)
            .Select(r => new { r.Status, r.Source })
            .FirstOrDefaultAsync(ct);
        form.EmployeeName = employee.FullName;
        form.CurrentStatus = record?.Status;
        form.LeaveMade = record?.Source == AttendanceRecordSource.Leave;
        form.Locked = await locks.IsLockedAsync(form.EmployeeId, form.AttendanceDate, ct);
        return View(nameof(Create), form);
    }
}
