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
/// PLAN-30 I: «طلبي» and the candidate actions are in <see cref="CandidatePortalService"/> (session tenant, actor Candidate);
/// a refused post shows the page again with the errors, a successful one redirects with a message code (<c>?done=</c>).
/// </summary>
[AllowAnonymous]
[Route("my")]
public class CandidatePortalController(
    CandidateOtpService otp,
    CandidatePortalSession sessions,
    CareersClientIpResolver clientIp,
    CandidatePortalService portalService) : Controller
{
    /// <summary>Success messages after a post-redirect-get (no TempData on the portal; the code carries no data).</summary>
    public static readonly IReadOnlyDictionary<string, string> DoneMessages = new Dictionary<string, string>
    {
        ["accepted"] = "تم قبول العرض. ستتواصل معك جهة التوظيف لاستكمال إجراءات التعيين.",
        ["declined"] = "تم تسجيل اعتذارك عن العرض.",
        ["withdrawn"] = "تم سحب طلبك.",
        ["appealed"] = "تم تقديم الاستئناف، وستُبلَّغ بنتيجته.",
        ["info"] = "تم إرسال المعلومات المطلوبة إلى مراجع الاستئناف.",
        ["uploaded"] = "تم رفع المستندات، وستخضع للفحص قبل اعتمادها."
    };

    /// <summary>HCM-030 (PLAN-30 I): «طلبي». <paramref name="step"/> = <c>withdraw</c> opens the withdrawal confirm panel.</summary>
    [HttpGet("")]
    [CandidatePortalSession]
    public async Task<IActionResult> Index(string? done, string? step, CancellationToken ct)
    {
        var page = await portalService.GetPageAsync(Portal, ct);
        page.Notice = done is not null && DoneMessages.TryGetValue(done, out var message) ? message : null;
        page.ShowWithdrawConfirm = page.CanWithdraw && string.Equals(step, "withdraw", StringComparison.Ordinal);
        return View(page);
    }

    /// <summary>Accepts the session's offer (checkbox + full name typed to match).</summary>
    [HttpPost("offer/accept")]
    [ValidateAntiForgeryToken]
    [CandidatePortalSession]
    public async Task<IActionResult> AcceptOffer(CandidatePortalAcceptForm form, CancellationToken ct)
    {
        var result = await portalService.AcceptOfferAsync(Portal, form.Acknowledge, form.FullName, ct);
        return await AfterAsync(result, "accepted", "Accept", posted => posted.AcceptFullName = form.FullName, ct);
    }

    /// <summary>Declines the session's offer with a reason.</summary>
    [HttpPost("offer/decline")]
    [ValidateAntiForgeryToken]
    [CandidatePortalSession]
    public async Task<IActionResult> DeclineOffer(CandidatePortalTextForm form, CancellationToken ct)
    {
        var result = await portalService.DeclineOfferAsync(Portal, form.Text, ct);
        return await AfterAsync(result, "declined", "Decline", posted => posted.DeclineReason = form.Text, ct);
    }

    /// <summary>«تنزيل خطاب العرض» — the PDF of the session's Issued/Accepted offer.</summary>
    [HttpGet("offer/letter")]
    [CandidatePortalSession]
    public async Task<IActionResult> OfferLetter(CancellationToken ct)
    {
        var letter = await portalService.GetOfferLetterAsync(Portal, ct);
        if (letter is not { } file)
            return RedirectToAction(nameof(Index));
        Response.Headers.CacheControl = "no-store";
        return File(file.Pdf, "application/pdf", file.FileName);
    }

    /// <summary>Withdraws the application (second step of the in-page confirm panel).</summary>
    [HttpPost("withdraw")]
    [ValidateAntiForgeryToken]
    [CandidatePortalSession]
    public async Task<IActionResult> Withdraw(CandidatePortalWithdrawForm form, CancellationToken ct)
    {
        var result = await portalService.WithdrawAsync(Portal, form.Reason, form.Confirm, ct);
        return await AfterAsync(result, "withdrawn", "Withdraw", posted => posted.WithdrawReason = form.Reason, ct, reopenWithdraw: true);
    }

    /// <summary>Files an appeal while the window is open (checked again by the service).</summary>
    [HttpPost("appeal")]
    [ValidateAntiForgeryToken]
    [CandidatePortalSession]
    public async Task<IActionResult> Appeal(CandidatePortalTextForm form, CancellationToken ct)
    {
        var result = await portalService.FileAppealAsync(Portal, form.Text, ct);
        return await AfterAsync(result, "appealed", "Appeal", posted => posted.AppealReason = form.Text, ct);
    }

    /// <summary>Answers the reviewer's information request on the pending appeal.</summary>
    [HttpPost("appeal/info")]
    [ValidateAntiForgeryToken]
    [CandidatePortalSession]
    public async Task<IActionResult> AppealInfo(CandidatePortalTextForm form, CancellationToken ct)
    {
        var result = await portalService.AnswerAppealInfoRequestAsync(Portal, form.Text, ct);
        return await AfterAsync(result, "info", "AppealInfo", posted => posted.AppealInfo = form.Text, ct);
    }

    /// <summary>Uploads missing required documents while intake is open (session 2 limits).</summary>
    [HttpPost("documents")]
    [ValidateAntiForgeryToken]
    [CandidatePortalSession]
    public async Task<IActionResult> Documents(CandidatePortalDocumentsForm form, CancellationToken ct)
    {
        var result = await portalService.UploadDocumentsAsync(Portal, form.AttachedFiles(), ct);
        return await AfterAsync(result, "uploaded", "Documents", _ => { }, ct);
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

    /// <summary>The session of this request (set by <see cref="CandidatePortalSessionAttribute"/>).</summary>
    private CandidatePortalContext Portal => HttpContext.GetCandidatePortal()!;

    /// <summary>
    /// Success: redirect to «طلبي» with a message code. Refusal: the page again with the errors under
    /// <c>{form}.{field}</c> (<c>{form}</c> alone for the whole form) and the typed text.
    /// </summary>
    private async Task<IActionResult> AfterAsync(
        CandidatePortalActionResult result,
        string done,
        string form,
        Action<CandidatePortalPostedValues> keepPosted,
        CancellationToken ct,
        bool reopenWithdraw = false)
    {
        if (result.Success)
            return RedirectToAction(nameof(Index), new { done });

        ModelState.Clear();
        foreach (var (field, error) in result.Errors)
            ModelState.AddModelError(string.IsNullOrEmpty(field) ? form : $"{form}.{field}", error);
        var page = await portalService.GetPageAsync(Portal, ct);
        keepPosted(page.Posted);
        page.ShowWithdrawConfirm = reopenWithdraw && page.CanWithdraw;
        return View(nameof(Index), page);
    }

    private ViewResult CodeView(CandidatePortalCodeViewModel vm)
    {
        // The view never echoes a typed code back.
        vm.Code = null;
        vm.Notice = null;
        return View("Code", vm);
    }
}
