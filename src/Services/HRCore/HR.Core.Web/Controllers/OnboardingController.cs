using HR.Core.Infrastructure.Onboarding;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

[ApiController]
[Route("api/onboarding/agencies")]
[Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
public class OnboardingController(AgencyProvisioningService provisioning) : ControllerBase
{
    [HttpPost("apply")]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<ActionResult<AgencyProvisioningResult>> Apply(
        [FromBody] AgencyManifest manifest,
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        var result = await provisioning.ApplyManifestAsync(manifest, dryRun, ct);
        return Ok(result);
    }

    [HttpPost("apply-file")]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<ActionResult<AgencyProvisioningResult>> ApplyFromFile(
        [FromQuery] string path,
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        var manifest = AgencyManifestLoader.LoadFromFile(path);
        var result = await provisioning.ApplyManifestAsync(manifest, dryRun, ct);
        return Ok(result);
    }
}
