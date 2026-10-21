using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Core.Infrastructure;

public static class CodeGenerationDependencyInjection
{
    public static IServiceCollection AddHrCodeGenerationEngine(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<ICodeGenerator, CodeGeneratorService>();
        services.AddScoped<IFundedPositionCodeService, FundedPositionCodeService>();
        services.AddScoped<IJobCatalogCodeService, JobCatalogCodeService>();
        services.AddScoped<CodSequenceBootstrapService>();
        return services;
    }
}
