using HR.Core.Infrastructure.Middleware;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-030 (PLAN-30 H, D-4): candidate self-service at <c>/careers/my</c> (gateway route <c>careers-public</c>; HR.Core sees
/// <c>/careers</c> as the path base). Sign-in is a one-time code sent to the contact on file
/// (<see cref="CandidateOtpService"/>), then the <c>careers_session</c> cookie (<see cref="CandidatePortalSession"/>).
/// Every signed-in action carries <see cref="CandidatePortalSessionAttribute"/> and reads the application only from
/// <see cref="CandidatePortalHttpContextExtensions.GetCandidatePortal"/> — never from a route, query or form id.
/// Anti-forgery on every POST; no TempData (the gateway drops its cookie); no PII in URLs or logs.
/// </summary>
[AllowAnonymous]
[Route("my")]
public class CandidatePortalController(
    CandidateOtpService otp,
    CandidatePortalSession sessions,
    CareersClientIpResolver clientIp) : Controller
{
    [HttpGet("")]
    [CandidatePortalSession]
    public IActionResult Index()
    {
        var portal = HttpContext.GetCandidatePortal()!;
        return View(portal);
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        if (sessions.Read(HttpContext) is not null)
            return RedirectToAction(nameof(Index));
        return View(new CandidatePortalLoginViewModel());
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicCareersRateLimitExtensions.OtpPolicyName)]
    [CareersOtpRateLimit(CareersOtpOperation.Request)]
    public async Task<IActionResult> Login(CandidatePortalLoginViewModel vm, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vm.ReferenceNumber))
            ModelState.AddModelError(nameof(vm.ReferenceNumber), "الرقم المرجعي مطلوب.");
        if (string.IsNullOrWhiteSpace(vm.NationalId))
            ModelState.AddModelError(nameof(vm.NationalId), "الرقم الوطني مطلوب.");
        if (!ModelState.IsValid)
        {
            vm.NationalId = null;
            return View(vm);
        }

        var result = await otp.RequestAsync(vm.ReferenceNumber, vm.NationalId, clientIp.Resolve(HttpContext)?.ToString(), ct);
        // Always the same page and message, whatever happened (no account enumeration).
        return View("Code", new CandidatePortalCodeViewModel
        {
            ReferenceNumber = vm.ReferenceNumber!.Trim(),
            Notice = result.Message
        });
    }

    [HttpPost("verify")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicCareersRateLimitExtensions.OtpPolicyName)]
    [CareersOtpRateLimit(CareersOtpOperation.Verify)]
    public async Task<IActionResult> Verify(CandidatePortalCodeViewModel vm, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vm.ReferenceNumber))
            return RedirectToAction(nameof(Login));
        if (CandidateOtpService.NormalizeCode(vm.Code) is null)
        {
            ModelState.AddModelError(nameof(vm.Code), "يرجى إدخال رمز التحقق المكوّن من 6 أرقام.");
            return CodeView(vm);
        }

        var result = await otp.VerifyAsync(vm.ReferenceNumber, vm.Code, clientIp.Resolve(HttpContext)?.ToString(), ct);
        if (result is { Succeeded: true, CitizenApplicationId: Guid applicationId, TenantId: Guid tenantId })
        {
            sessions.Issue(HttpContext, applicationId, tenantId);
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError(nameof(vm.Code), result.Message ?? CandidateOtpService.InvalidCodeMessage);
        return CodeView(vm);
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        sessions.Clear(HttpContext);
        return RedirectToAction(nameof(Login));
    }

    private ViewResult CodeView(CandidatePortalCodeViewModel vm)
    {
        // The view never echoes a typed code back.
        vm.Code = null;
        vm.Notice = null;
        return View("Code", vm);
    }
}
