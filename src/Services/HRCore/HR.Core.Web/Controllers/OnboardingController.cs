using HR.Core.Infrastructure.Onboarding;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// Agency onboarding from a manifest in the request body. PLAN-51 (§5, G-IAM-09): the platform administrator's alone
/// (<see cref="HrManagementPolicies.PlatformAgencyOnboarding"/>), in the service and at the gateway; no hr-core of any agency. It reads no
/// file from disk over HTTP: <c>apply-file</c> is gone, and <see cref="AgencyManifestLoader"/> serves the scripts and the seeds only.
/// </summary>
[ApiController]
[Route("api/onboarding/agencies")]
[Authorize(Policy = HrManagementPolicies.PlatformAgencyOnboarding)]
// PLAN-51 (§4, P-6): replaces the module filter's name list; the platform administrator holds no HR.Core module role.
[ModulePolicyOverride(HrManagementPolicies.PlatformAgencyOnboarding)]
[OrgScopeCentralOnly]
public class OnboardingController(AgencyProvisioningService provisioning) : ControllerBase
{
    [HttpPost("apply")]
    [Authorize(Policy = HrManagementPolicies.PlatformAgencyOnboarding)]
    public async Task<ActionResult<AgencyProvisioningResult>> Apply(
        [FromBody] AgencyManifest manifest,
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        var result = await provisioning.ApplyManifestAsync(manifest, dryRun, ct);
        return Ok(result);
    }
}
