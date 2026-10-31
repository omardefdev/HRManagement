using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Validates the deployment license key and its expiry from configuration (G-OPS-15). Health and metrics endpoints
/// are exempt, so an expired license answers 402 to users while the orchestrator still sees the service as alive.
/// </summary>
public sealed class LicenseValidationMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private static readonly PathString[] ExemptPaths =
    [
        new("/health"),
        new("/alive"),
        new("/ready"),
        new("/metrics")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        var licenseKey = configuration["Deployment:LicenseKey"];
        var expires = configuration["Deployment:LicenseExpiresUtc"];

        if (TryParseExpiry(expires, out var expiry) && DateTime.UtcNow > expiry)
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsync("انتهت صلاحية ترخيص التركيب. تواصل مع المورّد.");
            return;
        }

        if (string.IsNullOrWhiteSpace(licenseKey)
            && configuration.GetValue<bool>("Deployment:RequireLicenseKey"))
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsync("مفتاح الترخيص مطلوب لهذا التركيب.");
            return;
        }

        await next(context);
    }

    public static bool IsExempt(PathString path) =>
        ExemptPaths.Any(exempt => path.StartsWithSegments(exempt, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Parses <c>Deployment:LicenseExpiresUtc</c> independently of the server culture (an ar-SA host would otherwise read
    /// the date in the Hijri calendar); a value without an offset is UTC.
    /// </summary>
    public static bool TryParseExpiry(string? value, out DateTime expiryUtc)
    {
        expiryUtc = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
            return false;

        expiryUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }
}

public static class LicenseValidationExtensions
{
    public static IApplicationBuilder UseHrLicenseValidation(this IApplicationBuilder app) =>
        app.UseMiddleware<LicenseValidationMiddleware>();
}
