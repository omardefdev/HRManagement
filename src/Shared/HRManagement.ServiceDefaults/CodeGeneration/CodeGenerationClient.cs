using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.CodeGeneration;

public interface ICodeGenerationClient
{
    Task<CodeGenerationResult?> GenerateAsync(CodeGenerationRequest request, CancellationToken ct = default);
    Task<CodeGenerationResult?> PreviewAsync(CodeGenerationRequest request, CancellationToken ct = default);
}

public sealed class CodeGenerationClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CodeGenerationClient> logger) : ICodeGenerationClient
{
    public const string HttpClientName = "hr-core-codes";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<CodeGenerationResult?> GenerateAsync(CodeGenerationRequest request, CancellationToken ct = default) =>
        PostAsync("/api/internal/codes/generate", request, ct);

    public Task<CodeGenerationResult?> PreviewAsync(CodeGenerationRequest request, CancellationToken ct = default) =>
        PostAsync("/api/internal/codes/preview", request, ct);

    private async Task<CodeGenerationResult?> PostAsync(string path, CodeGenerationRequest request, CancellationToken ct)
    {
        var mode = configuration["CodeGeneration:DefaultMode"] ?? "Required";
        if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("CodeGeneration:Off — generation refused for {EntityKind}", request.EntityKind);
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.PostAsJsonAsync(path, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Code generation HTTP {Status} for {EntityKind}", response.StatusCode, request.EntityKind);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CodeGenerationResult>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Code generation failed for {EntityKind}", request.EntityKind);
            return null;
        }
    }
}

public static class CodeGenerationGateway
{
    public static async Task<(bool Ok, string? Code, string? Error)> GenerateOrErrorAsync(
        ICodeGenerationClient client,
        CodeGenerationRequest request,
        CancellationToken ct = default)
    {
        var result = await client.GenerateAsync(request, ct);
        if (result is null || string.IsNullOrWhiteSpace(result.Code))
            return (false, null, "تعذّر توليد الرمز — خدمة الترميز غير متاحة");
        return (true, result.Code, null);
    }
}

public static class CodeGenerationClientExtensions
{
    public static IServiceCollection AddHrCodeGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICodeGenerationClient, CodeGenerationClient>();
        // PLAN-37: the HR.Core URL is required (start-up error naming Integration:HrCoreBaseUrl outside Development).
        services.AddServiceEndpointWarnings();
        services.AddServiceAuthenticatedClient(CodeGenerationClient.HttpClientName, HrCoreEndpoint.RequireBaseUri(configuration));
        return services;
    }
}
