using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public interface IFileUrlProvider
{
    string UploadUrl();
    string DownloadUrl(Guid fileId);
    string MetadataUrl(Guid fileId);
    string ReferencesUrl(Guid fileId);
}

internal static class FileApiPaths
{
    internal const string Root = "/api/files";

    internal static string Upload() => Root;

    internal static string Download(Guid fileId) => $"{Root}/{fileId:D}/download";

    internal static string Metadata(Guid fileId) => $"{Root}/{fileId:D}";

    internal static string References(Guid fileId) => $"{Root}/{fileId:D}/references";
}

internal sealed class GatewayFileUrlProvider : IFileUrlProvider
{
    public string UploadUrl() => FileApiPaths.Upload();

    public string DownloadUrl(Guid fileId) => FileApiPaths.Download(fileId);

    public string MetadataUrl(Guid fileId) => FileApiPaths.Metadata(fileId);

    public string ReferencesUrl(Guid fileId) => FileApiPaths.References(fileId);
}

internal sealed class DirectGatewayFileUrlProvider(IConfiguration configuration) : IFileUrlProvider
{
    private string ApiRoot => $"{RequirePublicBaseUrl()}{FileApiPaths.Root}";

    public string UploadUrl() => ApiRoot;

    public string DownloadUrl(Guid fileId) => $"{RequirePublicBaseUrl()}{FileApiPaths.Download(fileId)}";

    public string MetadataUrl(Guid fileId) => $"{RequirePublicBaseUrl()}{FileApiPaths.Metadata(fileId)}";

    public string ReferencesUrl(Guid fileId) => $"{RequirePublicBaseUrl()}{FileApiPaths.References(fileId)}";

    private string RequirePublicBaseUrl()
    {
        var baseUrl = configuration["HrGateway:PublicBaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "HrGateway:PublicBaseUrl is required when HrAuth:BehindGateway is false.");
        }

        return baseUrl.TrimEnd('/');
    }
}

public static class FileUrlProviderExtensions
{
    public static IServiceCollection AddHrFileUrlProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var behindGateway = configuration.GetValue("HrAuth:BehindGateway", true);
        if (behindGateway)
        {
            services.AddSingleton<IFileUrlProvider, GatewayFileUrlProvider>();
        }
        else
        {
            services.AddSingleton<IFileUrlProvider, DirectGatewayFileUrlProvider>();
        }

        return services;
    }
}
