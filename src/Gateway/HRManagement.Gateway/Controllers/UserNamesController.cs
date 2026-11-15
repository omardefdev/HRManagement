using HRManagement.Gateway.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Gateway.Controllers;

/// <summary>
/// PLAN-67 (D-67-3, P-6, G-UX-04): the names of users, by their ids, for the pages that show a person instead of an id
/// (<c>display-names.js</c>, <c>[data-hr-user-id]</c>). For interactive staff only (<see cref="HrManagementPolicies.GatewayUserNames"/>,
/// PLAN-50's definition); at most <see cref="MaxIds"/> ids; the caller's agency only, unless the caller is <c>platform-admin</c>; the name
/// only (no e-mail, no role). An id the registry does not know is simply absent from the answer. The lookup is a POST, so it takes the
/// gateway's antiforgery token (<c>GET api/user-names/token</c>, same policy) in the <c>RequestVerificationToken</c> header.
/// </summary>
[ApiController]
[Route("api/user-names")]
[Authorize(Policy = HrManagementPolicies.GatewayUserNames)]
[OrgScopeNotApplicable("user names, filtered by agency: no org records")]
public sealed class UserNamesController(GatewayAuditDbContext db) : ControllerBase
{
    public const int MaxIds = 50;

    public sealed record UserNamesRequest(IReadOnlyList<string>? Ids);

    public sealed record UserNameItem(string Id, string Label);

    public sealed record UserNamesResponse(IReadOnlyList<UserNameItem> Items);

    public sealed record UserNamesToken(string Token);

    /// <summary>The antiforgery token for <see cref="ByIds"/>; the matching cookie is set on this response.</summary>
    [HttpGet("token")]
    public ActionResult<UserNamesToken> Token([FromServices] IAntiforgery antiforgery) =>
        Ok(new UserNamesToken(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!));

    [HttpPost("by-ids")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<UserNamesResponse>> ByIds([FromBody] UserNamesRequest? request, CancellationToken ct)
    {
        var ids = (request?.Ids ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
            return Ok(new UserNamesResponse([]));
        if (ids.Count > MaxIds)
            return BadRequest($"Maximum {MaxIds} ids per request.");

        var lowered = ids.Select(id => id.ToLowerInvariant()).ToList();
        var query = db.UserNames.AsNoTracking().Where(u => ids.Contains(u.Sub) || lowered.Contains(u.Sub));
        if (!User.IsInRole(HrManagementRoles.PlatformAdmin))
        {
            if (!Guid.TryParse(User.FindFirst(TenantContext.TenantClaimType)?.Value, out var tenant))
                return Forbid();
            query = query.Where(u => u.AgencyId == tenant);
        }

        var rows = await query.Select(u => new UserNameItem(u.Sub, u.DisplayName)).ToListAsync(ct);
        return Ok(new UserNamesResponse(rows));
    }
}
