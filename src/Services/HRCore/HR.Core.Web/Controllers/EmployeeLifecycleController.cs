using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Web.Controllers;

/// <summary>DATA-002-D: legal hold, anonymize, merge (no hard delete / no auto-purge).</summary>
[Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
[Route("EmployeeLifecycle")]
public class EmployeeLifecycleController(
    HRCoreDbContext db,
    EmployeeIntegrationService integration) : Controller
{
    public record LegalHoldDto(Guid EmployeeId, bool Hold, string? Reason);
    public record MergeDto(Guid SourceEmployeeId, Guid TargetEmployeeId, Guid? DuplicateCaseId);
    public record AnonymizeDto(Guid EmployeeId);
    public record UnmergeDto(Guid SourceEmployeeId);

    [HttpPost("legal-hold")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetLegalHold([FromBody] LegalHoldDto dto, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
        if (employee is null) return NotFound();

        var actor = ParseUserId(User) ?? Guid.Empty;
        var (ok, error) = EmployeeLegalLifecycleService.SetLegalHold(employee, dto.Hold, dto.Reason, actor);
        if (!ok) return BadRequest(new { error });

        await db.SaveChangesAsync(ct);
        return Ok(new { employee.Id, employee.LegalHold, employee.LegalHoldReason });
    }

    [HttpPost("anonymize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Anonymize([FromBody] AnonymizeDto dto, CancellationToken ct)
    {
        var employee = await db.Employees
            .Include(e => e.Contacts)
            .FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
        if (employee is null) return NotFound();

        var oldStatus = employee.CurrentStatus;
        var history = new List<EmploymentStatusHistory>();
        var actor = ParseUserId(User) ?? Guid.Empty;
        var (ok, error) = EmployeeLegalLifecycleService.TryAnonymize(
            employee, employee.Contacts.ToList(), actor, history);
        if (!ok) return Conflict(new { error });

        foreach (var h in history)
            db.EmploymentStatusHistories.Add(h);

        await db.SaveChangesAsync(ct);
        await integration.PublishStatusChangedIfNeededAsync(
            employee, oldStatus, EmploymentStatus.Anonymized, "AnonymizeEmployee", ct);

        return Ok(new { employee.Id, employee.CurrentStatus, employee.FullName });
    }

    [HttpPost("merge")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Merge([FromBody] MergeDto dto, CancellationToken ct)
    {
        var source = await db.Employees.FirstOrDefaultAsync(e => e.Id == dto.SourceEmployeeId, ct);
        var target = await db.Employees.FirstOrDefaultAsync(e => e.Id == dto.TargetEmployeeId, ct);
        if (source is null || target is null) return NotFound();

        var oldStatus = source.CurrentStatus;
        var history = new List<EmploymentStatusHistory>();
        var actor = ParseUserId(User) ?? Guid.Empty;
        var (ok, error) = EmployeeLegalLifecycleService.TryMerge(source, target, actor, history);
        if (!ok) return Conflict(new { error });

        foreach (var h in history)
            db.EmploymentStatusHistories.Add(h);

        if (dto.DuplicateCaseId.HasValue)
        {
            var dup = await db.MasterDataDuplicateCases
                .FirstOrDefaultAsync(c => c.Id == dto.DuplicateCaseId.Value, ct);
            if (dup is not null)
            {
                dup.Status = Domain.Enums.MasterDataDuplicateCaseStatus.Merged;
                dup.ResolvedIntoEmployeeId = target.Id;
                dup.ResolvedAt = DateTime.UtcNow;
                dup.ResolvedBy = actor;
            }
        }

        await db.SaveChangesAsync(ct);
        await integration.PublishStatusChangedIfNeededAsync(
            source, oldStatus, EmploymentStatus.Merged, $"MergedInto={target.Id}", ct);

        return Ok(new
        {
            source.Id,
            source.CurrentStatus,
            source.MergedIntoEmployeeId,
            Note = "GOV-003/DATA-002: merge marks source Merged in HR.Core; see GOV_003_MERGE_RUNBOOK for satellite remaps."
        });
    }

    [HttpPost("unmerge")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Unmerge([FromBody] UnmergeDto dto, CancellationToken ct)
    {
        var source = await db.Employees.FirstOrDefaultAsync(e => e.Id == dto.SourceEmployeeId, ct);
        if (source is null) return NotFound();

        var oldStatus = source.CurrentStatus;
        var history = new List<EmploymentStatusHistory>();
        var actor = ParseUserId(User) ?? Guid.Empty;
        var (ok, error) = EmployeeLegalLifecycleService.TryUnmerge(source, actor, history);
        if (!ok) return Conflict(new { error });

        foreach (var h in history)
            db.EmploymentStatusHistories.Add(h);

        await db.SaveChangesAsync(ct);
        await integration.PublishStatusChangedIfNeededAsync(
            source, oldStatus, EmploymentStatus.Active, "UnmergeEmployee", ct);

        return Ok(new { source.Id, source.CurrentStatus, source.MergedIntoEmployeeId });
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
