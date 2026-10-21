using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Core.Infrastructure.Middleware;

/// <summary>HCM-006: rate limit public careers apply/track endpoints.</summary>
public static class PublicCareersRateLimitExtensions
{
    public const string PolicyName = "public-careers";

    public static IServiceCollection AddPublicCareersRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("Recruitment:PublicCareersRateLimitPerMinute", 30);
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(PolicyName, limiter =>
            {
                limiter.PermitLimit = permitLimit;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
        return services;
    }

    public static IApplicationBuilder UsePublicCareersRateLimiting(this IApplicationBuilder app) =>
        app.UseRateLimiter();
}
