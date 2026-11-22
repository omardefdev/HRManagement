using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.PayProfileChanges;

namespace Payroll.Web.Controllers;

/// <summary>PLAN-53: what the salary change controllers share: the caller, and the one mapping from a command's answer to HTTP.</summary>
internal static class PayProfileChangeHttp
{
    public static Guid RequireUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    public static string? UserName(ClaimsPrincipal user) =>
        user.FindFirstValue("preferred_username") ?? user.FindFirstValue("name") ?? user.Identity?.Name;

    /// <summary>
    /// The API's answer to a refusal, the same everywhere: 400 for a broken input rule, 409 for a state conflict, 403 for deciding one's
    /// own request, 404 outside the scope or the agency. The body carries the Arabic message and the request's unchanged state.
    /// </summary>
    public static ObjectResult Refusal(PayProfileChangeResult result) =>
        new(new { error = result.Error, requestId = result.RequestId, state = result.State?.ToString() }) { StatusCode = result.StatusCode };

    public static object Body(PayProfileChangeResult result) =>
        new { requestId = result.RequestId, state = result.State?.ToString(), warning = result.Warning };
}
