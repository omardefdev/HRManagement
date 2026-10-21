using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Web.Controllers;

/// <summary>DATA-002-C: request / approve demographic corrections.</summary>
[Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
public class EmployeeCorrectionsController(HRCoreDbContext db, OrgScopeService orgScope) : Controller
{
    public record CorrectionRequestDto(
        Guid EmployeeId,
        string Reason,
        Dictionary<string, JsonElement> Changes);

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? employeeId,
        ApprovalStatus? status,
        bool? requiresLegalApproval,
        DateTime? createdFrom,
        DateTime? createdTo,
        CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        if (employeeId is { } eid)
        {
            var employeeScope = await db.Employees.AsNoTracking()
                .Where(e => e.Id == eid)
                .Select(e => new { e.PrimaryOrgUnitId })
                .FirstOrDefaultAsync(ct);
            if (employeeScope is null)
                return NotFound();

            if (this.ForbidIfOrgUnitOutOfScope(scope, employeeScope.PrimaryOrgUnitId) is { } empDenied)
                return empDenied;
        }

        var q = db.EmployeeCorrections.AsNoTracking().AsQueryable();
        if (employeeId.HasValue)
            q = q.Where(c => c.EmployeeId == employeeId.Value);
        else if (scope.MustFilter)
        {
            var allowedEmployees = db.Employees.AsNoTracking()
                .Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value))
                .Select(e => e.Id);
            q = q.Where(c => allowedEmployees.Contains(c.EmployeeId));
        }

        if (status is ApprovalStatus st)
            q = q.Where(c => c.Status == st);
        if (requiresLegalApproval is bool rla)
            q = q.Where(c => c.RequiresLegalApproval == rla);
        if (createdFrom is DateTime cf)
            q = q.Where(c => c.RequestedAt >= cf);
        if (createdTo is DateTime ct2)
            q = q.Where(c => c.RequestedAt <= ct2);

        string? filterEmployeeName = null;
        if (employeeId is Guid empId)
            filterEmployeeName = await db.Employees.AsNoTracking().Where(e => e.Id == empId).Select(e => e.FullName).FirstOrDefaultAsync(ct);

        var items = await q.OrderByDescending(c => c.RequestedAt).Take(100).ToListAsync(ct);
        return View(new EmployeeCorrectionsIndexViewModel
        {
            Items = items,
            EmployeeId = employeeId,
            FilterEmployeeName = filterEmployeeName,
            Status = status,
            RequiresLegalApproval = requiresLegalApproval,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestCorrection([FromForm] Guid employeeId, [FromForm] string reason, [FromForm] string changesJson, CancellationToken ct)
    {
        Dictionary<string, JsonElement> changes;
        try
        {
            changes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(changesJson ?? "{}")
                      ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "صيغة التغييرات غير صالحة." });
        }

        return await RequestCorrectionCore(employeeId, reason, changes, ct);
    }

    [HttpPost("api/employee-corrections")]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> RequestCorrectionApi([FromBody] CorrectionRequestDto dto, CancellationToken ct) =>
        RequestCorrectionCore(dto.EmployeeId, dto.Reason, dto.Changes ?? new(), ct);

    private async Task<IActionResult> RequestCorrectionCore(
        Guid employeeId,
        string? reason,
        Dictionary<string, JsonElement> changes,
        CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null) return NotFound();

        var actor = ParseUserId(User) ?? Guid.Empty;
        var (correction, error) = EmployeeCorrectionService.CreateRequest(employee, changes, reason, actor);
        if (correction is null)
            return BadRequest(new { error });

        db.EmployeeCorrections.Add(correction);
        await db.SaveChangesAsync(ct);
        return Ok(new { correction.Id, correction.Status, correction.RequiresLegalApproval });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var correction = await db.EmployeeCorrections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (correction is null) return NotFound();

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == correction.EmployeeId, ct);
        if (employee is null) return NotFound();

        if (correction.RequiresLegalApproval)
        {
            // Four-eyes: approver must differ from requester when legal fields are touched.
            var actor = ParseUserId(User) ?? Guid.Empty;
            if (actor != Guid.Empty && actor == correction.RequestedBy)
                return Conflict(new { error = "لا يجوز اعتماد التصحيح القانوني من نفس طالب التصحيح." });
        }

        var history = new List<EmploymentStatusHistory>();
        var approver = ParseUserId(User) ?? Guid.Empty;
        var (ok, error) = EmployeeCorrectionService.ApplyApproved(employee, correction, approver, history);
        if (!ok)
            return BadRequest(new { error });

        foreach (var h in history)
            db.EmploymentStatusHistories.Add(h);

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(Employee),
            EntityId = employee.Id,
            Action = "CorrectionApproved",
            ChangedFields = correction.ChangesJson,
            UserId = approver,
            TenantId = employee.TenantId
        });

        await db.SaveChangesAsync(ct);
        return Ok(new { correction.Id, correction.Status, employee.CurrentStatus });
    }

    [HttpPost("api/employee-corrections/{id:guid}/approve")]
    [IgnoreAntiforgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public Task<IActionResult> ApproveApi(Guid id, CancellationToken ct) => Approve(id, ct);

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
