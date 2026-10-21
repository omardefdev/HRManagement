using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

[Route("api/establishment")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class EstablishmentTemporalApiController(
    EstablishmentAsOfService asOfService,
    IJobCatalogVersionService jobCatalogVersions,
    IFundedPositionVersionService fundedPositionVersions,
    EstablishmentScopeService establishmentScope,
    OrgScopeService orgScope) : ControllerBase
{
    [HttpGet("catalog/{id:guid}/as-of")]
    public async Task<IActionResult> CatalogAsOf(Guid id, [FromQuery] DateTime at, CancellationToken ct)
    {
        if (await establishmentScope.ValidateJobCatalogAccessAsync(User) is not null)
            return Forbid();

        var dto = await asOfService.ResolveCatalogAsOfAsync(id, at, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("positions/{id:guid}/as-of")]
    public async Task<IActionResult> PositionAsOf(Guid id, [FromQuery] DateTime at, CancellationToken ct)
    {
        var scope = await establishmentScope.GetScopeAsync(User);
        if (this.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;

        var dto = await asOfService.ResolvePositionAsOfAsync(id, at, ct);
        if (dto is null) return NotFound();

        if (this.ForbidIfOrgUnitOutOfScope(scope, dto.OrgUnitId, "FundedPosition", id.ToString("D")) is { } unitDenied)
            return unitDenied;

        return Ok(dto);
    }

    [HttpGet("units/{orgUnitId:guid}/positions/as-of")]
    public async Task<IActionResult> UnitPositionsAsOf(Guid orgUnitId, [FromQuery] DateTime at, CancellationToken ct)
    {
        var orgAccess = await orgScope.GetAllowedOrgUnitIdsAsync(User);
        if (orgAccess.IsDenied) return Forbid();
        if (orgAccess.MustFilter && !orgAccess.CanAccess(orgUnitId)) return Forbid();

        var items = await asOfService.ListUnitPositionsAsOfAsync(orgUnitId, at, ct);
        return Ok(new { orgUnitId, asOf = at.ToUniversalTime(), items });
    }

    [HttpGet("positions/{id:guid}/history")]
    public async Task<IActionResult> PositionHistory(Guid id, CancellationToken ct)
    {
        var scope = await establishmentScope.GetScopeAsync(User);
        if (this.ForbidIfOrgScopeDenied(scope) is { } denied)
            return denied;

        var current = await fundedPositionVersions.ResolveAsOfAsync(id, DateTime.UtcNow, ct);
        if (current is null) return NotFound();

        if (this.ForbidIfOrgUnitOutOfScope(scope, current.OrgUnitId, "FundedPosition", id.ToString("D")) is { } unitDenied)
            return unitDenied;

        var history = await fundedPositionVersions.ListHistoryAsync(id, ct);
        return Ok(history);
    }

    [HttpGet("catalog/{id:guid}/history")]
    public async Task<IActionResult> CatalogHistory(Guid id, CancellationToken ct)
    {
        if (await establishmentScope.ValidateJobCatalogAccessAsync(User) is not null)
            return Forbid();

        var current = await jobCatalogVersions.ResolveAsOfAsync(id, DateTime.UtcNow, ct);
        if (current is null) return NotFound();

        var history = await jobCatalogVersions.ListHistoryAsync(id, ct);
        return Ok(history);
    }
}
