using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults;

public static class HrGatewayConfigurationExtensions
{
    public const string InternalBaseUrlKey = "HrGateway:InternalBaseUrl";

    public static string RequireInternalBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration[InternalBaseUrlKey]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                $"{InternalBaseUrlKey} is required for server-side File API calls (RegisterReferenceAsync).");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{InternalBaseUrlKey} must be an absolute URL (e.g. http://hr-gateway:8080).");
        }

        ValidateInternalBaseUrlForContainer(baseUrl, uri);
        return baseUrl.TrimEnd('/');
    }

    internal static void ValidateInternalBaseUrlForContainer(string baseUrl, Uri? uri = null)
    {
        uri ??= new Uri(baseUrl, UriKind.Absolute);
        if (!IsRunningInContainer())
            return;

        if (IsLocalhost(uri))
        {
            throw new InvalidOperationException(
                $"{InternalBaseUrlKey} must use Docker service DNS (e.g. http://hr-gateway:8080) when running in a container. " +
                $"Current value: {baseUrl}");
        }
    }

    internal static bool IsRunningInContainer() =>
        string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalhost(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || uri.Host == "::1";
}
