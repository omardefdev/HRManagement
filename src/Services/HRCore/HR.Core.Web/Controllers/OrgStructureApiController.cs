using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.OrgStructure;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Route("api/org-structure")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class OrgStructureApiController(
    HRCoreDbContext db,
    IOrgUnitVersionService versionService,
    OrgScopeService orgScope,
    OrgUnitPathService pathService,
    IOrgUnitHierarchyCache hierarchyCache,
    HR.Core.Infrastructure.Integration.OrgUnitIntegrationService integration) : ControllerBase
{
    [HttpGet("units/{id:guid}/as-of")]
    public async Task<IActionResult> GetAsOf(Guid id, [FromQuery] DateTime at, CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (scope.IsDenied) return Forbid();
        if (scope.MustFilter && !scope.CanAccess(id)) return Forbid();

        var dto = await versionService.ResolveAsOfAsync(id, at.ToUniversalTime(), ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPut("units/{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> UpdateUnit(Guid id, [FromBody] UpdateOrgUnitStructureRequest request, CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (scope.IsDenied) return Forbid();
        if (scope.MustFilter && !scope.CanAccess(id)) return Forbid();

        var orgUnit = await db.OrgUnits.FindAsync([id], ct);
        if (orgUnit is null) return NotFound();

        var beforeName = orgUnit.NameAr;
        var beforeHead = orgUnit.HeadEmployeeId;
        var beforeParent = orgUnit.ParentId;
        var effectiveFrom = (request.EffectiveFrom ?? DateTime.UtcNow).ToUniversalTime();

        orgUnit.NameAr = request.NameAr ?? orgUnit.NameAr;
        orgUnit.NameEn = request.NameEn ?? orgUnit.NameEn;
        if (request.HeadEmployeeId.HasValue || request.ClearHeadEmployee == true)
            orgUnit.HeadEmployeeId = request.ClearHeadEmployee == true ? null : request.HeadEmployeeId;

        var parentChanged = request.ParentId.HasValue && orgUnit.ParentId != request.ParentId;
        if (request.ParentId.HasValue)
            orgUnit.ParentId = request.ParentId;

        if (parentChanged && beforeParent != orgUnit.ParentId)
        {
            // PLAN-40 (class B): one transaction for the fields, the subtree move, the events and the version.
            var (ok, error) = await OutboxUnitOfWork.ExecuteAsync(db, async token =>
            {
                await db.SaveChangesAsync(token);
                var (moved, moveError, affected) = await pathService.MoveSubtreeAsync(orgUnit.Id, orgUnit.ParentId, token);
                if (!moved)
                    return (false, moveError);

                await integration.PublishManyOrgUnitsChangedAsync(affected, token);
                await db.SaveChangesAsync(token);
                await db.Entry(orgUnit).ReloadAsync(token);
                await versionService.AppendVersionAsync(orgUnit, OrgOperationType.Move, effectiveFrom, cancellationToken: token);
                return (true, (string?)null);
            }, ct);
            if (!ok) return BadRequest(new { error });
            hierarchyCache.InvalidateTenant(orgUnit.TenantId);
        }
        else
        {
            var renamed = beforeName != orgUnit.NameAr || beforeHead != orgUnit.HeadEmployeeId || request.NameEn is not null;
            await OutboxUnitOfWork.ExecuteAsync(db, async token =>
            {
                await integration.PublishOrgUnitChangedAsync(orgUnit, ct: token);
                if (renamed)
                {
                    await versionService.AppendVersionAsync(
                        orgUnit, OrgOperationType.Rename, effectiveFrom, cancellationToken: token);
                }
            }, ct);
            hierarchyCache.InvalidateTenant(orgUnit.TenantId);
        }

        return Ok(orgUnit);
    }
}

public sealed class UpdateOrgUnitStructureRequest
{
    public string? NameAr { get; init; }
    public string? NameEn { get; init; }
    public Guid? HeadEmployeeId { get; init; }
    public bool? ClearHeadEmployee { get; init; }
    public Guid? ParentId { get; init; }
    public DateTime? EffectiveFrom { get; init; }
}
