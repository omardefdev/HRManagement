using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.PayrollRuns.Lifecycle;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-56 (P-4): one rule for the HTTP answers of the acts of a run's life, on every controller: done 200; a missing note or
/// reason, or a state the act cannot leave 400; a caller the run's state does not admit 403; another agency's run, job or request
/// 404; a request that lost to another 409. Every refusal carries its Arabic message and changes nothing.
/// </summary>
internal static class PayrollRunActHttp
{
    public static int StatusOf(RunActOutcome outcome) => outcome switch
    {
        RunActOutcome.Done => StatusCodes.Status200OK,
        RunActOutcome.NotFound => StatusCodes.Status404NotFound,
        RunActOutcome.Forbidden => StatusCodes.Status403Forbidden,
        RunActOutcome.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>The API's answer: the outcome's status, with the refusal's message or the id the act made.</summary>
    public static IActionResult Answer(RunActResult result, object? body = null) =>
        result.Success
            ? new OkObjectResult(body ?? new { id = result.Id })
            : new ObjectResult(new { error = result.Error }) { StatusCode = StatusOf(result.Outcome) };

    /// <summary>
    /// D-PAY-18: what the caller's policies say of him, for the handler to apply to the run's state: whether he may cancel a run that
    /// was not sent for review (<c>Payroll.Process</c>), and one that was (<c>Payroll.Approve</c>).
    /// </summary>
    public static async Task<(bool MayProcess, bool MayApprove)> CancelRightsAsync(IAuthorizationService authorization, ClaimsPrincipal user) =>
        ((await authorization.AuthorizeAsync(user, HrManagementPolicies.PayrollProcess)).Succeeded,
         (await authorization.AuthorizeAsync(user, HrManagementPolicies.PayrollApprove)).Succeeded);
}
