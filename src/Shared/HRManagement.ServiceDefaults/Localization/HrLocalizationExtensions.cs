using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Localization;

/// <summary>L10N-001: shared localization — ar-SY default, en fallback, cookie/query providers.</summary>
public static class HrLocalizationExtensions
{
    public static readonly CultureInfo[] SupportedCultures =
    [
        new("ar-SY"),
        new("en")
    ];

    public const string DefaultCultureName = "ar-SY";
    public const string CultureCookieName = ".HrManagement.Culture";

    public static IServiceCollection AddHrLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Localization/Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(DefaultCultureName);
            options.SupportedCultures = SupportedCultures;
            options.SupportedUICultures = SupportedCultures;
            options.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new CookieRequestCultureProvider { CookieName = CultureCookieName }
            ];
        });
        services.AddSingleton<ICalendarService, DamascusCalendarService>();
        return services;
    }

    public static WebApplicationBuilder AddHrLocalization(this WebApplicationBuilder builder)
    {
        builder.Services.AddHrLocalization();
        return builder;
    }

    public static WebApplication UseHrLocalization(this WebApplication app)
    {
        app.UseRequestLocalization();
        return app;
    }
}
