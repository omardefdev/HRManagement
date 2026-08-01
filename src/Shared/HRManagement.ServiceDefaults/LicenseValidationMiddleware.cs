using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Validates deployment license key and optional employee cap from configuration.
/// </summary>
public sealed class LicenseValidationMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var licenseKey = configuration["Deployment:LicenseKey"];
        var expires = configuration["Deployment:LicenseExpiresUtc"];

        if (!string.IsNullOrWhiteSpace(expires)
            && DateTime.TryParse(expires, out var expiry)
            && DateTime.UtcNow > expiry.ToUniversalTime())
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
}

public static class LicenseValidationExtensions
{
    public static IApplicationBuilder UseHrLicenseValidation(this IApplicationBuilder app) =>
        app.UseMiddleware<LicenseValidationMiddleware>();
}
