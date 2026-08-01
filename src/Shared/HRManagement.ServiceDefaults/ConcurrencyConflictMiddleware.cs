using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Maps concurrency/unique conflicts to HTTP 409 with Arabic messages (DATA-001).
/// Register early via <see cref="ConcurrencyMiddlewareExtensions.UseHrConcurrencyConflictHandling"/>.
/// </summary>
public sealed class ConcurrencyConflictMiddleware(
    RequestDelegate next,
    ILogger<ConcurrencyConflictMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Optimistic concurrency conflict on {Path}", context.Request.Path);
            await WriteConflictAsync(context, ConcurrencyConflictMessages.StaleWrite);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            logger.LogWarning(ex, "Unique constraint violation on {Path}", context.Request.Path);
            await WriteConflictAsync(context, ConcurrencyConflictMessages.UniqueViolation);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("تعارض", StringComparison.Ordinal)
            || ex.Message.Contains(ConcurrencyConflictMessages.InsufficientBalance, StringComparison.Ordinal)
            || ex.Message.Contains(ConcurrencyConflictMessages.IdempotencyMismatch, StringComparison.Ordinal))
        {
            await WriteConflictAsync(context, ex.Message);
        }
    }

    private static async Task WriteConflictAsync(HttpContext context, string message)
    {
        if (context.Response.HasStarted)
            throw new InvalidOperationException(message);

        context.Response.Clear();
        context.Response.StatusCode = (int)HttpStatusCode.Conflict;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { error = message, code = "concurrency_conflict" });
    }
}

public static class ConcurrencyMiddlewareExtensions
{
    public static IServiceCollection AddHrConcurrencyConflictHandling(this IServiceCollection services)
    {
        // Marker for discoverability; middleware itself needs no extra DI.
        return services;
    }

    public static IApplicationBuilder UseHrConcurrencyConflictHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ConcurrencyConflictMiddleware>();
}
