using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR.Core.Infrastructure.Services.Legal;
using HRManagement.ServiceDefaults;

namespace HR.Core.Web.Controllers;

[ApiController]
[Route("api/internal/service-regime")]
[Authorize(Policy = ServicePolicies.HrCoreLookupS2S)]
public sealed class ServiceRegimeInternalController(
    ServiceRegimePayrollBlockService blockService,
    ServiceRegimeActiveRegimeService activeRegimeService) : ControllerBase
{
    public sealed record PayrollBlockersRequest(IReadOnlyList<Guid> EmployeeIds);

    public sealed record PayrollBlockerDto(Guid EmployeeId, bool Blocked, string? ReasonCode);

    public sealed record ActiveRegimesRequest(IReadOnlyList<Guid> EmployeeIds);

    public sealed record ActiveRegimeDto(Guid EmployeeId, string? LegalRegimeId, Guid? EmploymentRecordId);

    [HttpPost("payroll-blockers")]
    public async Task<ActionResult<object>> GetPayrollBlockers(
        [FromBody] PayrollBlockersRequest request,
        CancellationToken ct)
    {
        var blockers = await blockService.GetBlockersAsync(request.EmployeeIds ?? [], ct);
        return Ok(new
        {
            blockers = blockers.Select(b => new PayrollBlockerDto(b.EmployeeId, b.Blocked, b.ReasonCode)).ToList()
        });
    }

    [HttpPost("active-regimes")]
    public async Task<ActionResult<object>> GetActiveRegimes(
        [FromBody] ActiveRegimesRequest request,
        CancellationToken ct)
    {
        var regimes = await activeRegimeService.GetActiveRegimesAsync(request.EmployeeIds ?? [], ct);
        return Ok(new
        {
            regimes = regimes.Select(r => new ActiveRegimeDto(r.EmployeeId, r.LegalRegimeId, r.EmploymentRecordId)).ToList()
        });
    }
}
