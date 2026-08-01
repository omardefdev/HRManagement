using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using HRManagement.ServiceDefaults.NoFork;

namespace HRManagement.ServiceDefaults;

public sealed class OrganizationBrandingOptions
{
    public const string SectionName = "Organization";

    public string Name { get; set; } = "شعبة التنظيم والموارد البشرية";
    public string ShortName { get; set; } = "الموارد البشرية";
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#6b1f2a";
}

public static class OrganizationBrandingExtensions
{
    public static IServiceCollection AddOrganizationBranding(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OrganizationBrandingOptions>(configuration.GetSection(OrganizationBrandingOptions.SectionName));
        services.TryAddSingleton<IAgencyBrandingAccessor, AgencyBrandingAccessor>();
        return services;
    }
}
