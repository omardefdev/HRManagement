using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public interface IFileReferenceClient
{
    Task RegisterReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default);
    Task ReleaseReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Delivers file-reference intents for an explicit tenant. The outbox dispatcher runs without a request (no
/// HttpContext, no <see cref="TenantIdFlow"/>), so the tenant comes from each outbox row, never from ambient state.
/// </summary>
public interface ITenantFileReferenceDelivery
{
    Task RegisterReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default);
    Task ReleaseReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default);
}

public sealed class HttpFileReferenceClient(IHttpClientFactory httpClientFactory) : IFileReferenceClient, ITenantFileReferenceDelivery
{
    private HttpClient CreateClient() => httpClientFactory.CreateClient("FileStorage");

    public Task RegisterReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, fileId, holderKey, tenantId: null, cancellationToken);

    public Task ReleaseReferenceAsync(Guid fileId, string holderKey, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, fileId, holderKey, tenantId: null, cancellationToken);

    public Task RegisterReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, fileId, holderKey, RequireTenant(tenantId), cancellationToken);

    public Task ReleaseReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, fileId, holderKey, RequireTenant(tenantId), cancellationToken);

    private async Task SendAsync(
        HttpMethod method,
        Guid fileId,
        string holderKey,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(method, FileApiPaths.References(fileId))
        {
            Content = JsonContent.Create(new { holderKey })
        };
        // An explicit tenant wins: ForwardTenantHandler only adds X-Tenant-ID when the header is absent.
        if (tenantId is { } tenant)
            request.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenant.ToString("D"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Guid RequireTenant(Guid tenantId) =>
        tenantId == Guid.Empty
            ? throw new ArgumentException("A tenant id is required to deliver a file reference.", nameof(tenantId))
            : tenantId;
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
