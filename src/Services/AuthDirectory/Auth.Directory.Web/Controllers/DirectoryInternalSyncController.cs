using Auth.Directory.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.Directory.Web.Controllers;

[Route("api/internal/directory")]
[ApiController]
[Authorize(Policy = ServicePolicies.DirectorySync)]
public class DirectoryInternalSyncController(IDirectoryEntrySyncService sync) : ControllerBase
{
    public record SyncEmployeeRequest(Guid EmployeeId, string FullName, string? NationalId, Guid? PrimaryOrgUnitId);

    [HttpPost("sync-employee")]
    public async Task<IActionResult> SyncEmployee([FromBody] SyncEmployeeRequest request, CancellationToken cancellationToken)
    {
        await sync.SyncEmployeeCreatedAsync(
            request.EmployeeId,
            request.FullName,
            request.NationalId,
            request.PrimaryOrgUnitId,
            cancellationToken);
        return Ok();
    }
}
