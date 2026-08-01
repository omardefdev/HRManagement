using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>Persisted HTTP idempotency record (DATA-001 / bridge to INT-002).</summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int ResponseStatus { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class IdempotencyModelBuilderExtensions
{
    public static void ConfigureIdempotencyRecords(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ResponseBody).HasMaxLength(8000);
            entity.HasIndex(e => new { e.TenantId, e.Key }).IsUnique();
        });
    }
}

/// <summary>
/// MVC filter: when request has Idempotency-Key header, replay or store the response.
/// Requires a DbContext registered that includes <see cref="IdempotencyRecord"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var keyValues))
        {
            await next();
            return;
        }

        var key = keyValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
        {
            context.Result = new BadRequestObjectResult(new { error = "Idempotency-Key غير صالح." });
            return;
        }

        var db = context.HttpContext.RequestServices.GetService<DbContext>();
        if (db is null || db.Model.FindEntityType(typeof(IdempotencyRecord)) is null)
        {
            await next();
            return;
        }

        var tenant = context.HttpContext.RequestServices.GetService<HRManagement.SharedKernel.Tenancy.ITenantContext>();
        var tenantId = tenant?.TenantId ?? Guid.Empty;
        var hash = await HashRequestBodyAsync(context.HttpContext.Request);

        var existing = await db.Set<IdempotencyRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Key == key);

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                context.Result = new ConflictObjectResult(new
                {
                    error = ConcurrencyConflictMessages.IdempotencyMismatch,
                    code = "idempotency_mismatch"
                });
                return;
            }

            context.HttpContext.Response.StatusCode = existing.ResponseStatus;
            if (!string.IsNullOrEmpty(existing.ResponseBody))
            {
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(existing.ResponseBody);
            }

            context.Result = new EmptyResult();
            return;
        }

        var executed = await next();
        if (executed.Exception is not null || executed.Canceled)
            return;

        var status = context.HttpContext.Response.StatusCode;
        string? body = null;
        if (executed.Result is ObjectResult { Value: not null } obj)
            body = JsonSerializer.Serialize(obj.Value);
        else if (executed.Result is JsonResult { Value: not null } json)
            body = JsonSerializer.Serialize(json.Value);

        // Only store successful-ish completions (2xx/3xx/409 stored for stable replay of conflicts from unique).
        if (status is >= 200 and < 500)
        {
            db.Set<IdempotencyRecord>().Add(new IdempotencyRecord
            {
                TenantId = tenantId,
                Key = key,
                RequestHash = hash,
                ResponseStatus = status is 0 ? 200 : status,
                ResponseBody = body,
                CreatedAt = DateTime.UtcNow
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
            {
                // Parallel first writes — reload winner and treat as replay if hash matches.
                var winner = await db.Set<IdempotencyRecord>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Key == key);
                if (winner is not null && !string.Equals(winner.RequestHash, hash, StringComparison.Ordinal))
                {
                    context.Result = new ConflictObjectResult(new
                    {
                        error = ConcurrencyConflictMessages.IdempotencyMismatch,
                        code = "idempotency_mismatch"
                    });
                }
            }
        }
    }

    private static async Task<string> HashRequestBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        var payload = $"{request.Method}|{request.Path}|{body}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
