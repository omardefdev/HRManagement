using System.Net;
using System.Net.Http.Headers;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-67 (D-67-5, G-UX-07, P-9): HR.Core as the gateway runs it, with the module's path base (<c>/ui/hr-core</c>) and the public portal
/// arriving through <c>/careers</c>. The portal answers at <c>/careers</c> alone; <c>/ui/hr-core/</c> opens the dashboard; the portal's
/// pages are no longer reachable under the module's path.
/// </summary>
public sealed class Ux067PortalRoutingTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hr-ux067-portal-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<HRCoreWebProgram> _host = null!;

    public async Task InitializeAsync()
    {
        _host = new WebApplicationFactory<HRCoreWebProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", $"Data Source={_dbPath}");
            builder.UseSetting("ASPNETCORE_PATHBASE", "/ui/hr-core");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsAtStartup"] = "false",
                ["ConnectionStrings:HRCoreDb"] = $"Data Source={_dbPath}",
                ["ASPNETCORE_PATHBASE"] = "/ui/hr-core",
                ["HrAuth:BehindGateway"] = "true",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });
        await using var scope = _host.Services.CreateAsyncScope();
        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            await scope.ServiceProvider.GetRequiredService<HRCoreDbContext>().Database.EnsureCreatedAsync();
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { global::System.IO.File.Delete(_dbPath); } catch (IOException) { }
    }

    private HttpClient Client(bool signedIn)
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (signedIn)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, TenantDefaults.DefaultTenantId.ToString("D"));
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString("D"));
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "hr-admin");
        }

        return client;
    }

    [Fact]
    public async Task The_portal_answers_at_careers_and_its_tracking_page_too()
    {
        using var client = Client(signedIn: false);
        var portal = await client.GetAsync("/careers");
        Assert.Equal(HttpStatusCode.OK, portal.StatusCode);
        Assert.Contains("careers-navbar", await portal.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/careers/track")).StatusCode);
    }

    [Fact]
    public async Task The_module_root_opens_the_dashboard_not_the_portal()
    {
        using var client = Client(signedIn: true);
        var root = await client.GetAsync("/ui/hr-core/");
        var body = await root.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.DoesNotContain("careers-navbar", body, StringComparison.Ordinal);
        Assert.Contains("app-sidebar", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/ui/hr-core/track")]
    [InlineData("/ui/hr-core/my")]
    public async Task The_portal_pages_are_not_reachable_under_the_module_path(string path)
    {
        using var client = Client(signedIn: true);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
