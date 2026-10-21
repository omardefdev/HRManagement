using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Maps concurrency/unique conflicts to HTTP 409 JSON (API) or TempData redirect (MVC forms) (DATA-001 / UX-004).
/// Register early via <see cref="ConcurrencyMiddlewareExtensions.UseHrConcurrencyConflictHandling"/>.
/// </summary>
public sealed class ConcurrencyConflictMiddleware(
    RequestDelegate next,
    ILogger<ConcurrencyConflictMiddleware> logger)
{
    public const string TempDataErrorKey = "Error";
    public const string TempDataShowDialogKey = "ShowDialog";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Optimistic concurrency conflict on {Path}", context.Request.Path);
            await WriteConflictAsync(context, ConcurrencyConflictMessages.StaleWrite, ex);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            logger.LogWarning(ex, "Unique constraint violation on {Path}", context.Request.Path);
            var resolution = UniqueConstraintMessageRegistry.Resolve(ex);
            await WriteConflictAsync(context, resolution.Message, ex);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("تعارض", StringComparison.Ordinal)
            || ex.Message.Contains(ConcurrencyConflictMessages.InsufficientBalance, StringComparison.Ordinal)
            || ex.Message.Contains(ConcurrencyConflictMessages.IdempotencyMismatch, StringComparison.Ordinal))
        {
            await WriteConflictAsync(context, ex.Message, ex);
        }
    }

    private static async Task WriteConflictAsync(HttpContext context, string message, Exception ex)
    {
        if (context.Response.HasStarted)
            throw new InvalidOperationException(message, ex);

        if (HrRequestClassification.WantsJsonResponse(context))
        {
            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = message, code = "concurrency_conflict" });
            return;
        }

        await WriteHtmlConflictAsync(context, message);
    }

    private static async Task WriteHtmlConflictAsync(HttpContext context, string message)
    {
        var tempDataFactory = context.RequestServices.GetService<ITempDataDictionaryFactory>();
        if (tempDataFactory is not null)
        {
            var tempData = tempDataFactory.GetTempData(context);
            tempData[TempDataErrorKey] = message;
            tempData[TempDataShowDialogKey] = "true";
            tempData.Save();
        }

        var target = SafeRedirect.TryGetSafeRefererRedirect(context, out var referer)
            ? referer
            : "/";

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status302Found;
        context.Response.Headers.Location = target;
        await context.Response.CompleteAsync();
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
