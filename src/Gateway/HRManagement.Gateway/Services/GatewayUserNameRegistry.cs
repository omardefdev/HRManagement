using System.Security.Claims;
using System.Threading.Channels;
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
/// session that began before a deploy is recorded at its next request. A service account is never recorded.
/// A request never waits for the database: <see cref="Queue"/> hands the sighting to <see cref="GatewayUserNameWriter"/>, which writes it.
/// A failed write is logged and tried again a minute later, so an unreachable audit database costs no page anything.
/// </summary>
public sealed class GatewayUserNameRegistry(IServiceScopeFactory scopes, IMemoryCache cache, ILogger<GatewayUserNameRegistry> logger)
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(1);

    /// <summary>A sighting is queued at most this often while its write has not succeeded.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly Channel<UserNameSighting> _queue = Channel.CreateBounded<UserNameSighting>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<UserNameSighting> Pending => _queue.Reader;

    public static UserNameSighting? Read(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true || ServiceCallerPrincipal.IsServiceAccount(user))
            return null;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username") ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(name) || sub.Length > 128)
            return null;
        Guid? tenant = Guid.TryParse(user.FindFirstValue(TenantContext.TenantClaimType), out var t) ? t : null;
        return new UserNameSighting(sub.Trim(), name.Trim().Length > 256 ? name.Trim()[..256] : name.Trim(), tenant);
    }

    private static string WrittenKey(string sub) => "ux067:user-name:written:" + sub;

    private static string QueuedKey(string sub) => "ux067:user-name:queued:" + sub;

    /// <summary>For the request path: queues the user's sighting unless it is written already or queued in the last minute; never waits.</summary>
    public void Queue(ClaimsPrincipal user)
    {
        if (Read(user) is not { } who)
            return;
        if (cache.TryGetValue(WrittenKey(who.Sub), out string? written) && written == who.Value)
            return;
        if (cache.TryGetValue(QueuedKey(who.Sub), out string? queued) && queued == who.Value)
            return;
        cache.Set(QueuedKey(who.Sub), who.Value, RetryAfter);
        _queue.Writer.TryWrite(who);
    }

    /// <summary>Writes the user's sighting now (the writer, and the tests).</summary>
    public async Task RecordAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (Read(user) is { } who)
            await WriteAsync(who, ct);
    }

    /// <summary>Inserts or updates the row; false when the database could not be reached or refused the write (logged).</summary>
    public async Task<bool> WriteAsync(UserNameSighting who, CancellationToken ct)
    {
        if (cache.TryGetValue(WrittenKey(who.Sub), out string? known) && known == who.Value)
            return true;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
            var now = DateTime.UtcNow;
            var row = await db.UserNames.FirstOrDefaultAsync(u => u.Sub == who.Sub, ct);
            if (row is null)
            {
                db.UserNames.Add(new GatewayUserName { Sub = who.Sub, DisplayName = who.Name, AgencyId = who.TenantId, FirstSeenAtUtc = now, LastSeenAtUtc = now });
            }
            else
            {
                row.DisplayName = who.Name;
                row.AgencyId = who.TenantId;
                row.LastSeenAtUtc = now;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Two first sightings of the same user at once: the other one inserted the row.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("The user-name registry could not record a sign-in ({ExceptionType}); it tries again in a minute.", ex.GetType().Name);
            return false;
        }

        cache.Set(WrittenKey(who.Sub), who.Value, RefreshEvery);
        return true;
    }
}

/// <summary>A signed-in user as the registry records it.</summary>
public sealed record UserNameSighting(string Sub, string Name, Guid? TenantId)
{
    public string Value => Name + "|" + TenantId;
}

/// <summary>PLAN-67 (P-6): writes the queued sightings, one at a time, off the request path.</summary>
public sealed class GatewayUserNameWriter(GatewayUserNameRegistry registry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var who in registry.Pending.ReadAllAsync(stoppingToken))
            await registry.WriteAsync(who, stoppingToken);
    }
}

/// <summary>PLAN-67 (P-6): queues every authenticated request's user; the registry keeps it to once an hour per user.</summary>
public sealed class GatewayUserNameRegistryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GatewayUserNameRegistry registry)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            registry.Queue(context.User);
        await next(context);
    }
}
