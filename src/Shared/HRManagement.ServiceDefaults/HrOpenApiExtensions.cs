using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults;

/// <summary>تسجيل OpenAPI/Swagger موحّد لجميع خدمات HR.</summary>
public static class HrOpenApiExtensions
{
    public static IServiceCollection AddHrOpenApi(this IServiceCollection services, string apiTitle, string version = "v1")
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        })
        .AddMvc()
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = apiTitle, Version = "v1" });
            options.SwaggerDoc("v2", new() { Title = apiTitle, Version = "v2" });
            options.DocInclusionPredicate((docName, apiDesc) =>
                string.Equals(apiDesc.GroupName, docName, StringComparison.OrdinalIgnoreCase)
                || (docName == "v1" && string.IsNullOrEmpty(apiDesc.GroupName)));
        });
        return services;
    }

    public static WebApplication UseHrOpenApi(this WebApplication app, string? routePrefix = null)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
            if (!string.IsNullOrWhiteSpace(routePrefix))
                c.RoutePrefix = routePrefix;
        });
        return app;
    }
}
