using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.ServiceDefaults.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>AC-26: hash-based employee discovery + role-aware national ID in API responses.</summary>
[Route("api/employees")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreLookup)]
public class EmployeesApiController(
    HRCoreDbContext context,
    HR.Core.Infrastructure.OrgScopeService orgScope,
    TenantQuarantineGuard quarantineGuard) : ControllerBase
{
    public sealed record EmployeeLookupDto(
        Guid Id,
        string FullName,
        string DisplayNationalId,
        Guid? PrimaryOrgUnitId,
        string? OrgUnitName);

    [HttpGet("by-national-id-hash")]
    public async Task<ActionResult<EmployeeLookupDto>> GetByNationalIdHash(
        [FromQuery] string hash,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
            return BadRequest("hash must be a 64-character SHA-256 hex value.");

        var normalizedHash = hash.Trim().ToLowerInvariant();
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
            return Forbid();

        var query = context.Employees.AsNoTracking()
            .Include(e => e.PrimaryOrgUnit)
            .Where(e => e.NationalIdHash == normalizedHash);

        if (scope.MustFilter)
            query = query.Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value));

        var employee = await query.FirstOrDefaultAsync(ct);
        if (employee is null) return NotFound();

        return Ok(ToDto(employee));
    }

    [HttpGet("search")]
    public async Task<ActionResult<EmployeeLookupDto>> SearchByNationalId(
        [FromQuery] string nationalId,
        CancellationToken ct = default)
    {
        if (!NationalIdSearch.LooksLikeNationalId(nationalId))
            return BadRequest("nationalId must match a national identifier pattern.");

        var hash = NationalIdSearch.ComputeHash(nationalId);
        return await GetByNationalIdHash(hash, ct);
    }

    private EmployeeLookupDto ToDto(HR.Core.Domain.Entities.Employee employee) =>
        new(
            employee.Id,
            employee.FullName,
            EmployeeNationalIdPresentation.ResolveDisplayNationalId(employee, User),
            employee.PrimaryOrgUnitId,
            employee.PrimaryOrgUnit?.NameAr);

    public sealed record PatchEmployeeRequest(string? Notes);

    /// <summary>AC-08: lightweight mutation endpoint for quarantine acceptance (notes-only patch).</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchEmployeeRequest request, CancellationToken ct = default)
    {
        try
        {
            await quarantineGuard.ThrowIfQuarantinedAsync("Employees", id, ct);
        }
        catch (QuarantinedRecordException ex)
        {
            return Conflict(new { errorCode = QuarantinedRecordException.ErrorCode, error = ex.Message });
        }

        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
            return Forbid();

        var employee = await context.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return NotFound();

        if (scope.MustFilter
            && (!employee.PrimaryOrgUnitId.HasValue
                || !scope.AllowedOrgUnitIds.Contains(employee.PrimaryOrgUnitId.Value)))
            return Forbid();

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            employee.MiddleName = request.Notes.Trim();
            employee.FullName = $"{employee.FirstName} {employee.MiddleName} {employee.LastName}".Replace("  ", " ").Trim();
            await context.SaveChangesAsync(ct);
        }

        return Ok(ToDto(employee));
    }
}
