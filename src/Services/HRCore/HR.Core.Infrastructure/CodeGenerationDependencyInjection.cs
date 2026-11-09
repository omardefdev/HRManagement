using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure;

public static class CodeGenerationDependencyInjection
{
    /// <summary>
    /// COD-001. PLAN-48 (D-TEN-9): <see cref="CodeGenerationOptions.MissingPolicy"/> is Refuse in every environment except
    /// Development and Testing, where it is Fallback with a prefix; <c>CodeGeneration:MissingPolicy</c> may only make it stricter.
    /// With no configuration or environment (tests, tools) the generator refuses.
    /// </summary>
    public static IServiceCollection AddHrCodeGenerationEngine(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null)
    {
        services.AddMemoryCache();
        services.AddSingleton<IOptions<CodeGenerationOptions>>(sp =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("CodeGeneration");
            return Options.Create(CodeGenerationOptions.Resolve(
                configuration ?? sp.GetService<IConfiguration>(),
                environment ?? sp.GetService<IHostEnvironment>(),
                message => logger?.LogWarning("{Message}", message)));
        });
        services.AddScoped<ICodeGenerator, CodeGeneratorService>();
        services.AddScoped<IFundedPositionCodeService, FundedPositionCodeService>();
        services.AddScoped<IJobCatalogCodeService, JobCatalogCodeService>();
        services.AddScoped<CodSequenceBootstrapService>();
        return services;
    }
}
