using HRManagement.ServiceDefaults.NoFork;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace HRManagement.ServiceDefaults;

public static class HrMvcSecurityExtensions
{
    /// <summary>Fallback auth + global antiforgery on mutating MVC actions (SEC-001).</summary>
    public static IServiceCollection AddHrMvcSecurityDefaults(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<AutoValidateAntiforgeryUnlessApiFilter>();
        });

        // Every module is exposed through the same gateway host. An explicit per-assembly
        // cookie name prevents one module's antiforgery cookie from shadowing another.
        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "HRManagement";
        var cookieSuffix = new string(assemblyName
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = $".AspNetCore.Antiforgery.HRM-{cookieSuffix}";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // _HrSharedLayout injects IAgencyBrandingAccessor via _OrganizationBranding.
        services.TryAddSingleton<IAgencyBrandingAccessor, AgencyBrandingAccessor>();

        return services;
    }
}

/// <summary>Antiforgery on form POSTs; API controllers opt out via [ApiController].</summary>
public sealed class AutoValidateAntiforgeryUnlessApiFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor cad
            && cad.ControllerTypeInfo.IsDefined(typeof(ApiControllerAttribute), inherit: true))
            return;

        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsPost(method)
            && !HttpMethods.IsPut(method)
            && !HttpMethods.IsPatch(method)
            && !HttpMethods.IsDelete(method))
            return;

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new AntiforgeryValidationFailedResult();
        }
    }
}
