using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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
        // PLAN-67 (D-67-2): the display zone comes from Localization:TimeZone (Asia/Damascus by default); a datetime-local value posted
        // to a page is read in that zone and bound as UTC.
        services.AddSingleton<ICalendarService>(sp => new DamascusCalendarService(sp.GetService<IConfiguration>()));
        services.Configure<MvcOptions>(options => options.ModelBinderProviders.Insert(0, new HrLocalDateTimeModelBinderProvider()));
        return services;
    }

    public static WebApplicationBuilder AddHrLocalization(this WebApplicationBuilder builder)
    {
        builder.Services.AddHrLocalization();
        return builder;
    }

    public static WebApplication UseHrLocalization(this WebApplication app)
    {
        // PLAN-67 (D-67-2): the zone is read once, at start-up, so HrLocalTime.Zone is the configured one before the first request
        // (the candidate portal and its messages read it outside a view), and an unknown Localization:TimeZone stops the service.
        _ = app.Services.GetRequiredService<ICalendarService>();
        app.UseRequestLocalization();
        return app;
    }
}
