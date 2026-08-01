using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public interface IFileReferenceClient
{
    Task RegisterReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default);
    Task ReleaseReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default);
}

public sealed class HttpFileReferenceClient(IHttpClientFactory httpClientFactory) : IFileReferenceClient
{
    private HttpClient CreateClient() => httpClientFactory.CreateClient("FileStorage");

    public async Task RegisterReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            FileApiPaths.References(fileId),
            new { holderKey },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReleaseReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Delete, FileApiPaths.References(fileId))
        {
            Content = JsonContent.Create(new { holderKey })
        };
        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public static class FileReferenceClientExtensions
{
    /// <summary>
    /// Registers File.Storage reference client authenticated with a service JWT (IAM-004).
    /// Optionally forwards X-Acting-User for audit when an interactive user is present.
    /// </summary>
    public static IServiceCollection AddHrFileReferenceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = HrGatewayConfigurationExtensions.RequireInternalBaseUrl(configuration);

        services.AddHttpContextAccessor();
        services.AddServiceAuthenticatedClient(
            "FileStorage",
            new Uri(baseUrl + "/"),
            forwardActingUser: true);

        services.AddScoped<IFileReferenceClient, HttpFileReferenceClient>();
        return services;
    }

    public static string BuildHolderKey(string service, string entityType, Guid entityId, string fieldName) =>
        $"{service}:{entityType}:{entityId:N}:{fieldName}";
}
