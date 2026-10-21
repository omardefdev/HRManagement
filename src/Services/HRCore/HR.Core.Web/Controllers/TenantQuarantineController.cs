using System.Security.Claims;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Tenancy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[ApiController]
[Route("api/tenant-quarantine")]
[Authorize(Policy = HrManagementPolicies.TenantQuarantineReconcile)]
public sealed class TenantQuarantineController(HRCoreDbContext db, ITenantContext tenantContext) : ControllerBase
{
    public sealed record QuarantineEntryDto(
        Guid Id,
        string ServiceName,
        string TableName,
        Guid RecordId,
        string Reason,
        string? Resolution,
        Guid? AssignedTenantId,
        DateTime CreatedAt,
        DateTime? ResolvedAt);

    public sealed record ResolveQuarantineRequest
    {
        public string Resolution { get; init; } = "";
        public Guid? AssignedTenantId { get; init; }
        public string Reason { get; init; } = "";
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<QuarantineEntryDto>>> List(
        [FromQuery] bool resolved = false,
        CancellationToken ct = default)
    {
        var query = db.TenantQuarantineEntries.AsNoTracking().AsQueryable();
        query = resolved ? query.Where(e => e.ResolvedAt != null) : query.Where(e => e.ResolvedAt == null);

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new QuarantineEntryDto(
                e.Id, e.ServiceName, e.TableName, e.RecordId, e.Reason,
                e.Resolution, e.AssignedTenantId, e.CreatedAt, e.ResolvedAt))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuarantineEntryDto>> Get(Guid id, CancellationToken ct = default)
    {
        var entry = await db.TenantQuarantineEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null) return NotFound();

        return Ok(new QuarantineEntryDto(
            entry.Id, entry.ServiceName, entry.TableName, entry.RecordId, entry.Reason,
            entry.Resolution, entry.AssignedTenantId, entry.CreatedAt, entry.ResolvedAt));
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<QuarantineEntryDto>> Resolve(
        Guid id,
        [FromBody] ResolveQuarantineRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Reason is required for audit.");

        var entry = await db.TenantQuarantineEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null) return NotFound();
        if (entry.ResolvedAt is not null)
            return Conflict(new { error = "Quarantine entry already resolved." });

        var resolution = request.Resolution.Trim();
        if (resolution.Equals("AssignTenant", StringComparison.OrdinalIgnoreCase))
        {
            if (request.AssignedTenantId is null)
                return BadRequest("AssignedTenantId is required for AssignTenant.");

            if (!await db.Tenants.AnyAsync(t => t.Id == request.AssignedTenantId.Value, ct))
                return BadRequest("AssignedTenantId does not match an active tenant.");

            if (entry.ServiceName == TenantQuarantineGuard.ServiceName
                && entry.TableName == "Employees")
            {
                var employee = await db.Employees
                    .IgnoreTenantFilterForPlatformReconciliation(tenantContext)
                    .FirstOrDefaultAsync(e => e.Id == entry.RecordId, ct);
                if (employee is null)
                    return NotFound(new { error = "Employee record not found for quarantine entry." });

                employee.TenantId = request.AssignedTenantId.Value;
            }

            entry.AssignedTenantId = request.AssignedTenantId;
            entry.Resolution = $"AssignTenant: {request.Reason.Trim()}";
        }
        else if (resolution.Equals("KeepPlatform", StringComparison.OrdinalIgnoreCase))
        {
            entry.Resolution = $"KeepPlatform: {request.Reason.Trim()}";
        }
        else if (resolution.Equals("Rollback", StringComparison.OrdinalIgnoreCase))
        {
            entry.Resolution = $"Rollback: {request.Reason.Trim()}";
        }
        else
        {
            return BadRequest("Resolution must be AssignTenant, KeepPlatform, or Rollback.");
        }

        entry.ResolvedByUserId = ParseUserId(User);
        entry.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new QuarantineEntryDto(
            entry.Id, entry.ServiceName, entry.TableName, entry.RecordId, entry.Reason,
            entry.Resolution, entry.AssignedTenantId, entry.CreatedAt, entry.ResolvedAt));
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
