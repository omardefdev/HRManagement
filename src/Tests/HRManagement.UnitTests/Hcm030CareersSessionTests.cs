using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Middleware;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (PLAN-30 H4): the <c>careers_session</c> cookie (<see cref="CandidatePortalSession"/>: Data Protection payload,
/// absolute expiry, tamper/purpose/version checks, cookie flags and path), the session filter (redirect to login, ids only
/// from the session, session tenant as the ambient tenant) and the minimal <see cref="CandidatePortalController"/> flow
/// (neutral code page, verify → cookie, logout).
/// </summary>
public sealed class Hcm030CareersSessionTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-otp-hmac-key-0123456789abcdef");
    private static readonly Regex SixDigits = new(@"\b\d{6}\b", RegexOptions.None, TimeSpan.FromSeconds(1));

    private readonly EphemeralDataProtectionProvider dataProtection = new();

    // ------------------------------------------------------------------ payload

    [Fact]
    public void Payload_round_trips_and_is_not_readable_without_the_purpose_key()
    {
        var sessions = Sessions();
        var now = DateTime.UtcNow;
        var payload = new CandidatePortalSessionPayload(Guid.NewGuid(), Guid.NewGuid(), now, now.AddMinutes(30));

        var value = sessions.Protect(payload);

        Assert.Equal(payload, sessions.Unprotect(value, now.AddMinutes(1)));
        Assert.DoesNotContain(payload.CitizenApplicationId.ToString(), value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("HR.Core.CareersSession.v1", CandidatePortalSession.Purpose);
        Assert.Equal("careers_session", CandidatePortalSession.CookieName);
        Assert.Equal(TimeSpan.FromMinutes(30), sessions.Lifetime);
    }

    [Fact]
    public void Expired_payload_is_no_session()
    {
        var sessions = Sessions();
        var issued = DateTime.UtcNow.AddMinutes(-31);
        var value = sessions.Protect(new CandidatePortalSessionPayload(Guid.NewGuid(), Guid.NewGuid(), issued, issued.AddMinutes(30)));

        Assert.Null(sessions.Unprotect(value, DateTime.UtcNow));
        // Absolute, not sliding: a still-valid cookie is rejected the moment its expiry passes.
        var fresh = DateTime.UtcNow;
        var valid = sessions.Protect(new CandidatePortalSessionPayload(Guid.NewGuid(), Guid.NewGuid(), fresh, fresh.AddMinutes(30)));
        Assert.NotNull(sessions.Unprotect(valid, fresh.AddMinutes(29)));
        Assert.Null(sessions.Unprotect(valid, fresh.AddMinutes(30)));
    }

    [Fact]
    public void Tampered_foreign_or_overlong_values_are_no_session()
    {
        var sessions = Sessions();
        var now = DateTime.UtcNow;
        var payload = new CandidatePortalSessionPayload(Guid.NewGuid(), Guid.NewGuid(), now, now.AddMinutes(30));
        var value = sessions.Protect(payload);

        var chars = value.ToCharArray();
        var i = chars.Length / 2;
        chars[i] = chars[i] == 'A' ? 'B' : 'A';
        Assert.Null(sessions.Unprotect(new string(chars), now));
        Assert.Null(sessions.Unprotect(value[..^4], now));
        Assert.Null(sessions.Unprotect("not-a-session", now));
        Assert.Null(sessions.Unprotect(null, now));

        // Same JSON under another purpose (or another key ring) does not unprotect.
        var json = JsonSerializer.Serialize(new { V = 1, A = payload.CitizenApplicationId, T = payload.TenantId, I = now, E = now.AddMinutes(30) });
        Assert.Null(sessions.Unprotect(dataProtection.CreateProtector("HR.Core.Other.v1").Protect(json), now));
        Assert.Null(Sessions(new EphemeralDataProtectionProvider()).Unprotect(value, now));

        // Right purpose but another version, empty ids or a lifetime above Recruitment:Portal:SessionMinutes.
        var protector = dataProtection.CreateProtector(CandidatePortalSession.Purpose);
        Assert.NotNull(sessions.Unprotect(protector.Protect(json), now));
        Assert.Null(sessions.Unprotect(protector.Protect(json.Replace("\"V\":1", "\"V\":2")), now));
        Assert.Null(sessions.Unprotect(protector.Protect(
            JsonSerializer.Serialize(new { V = 1, A = Guid.Empty, T = payload.TenantId, I = now, E = now.AddMinutes(30) })), now));
        Assert.Null(sessions.Unprotect(protector.Protect(
            JsonSerializer.Serialize(new { V = 1, A = payload.CitizenApplicationId, T = payload.TenantId, I = now, E = now.AddDays(1) })), now));
    }

    // ------------------------------------------------------------------ cookie

    [Fact]
    public void Issue_sets_an_httponly_strict_cookie_on_the_portal_path_with_an_absolute_expiry()
    {
        var sessions = Sessions();
        var http = new DefaultHttpContext();
        http.Request.PathBase = "/careers";
        http.Request.Scheme = "http";

        var payload = sessions.Issue(http, Guid.NewGuid(), Guid.NewGuid());

        var cookie = SetCookie(http);
        Assert.StartsWith(CandidatePortalSession.CookieName + "=", cookie);
        Assert.Contains("path=/careers", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase); // Development over http: SameAsRequest
        Assert.Equal(TimeSpan.FromMinutes(30), payload.ExpiresAt - payload.IssuedAt);

        var value = Uri.UnescapeDataString(cookie.Split(';')[0][(CandidatePortalSession.CookieName.Length + 1)..]);
        Assert.Equal(payload, sessions.Unprotect(value, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("Production", "http", true)]
    [InlineData("Staging", "http", true)]
    [InlineData("Development", "https", true)]
    [InlineData("Acceptance", "http", false)]
    public void Cookie_is_secure_always_in_production_and_staging_and_same_as_request_elsewhere(string environment, string scheme, bool secure)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = scheme;

        Sessions(environment: environment).Issue(http, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(secure, SetCookie(http).Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("path=/;", SetCookie(http) + ";", StringComparison.OrdinalIgnoreCase); // no path base: HR.Core root
    }

    [Fact]
    public void Clear_expires_the_cookie_on_the_same_path()
    {
        var http = new DefaultHttpContext();
        http.Request.PathBase = "/careers";

        Sessions().Clear(http);

        var cookie = SetCookie(http);
        Assert.StartsWith(CandidatePortalSession.CookieName + "=;", cookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/careers", cookie, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ filter (IDOR rule)

    [Fact]
    public async Task Filter_redirects_to_login_without_a_valid_session()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var filter = new CandidatePortalSessionFilter(Sessions(), db);

        var (noCookie, ranNoCookie) = await RunFilterAsync(filter, new DefaultHttpContext());
        var garbage = new DefaultHttpContext();
        garbage.Request.Headers.Cookie = CandidatePortalSession.CookieName + "=garbage";
        var (bad, ranBad) = await RunFilterAsync(filter, garbage);

        foreach (var (context, ran) in new[] { (noCookie, ranNoCookie), (bad, ranBad) })
        {
            Assert.False(ran);
            var redirect = Assert.IsType<RedirectToActionResult>(context.Result);
            Assert.Equal("Login", redirect.ActionName);
            Assert.Equal("CandidatePortal", redirect.ControllerName);
        }
        Assert.Contains(CandidatePortalSession.CookieName + "=;", SetCookie(garbage));
    }

    [Fact]
    public async Task Filter_resolves_only_the_session_application_and_runs_the_action_in_its_tenant()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, app) = await Hcm030CandidateOtpTests.ApplyAsync(db, NotificationChannel.Email);
        var (other, _) = await Hcm030CandidateOtpTests.ApplyAsync(db, NotificationChannel.Email, "0202020202");
        var sessions = Sessions();
        var http = WithSessionCookie(sessions, citizen.Id, citizen.TenantId);
        // A client-supplied id anywhere in the request is ignored.
        http.Request.QueryString = new QueryString("?id=" + other.Id);
        var routeData = new RouteData();
        routeData.Values["id"] = other.Id.ToString();
        Guid? tenantDuringAction = null;
        TenantIdFlow.Set(Guid.NewGuid()); // as if a client sent another X-Tenant-ID

        var (context, ran) = await RunFilterAsync(
            new CandidatePortalSessionFilter(sessions, db), http, routeData, () => tenantDuringAction = TenantIdFlow.Get());

        var outer = TenantIdFlow.Get();
        TenantIdFlow.Set(null);
        Assert.True(ran);
        Assert.Null(context.Result);
        var portal = http.GetCandidatePortal();
        Assert.NotNull(portal);
        Assert.Equal(citizen.Id, portal!.CitizenApplicationId);
        Assert.Equal(citizen.TenantId, portal.TenantId);
        Assert.Equal(app.Id, portal.RecruitmentApplicationId);
        Assert.Equal(citizen.ReferenceNumber, portal.ReferenceNumber);
        Assert.Equal(citizen.TenantId, tenantDuringAction);
        Assert.NotEqual(citizen.TenantId, outer); // restored afterwards
    }

    [Fact]
    public async Task Filter_rejects_a_session_for_another_tenant_or_an_anonymized_application()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await Hcm030CandidateOtpTests.ApplyAsync(db, NotificationChannel.Email);
        var sessions = Sessions();
        var filter = new CandidatePortalSessionFilter(sessions, db);

        var (wrongTenant, ranWrongTenant) = await RunFilterAsync(filter, WithSessionCookie(sessions, citizen.Id, Guid.NewGuid()));
        Assert.False(ranWrongTenant);
        Assert.IsType<RedirectToActionResult>(wrongTenant.Result);

        var tracked = await db.CitizenApplications.SingleAsync(c => c.Id == citizen.Id);
        tracked.AnonymizedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var (anonymized, ranAnonymized) = await RunFilterAsync(filter, WithSessionCookie(sessions, citizen.Id, citizen.TenantId));
        Assert.False(ranAnonymized);
        Assert.IsType<RedirectToActionResult>(anonymized.Result);
    }

    // ------------------------------------------------------------------ controller flow

    [Fact]
    public async Task Login_post_always_shows_the_code_page_with_the_neutral_message_and_only_the_reference()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await Hcm030CandidateOtpTests.ApplyAsync(db, NotificationChannel.Email);
        var sender = new CapturingSender();
        var controller = Controller(db, Sessions(), sender);

        var known = Assert.IsType<ViewResult>(await controller.Login(
            new CandidatePortalLoginViewModel { ReferenceNumber = citizen.ReferenceNumber, NationalId = "0101010101" }, CancellationToken.None));
        var unknown = Assert.IsType<ViewResult>(await Controller(db, Sessions(), sender).Login(
            new CandidatePortalLoginViewModel { ReferenceNumber = "CA-NOPE", NationalId = "0101010101" }, CancellationToken.None));

        foreach (var view in new[] { known, unknown })
        {
            Assert.Equal("Code", view.ViewName);
            var model = Assert.IsType<CandidatePortalCodeViewModel>(view.Model);
            Assert.Equal(CandidateOtpService.NeutralMessage, model.Notice);
            Assert.Null(model.Code);
        }
        Assert.Equal(citizen.ReferenceNumber, ((CandidatePortalCodeViewModel)known.Model!).ReferenceNumber);
        Assert.Single(sender.Messages);
        Assert.False(controller.HttpContext.Response.Headers.ContainsKey("Set-Cookie"));

        var missing = Assert.IsType<ViewResult>(await Controller(db, Sessions(), sender).Login(new CandidatePortalLoginViewModel(), CancellationToken.None));
        Assert.Null(missing.ViewName);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Verify_issues_the_session_on_success_and_logout_deletes_it()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        var (citizen, _) = await Hcm030CandidateOtpTests.ApplyAsync(db, NotificationChannel.Email);
        var sessions = Sessions();
        var sender = new CapturingSender();
        await Controller(db, sessions, sender).Login(
            new CandidatePortalLoginViewModel { ReferenceNumber = citizen.ReferenceNumber, NationalId = "0101010101" }, CancellationToken.None);
        var code = SixDigits.Match(sender.Messages.Single().Body).Value;

        var wrongController = Controller(db, sessions, sender);
        var wrong = Assert.IsType<ViewResult>(await wrongController.Verify(
            new CandidatePortalCodeViewModel { ReferenceNumber = citizen.ReferenceNumber, Code = code == "000000" ? "000001" : "000000" },
            CancellationToken.None));
        Assert.Equal("Code", wrong.ViewName);
        Assert.False(wrongController.ModelState.IsValid);
        Assert.Null(((CandidatePortalCodeViewModel)wrong.Model!).Code);
        Assert.False(wrongController.HttpContext.Response.Headers.ContainsKey("Set-Cookie"));

        var okController = Controller(db, sessions, sender);
        var ok = Assert.IsType<RedirectToActionResult>(await okController.Verify(
            new CandidatePortalCodeViewModel { ReferenceNumber = citizen.ReferenceNumber, Code = code }, CancellationToken.None));
        Assert.Equal(nameof(CandidatePortalController.Index), ok.ActionName);
        var cookie = SetCookie(okController.HttpContext);
        var value = Uri.UnescapeDataString(cookie.Split(';')[0][(CandidatePortalSession.CookieName.Length + 1)..]);
        var payload = sessions.Unprotect(value, DateTime.UtcNow);
        Assert.Equal(citizen.Id, payload!.CitizenApplicationId);
        Assert.Equal(citizen.TenantId, payload.TenantId);

        var logoutController = Controller(db, sessions, sender);
        var logout = Assert.IsType<RedirectToActionResult>(logoutController.Logout());
        Assert.Equal(nameof(CandidatePortalController.Login), logout.ActionName);
        Assert.StartsWith(CandidatePortalSession.CookieName + "=;", SetCookie(logoutController.HttpContext));
    }

    [Fact]
    public void Portal_actions_follow_the_portal_rules()
    {
        var type = typeof(CandidatePortalController);
        Assert.NotNull(Attribute.GetCustomAttribute(type, typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute)));
        Assert.Equal("my", ((RouteAttribute)Attribute.GetCustomAttribute(type, typeof(RouteAttribute))!).Template);
        Assert.NotNull(Attribute.GetCustomAttribute(type.GetMethod(nameof(CandidatePortalController.Index))!, typeof(CandidatePortalSessionAttribute)));
        foreach (var post in type.GetMethods().Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Length > 0))
            Assert.NotNull(Attribute.GetCustomAttribute(post, typeof(ValidateAntiForgeryTokenAttribute)));
        // The login/verify forms post no application id: only reference + national id, then reference + code.
        Assert.DoesNotContain(typeof(CandidatePortalCodeViewModel).GetProperties(), p => p.Name.EndsWith("Id", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ helpers

    private CandidatePortalSession Sessions(IDataProtectionProvider? provider = null, string environment = "Development") =>
        new(provider ?? dataProtection,
            Options.Create(new RecruitmentOptions()),
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environment));

    private static CandidatePortalController Controller(HRCoreDbContext db, CandidatePortalSession sessions, CapturingSender sender)
    {
        var otp = new CandidateOtpService(
            db,
            new NotificationChannelSenderRegistry([sender], Options.Create(new CandidateNotificationOptions { Provider = "FileDrop" })),
            new CandidateOtpKeyRing(Key),
            Options.Create(new RecruitmentOptions { Portal = { OtpResendSeconds = 0 } }),
            NullLogger<CandidateOtpService>.Instance);
        var controller = new CandidatePortalController(
            otp, sessions, CareersClientIpResolver.FromConfiguration(new ConfigurationBuilder().Build()), portalService: null!);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.20");
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static DefaultHttpContext WithSessionCookie(CandidatePortalSession sessions, Guid citizenApplicationId, Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = CandidatePortalSession.CookieName + "=" + Uri.EscapeDataString(
            sessions.Protect(new CandidatePortalSessionPayload(citizenApplicationId, tenantId, now, now.AddMinutes(30))));
        return http;
    }

    private static async Task<(ActionExecutingContext Context, bool Ran)> RunFilterAsync(
        IAsyncActionFilter filter, HttpContext http, RouteData? routeData = null, Action? action = null)
    {
        var actionContext = new ActionContext(http, routeData ?? new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());
        var ran = false;
        await filter.OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            action?.Invoke();
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()));
        });
        return (executing, ran);
    }

    private static string SetCookie(HttpContext http) =>
        http.Response.Headers.SetCookie.ToString();

    private sealed class CapturingSender : INotificationChannelSender
    {
        public NotificationChannel Channel => NotificationChannel.Email;
        public List<OutboundNotification> Messages { get; } = [];

        public Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.FromResult<ChannelSendResult>(new ChannelSendResult.Sent(null));
        }
    }
}
