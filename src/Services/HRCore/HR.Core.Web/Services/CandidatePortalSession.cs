using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Web.Services;

/// <summary>The content of a <c>careers_session</c> cookie (HCM-030 H4).</summary>
public sealed record CandidatePortalSessionPayload(Guid CitizenApplicationId, Guid TenantId, DateTime IssuedAt, DateTime ExpiresAt);

/// <summary>
/// What a <c>/careers/my</c> action may use: ids resolved from the session cookie and checked against the database. Portal
/// actions load the application and the offer only through these ids — never through a route or form id (IDOR rule).
/// </summary>
public sealed record CandidatePortalContext(
    Guid CitizenApplicationId,
    Guid TenantId,
    Guid? RecruitmentApplicationId,
    string ReferenceNumber,
    DateTime ExpiresAt);

/// <summary>
/// HCM-030 (PLAN-30 H4, D-4): the <c>careers_session</c> cookie of <c>/careers/my</c>. The value is a Data Protection
/// payload (purpose <see cref="Purpose"/>, key ring of <c>AddHrDataProtection("HR.Core")</c>) holding the citizen
/// application, its tenant and an absolute expiry (<c>Recruitment:Portal:SessionMinutes</c>, 30, not sliding). Cookie:
/// HttpOnly, SameSite=Strict, path = the request path base (<c>/careers</c> through the gateway), Secure always in
/// Production and Staging, same-as-request elsewhere (acceptance runs on http://localhost). Anything that does not
/// unprotect, has another version or is past its expiry is no session.
/// </summary>
public sealed class CandidatePortalSession(
    IDataProtectionProvider dataProtection,
    IOptions<RecruitmentOptions> options,
    IHostEnvironment environment)
{
    /// <summary>The gateway forwards exactly this cookie name upstream, and only on the careers route (H5).</summary>
    public const string CookieName = GatewayUpstreamCookieForwarding.CareersSessionCookieName;
    public const string Purpose = "HR.Core.CareersSession.v1";
    private const int PayloadVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private readonly IDataProtector protector = dataProtection.CreateProtector(Purpose);

    public TimeSpan Lifetime => TimeSpan.FromMinutes(Math.Max(1, options.Value.Portal.SessionMinutes));

    public string Protect(CandidatePortalSessionPayload payload) =>
        protector.Protect(JsonSerializer.Serialize(new Wire(
            PayloadVersion, payload.CitizenApplicationId, payload.TenantId, payload.IssuedAt, payload.ExpiresAt), JsonOptions));

    /// <summary>The payload, or null when the value is missing, tampered with, of another version or expired.</summary>
    public CandidatePortalSessionPayload? Unprotect(string? value, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        Wire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<Wire>(protector.Unprotect(value), JsonOptions);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }

        if (wire is null
            || wire.V != PayloadVersion
            || wire.A == Guid.Empty
            || wire.T == Guid.Empty
            || wire.E <= utcNow
            || wire.I > utcNow.AddMinutes(1)
            || wire.E - wire.I > Lifetime)
            return null;

        return new CandidatePortalSessionPayload(wire.A, wire.T, wire.I, wire.E);
    }

    /// <summary>Sets the cookie for a verified candidate (ids from <see cref="CandidateOtpService.VerifyAsync"/>).</summary>
    public CandidatePortalSessionPayload Issue(HttpContext http, Guid citizenApplicationId, Guid tenantId)
    {
        var now = DateTime.UtcNow;
        var payload = new CandidatePortalSessionPayload(citizenApplicationId, tenantId, now, now.Add(Lifetime));
        http.Response.Cookies.Append(CookieName, Protect(payload), CookieOptionsFor(http, payload.ExpiresAt));
        return payload;
    }

    public CandidatePortalSessionPayload? Read(HttpContext http) =>
        Unprotect(http.Request.Cookies[CookieName], DateTime.UtcNow);

    /// <summary>«خروج»: deletes the cookie (same path and flags as when it was set).</summary>
    public void Clear(HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, CookieOptionsFor(http, expiresAt: null));

    public CookieOptions CookieOptionsFor(HttpContext http, DateTime? expiresAt) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = environment.IsProduction() || environment.IsStaging() || http.Request.IsHttps,
        Path = CookiePath(http.Request),
        IsEssential = true,
        Expires = expiresAt is DateTime e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null
    };

    /// <summary><c>/careers</c> through the gateway (HR.Core sees it as the path base); <c>/</c> when HR.Core runs without one.</summary>
    public static string CookiePath(HttpRequest request) =>
        request.PathBase.HasValue ? request.PathBase.Value! : "/";

    /// <summary>Compact wire form (short names keep the cookie small).</summary>
    private sealed record Wire(int V, Guid A, Guid T, DateTime I, DateTime E);
}

/// <summary>
/// HCM-030 (H4): resolves the <c>careers_session</c> cookie for a <c>/careers/my</c> action. Without a valid session (or
/// when the application is gone or anonymized) it clears the cookie and redirects to <c>/careers/my/login</c>. With one,
/// it stores the <see cref="CandidatePortalContext"/> for the action and runs the action with the session's tenant as the
/// ambient tenant, so a client-sent <c>X-Tenant-ID</c> never wins.
/// </summary>
public sealed class CandidatePortalSessionFilter(CandidatePortalSession sessions, HRCoreDbContext db) : IAsyncActionFilter
{
    public const string ItemKey = "HCM030.CandidatePortal";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var payload = sessions.Read(http);
        var previousTenant = TenantIdFlow.Get();
        try
        {
            CandidatePortalContext? portal = null;
            if (payload is not null)
            {
                TenantIdFlow.Set(payload.TenantId);
                var application = await db.CitizenApplications
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                    .AsNoTracking()
                    .Where(a => a.Id == payload.CitizenApplicationId && a.TenantId == payload.TenantId && a.AnonymizedAt == null)
                    .Select(a => new { a.PromotedRecruitmentApplicationId, a.ReferenceNumber })
                    .FirstOrDefaultAsync(http.RequestAborted);
                if (application is not null)
                    portal = new CandidatePortalContext(
                        payload.CitizenApplicationId, payload.TenantId, application.PromotedRecruitmentApplicationId,
                        application.ReferenceNumber, payload.ExpiresAt);
            }

            if (portal is null)
            {
                if (http.Request.Cookies.ContainsKey(CandidatePortalSession.CookieName))
                    sessions.Clear(http);
                context.Result = new RedirectToActionResult("Login", "CandidatePortal", null);
                return;
            }

            http.Items[ItemKey] = portal;
            await next();
        }
        finally
        {
            TenantIdFlow.Set(previousTenant);
        }
    }
}

/// <summary>Puts <see cref="CandidatePortalSessionFilter"/> on a <c>/careers/my</c> action or controller.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class CandidatePortalSessionAttribute() : TypeFilterAttribute(typeof(CandidatePortalSessionFilter));

public static class CandidatePortalHttpContextExtensions
{
    /// <summary>The session of the current <c>/careers/my</c> request (set by <see cref="CandidatePortalSessionFilter"/>).</summary>
    public static CandidatePortalContext? GetCandidatePortal(this HttpContext http) =>
        http.Items.TryGetValue(CandidatePortalSessionFilter.ItemKey, out var value) ? value as CandidatePortalContext : null;
}
