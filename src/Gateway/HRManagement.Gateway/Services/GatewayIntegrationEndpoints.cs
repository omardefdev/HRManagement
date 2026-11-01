using HRManagement.ServiceDefaults.Integration;

namespace HRManagement.Gateway.Services;

/// <summary>
/// PLAN-37: the HR.Core and Auth.Directory base URLs the gateway calls with the user's token (journey rebuild,
/// dashboard statistics), resolved once at start-up. Missing outside Development is a start-up error that names the
/// key; localhost is refused in a container (no silent localhost fallback).
/// </summary>
public sealed record GatewayIntegrationEndpoints(string HrCoreBaseUrl, string AuthDirectoryBaseUrl)
{
    public const string AuthDirectoryKey = "Integration:AuthDirectoryBaseUrl";
    public const string AuthDirectoryDevelopmentDefault = "http://localhost:5130";

    public static GatewayIntegrationEndpoints Resolve(IConfiguration configuration, IHostEnvironment environment) =>
        new(
            HrCoreEndpoint.RequireBaseUrl(configuration, environment),
            ServiceEndpoint.Require(configuration, AuthDirectoryKey, AuthDirectoryDevelopmentDefault, environment: environment));
}
