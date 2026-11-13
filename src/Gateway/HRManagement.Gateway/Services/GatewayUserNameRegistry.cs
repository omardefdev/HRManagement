using System.Security.Claims;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.Gateway.Services;

/// <summary>
/// PLAN-67 (D-67-3, P-6): writes the signed-in user's name into <see cref="GatewayAuditDbContext.UserNames"/>: the token's <c>sub</c>,
/// its <c>name</c> (else <c>preferred_username</c>) and its <c>tenant_id</c>. Once per user and per hour (or when the name changes), so a
/// session that began before a deploy is recorded at its next request. A service account is never recorded. A failure is logged and
/// never fails the request.
/// </summary>
public sealed class GatewayUserNameRegistry(IServiceScopeFactory scopes, IMemoryCache cache, ILogger<GatewayUserNameRegistry> logger)
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(1);

    public static (string Sub, string Name, Guid? TenantId)? Read(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true || ServiceCallerPrincipal.IsServiceAccount(user))
            return null;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username") ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(name) || sub.Length > 128)
            return null;
        Guid? tenant = Guid.TryParse(user.FindFirstValue(TenantContext.TenantClaimType), out var t) ? t : null;
        return (sub.Trim(), name.Trim().Length > 256 ? name.Trim()[..256] : name.Trim(), tenant);
    }

    public async Task RecordAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (Read(user) is not { } who)
            return;
        var cacheKey = "ux067:user-name:" + who.Sub;
        if (cache.TryGetValue(cacheKey, out string? known) && known == who.Name + "|" + who.TenantId)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
            var now = DateTime.UtcNow;
            var row = await db.UserNames.FirstOrDefaultAsync(u => u.Sub == who.Sub, ct);
            if (row is null)
            {
                db.UserNames.Add(new GatewayUserName { Sub = who.Sub, DisplayName = who.Name, TenantId = who.TenantId, FirstSeenAtUtc = now, LastSeenAtUtc = now });
            }
            else
            {
                row.DisplayName = who.Name;
                row.TenantId = who.TenantId;
                row.LastSeenAtUtc = now;
            }

            await db.SaveChangesAsync(ct);
            cache.Set(cacheKey, who.Name + "|" + who.TenantId, RefreshEvery);
        }
        catch (DbUpdateException)
        {
            // Two first requests of the same user at once: the other one recorded the row.
            cache.Set(cacheKey, who.Name + "|" + who.TenantId, RefreshEvery);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("The user-name registry could not record a sign-in ({ExceptionType}); the page goes on.", ex.GetType().Name);
        }
    }
}

/// <summary>PLAN-67 (P-6): runs the registry on every authenticated request; the registry's cache keeps it to once an hour per user.</summary>
public sealed class GatewayUserNameRegistryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GatewayUserNameRegistry registry)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            await registry.RecordAsync(context.User, context.RequestAborted);
        await next(context);
    }
}
