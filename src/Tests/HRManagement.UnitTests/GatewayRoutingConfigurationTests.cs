using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;

namespace HRManagement.UnitTests;

public class GatewayRoutingConfigurationTests
{
    private static IConfiguration LoadGatewayConfiguration()
    {
        var gatewayDir = ResolveGatewayDirectory();
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(gatewayDir, "appsettings.json"), optional: false)
            .Build();
    }

    private static string ResolveGatewayDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Gateway", "HRManagement.Gateway");
            if (System.IO.File.Exists(Path.Combine(candidate, "appsettings.json")))
                return candidate;

            var sibling = Path.Combine(dir.FullName, "Gateway", "HRManagement.Gateway");
            if (System.IO.File.Exists(Path.Combine(sibling, "appsettings.json")))
                return sibling;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate HRManagement.Gateway/appsettings.json");
    }

    [Fact]
    public void All_routes_reference_existing_clusters()
    {
        var config = LoadGatewayConfiguration();
        var routes = config.GetSection("ReverseProxy:Routes").GetChildren();
        var clusterIds = config.GetSection("ReverseProxy:Clusters")
            .GetChildren()
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var route in routes)
        {
            var clusterId = route["ClusterId"];
            Assert.False(string.IsNullOrWhiteSpace(clusterId), $"Route {route.Key} missing ClusterId");
            Assert.True(clusterIds.Contains(clusterId!), $"Route {route.Key} references unknown cluster {clusterId}");
        }
    }

    [Fact]
    public void Module_catalog_slugs_match_api_route_prefixes()
    {
        var config = LoadGatewayConfiguration();
        var catalog = new GatewayModuleCatalog(config);
        var routes = config.GetSection("ReverseProxy:Routes").GetChildren();

        foreach (var module in catalog.Modules)
        {
            var apiRouteKey = $"{module.Slug}-route".Replace("records-archive", "records-archive")
                .Replace("admin-logistics", "admin-logistics")
                .Replace("auth-directory", "auth-directory");

            // Standard naming: hr-core-route, payroll-route, etc.
            var expectedKey = module.Slug switch
            {
                "records-archive" => "records-archive-route",
                "admin-logistics" => "admin-logistics-route",
                "auth-directory" => "auth-directory-route",
                "it-infra" => "it-infra-route",
                "dev-analytics" => "dev-analytics-route",
                "file-storage" => "files-route",
                _ => $"{module.Slug}-route"
            };

            var route = routes.FirstOrDefault(r =>
                string.Equals(r.Key, expectedKey, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(route);

            var path = route!["Match:Path"];
            var expectedApiPrefix = module.Slug switch
            {
                "file-storage" => "/api/files",
                _ => $"/api/{module.Slug}"
            };
            Assert.Contains(expectedApiPrefix, path ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Ui_routes_have_path_remove_prefix_transform_or_use_catch_all()
    {
        var config = LoadGatewayConfiguration();
        var uiRoutes = config.GetSection("ReverseProxy:Routes")
            .GetChildren()
            .Where(r => r.Key.StartsWith("ui-", StringComparison.OrdinalIgnoreCase));

        foreach (var route in uiRoutes)
        {
            var path = route["Match:Path"] ?? string.Empty;
            Assert.Contains("{**catch-all}", path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Gateway_module_catalog_has_fourteen_modules()
    {
        var config = LoadGatewayConfiguration();
        var catalog = new GatewayModuleCatalog(config);
        Assert.Equal(14, catalog.Modules.Count);
    }

    [Fact]
    public void Careers_public_route_allows_anonymous_access()
    {
        var config = LoadGatewayConfiguration();
        var route = config.GetSection("ReverseProxy:Routes:careers-public");
        Assert.True(route.Exists());
        Assert.Equal("Anonymous", route["AuthorizationPolicy"]);
        Assert.Equal("hr-core-cluster", route["ClusterId"]);
        Assert.Contains("/careers/", route["Match:Path"] ?? string.Empty, StringComparison.Ordinal);
    }
}
