using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Signatures;

public static class LegalSignatureExtensions
{
    public static IServiceCollection AddLegalSignatures(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SignatureOptions>(configuration.GetSection(SignatureOptions.SectionName));
        services.AddSingleton<SignatureImmutabilityInterceptor>();
        services.AddSingleton<ISignatureService, SoftwareSignatureService>();
        return services;
    }

    public static WebApplicationBuilder AddLegalSignatures(this WebApplicationBuilder builder)
    {
        builder.Services.AddLegalSignatures(builder.Configuration);
        return builder;
    }
}
