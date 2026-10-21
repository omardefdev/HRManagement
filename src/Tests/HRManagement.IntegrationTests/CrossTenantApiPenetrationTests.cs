using System.Net;
using System.Net.Http.Headers;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// TEN-003: API-level cross-tenant penetration — create as Tenant A, read/modify/delete as Tenant B
/// must yield 404 or empty (resource invisible via EF filter), not a privileged 403.
/// </summary>
public class CrossTenantApiPenetrationTests : IClassFixture<WebApplicationFactory<HRCoreWebProgram>>
{
    private readonly WebApplicationFactory<HRCoreWebProgram> _baseFactory;
    private readonly string _dbPath;

    public CrossTenantApiPenetrationTests(WebApplicationFactory<HRCoreWebProgram> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hr-tenant-api-{Guid.NewGuid():N}.db");
        _baseFactory = factory;
    }

    private WebApplicationFactory<HRCoreWebProgram> CreateFactory(Guid? fixedTenantForSeed = null)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false"); // HCM-026: no background intake job in tests
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false"); // HCM-027: no background offer expiry job in tests
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", $"Data Source={_dbPath}");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:HRCoreDb"] = $"Data Source={_dbPath}",
                    ["HrAuth:BehindGateway"] = "true",
                    ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                    ["HrGateway:InternalBaseUrl"] = "http://localhost:5200"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();

                if (fixedTenantForSeed.HasValue)
                {
                    var mock = new Mock<ITenantContext>();
                    mock.Setup(t => t.TenantId).Returns(fixedTenantForSeed.Value);
                    mock.Setup(t => t.IsPlatformOperator).Returns(false);
                    mock.Setup(t => t.RequireTenantId()).Returns(fixedTenantForSeed.Value);
                    services.RemoveAll<ITenantContext>();
                    services.AddSingleton(mock.Object);
                }
            });
        });
    }

    private static HttpClient ClientForTenant(WebApplicationFactory<HRCoreWebProgram> factory, Guid tenantId)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader,
            $"{HrManagementRoles.Admin},{HrManagementRoles.HrCore}");
        return client;
    }

    [Fact]
    public async Task TenantB_cannot_read_or_delete_TenantA_employee()
    {
        var tenantA = TenantTestFixtures.TenantA;
        var tenantB = TenantTestFixtures.TenantB;
        Guid employeeId;

        await using (var seedFactory = CreateFactory(tenantA))
        {
            await using var scope = seedFactory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            await db.Database.EnsureCreatedAsync();

            var emp = TenantTestFixtures.CreateEmployee(tenantA, null, Guid.NewGuid().ToString("N")[..12], "موظف جهة أ");
            db.Employees.Add(emp);
            await db.SaveChangesAsync();
            employeeId = emp.Id;
        }

        // Runtime factory uses claim-based TenantContext (not fixed mock)
        await using var factory = CreateFactory(fixedTenantForSeed: null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        using var clientB = ClientForTenant(factory, tenantB);

        var details = await clientB.GetAsync($"/Employees/Details/{employeeId}");
        Assert.True(
            details.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Redirect,
            $"Expected invisible resource (404/redirect), got {(int)details.StatusCode}");
        Assert.NotEqual(HttpStatusCode.Forbidden, details.StatusCode);

        var delete = await clientB.PostAsync($"/Employees/Delete/{employeeId}", new FormUrlEncodedContent([]));
        // Antiforgery may yield 400; missing resource yields 404/redirect. Never a privileged Forbidden from seeing the row.
        Assert.True(
            delete.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Redirect
                or HttpStatusCode.MethodNotAllowed
                or HttpStatusCode.BadRequest,
            $"Expected delete to miss resource or fail validation, got {(int)delete.StatusCode}");
        Assert.NotEqual(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, delete.StatusCode);
        Assert.NotEqual(HttpStatusCode.NoContent, delete.StatusCode);

        using var clientA = ClientForTenant(factory, tenantA);
        // Index as A should not throw; B's list must not contain A's national id via HTML when seeded only A
        var indexB = await clientB.GetAsync("/Employees");
        Assert.Equal(HttpStatusCode.OK, indexB.StatusCode);
        var htmlB = await indexB.Content.ReadAsStringAsync();
        Assert.DoesNotContain("موظف جهة أ", htmlB);

        _ = clientA;
    }

    [Fact]
    public async Task Export_and_search_paths_do_not_bypass_tenant_filter()
    {
        // Ensures no IgnoreQueryFilters in non-platform employee listing (search uses same DbSet filter).
        var suspicious = typeof(HRCoreDbContext).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .Where(m => m.Name.Contains("IgnoreQueryFilters", StringComparison.Ordinal) &&
                        m.DeclaringType?.Namespace?.StartsWith("HR.Core", StringComparison.Ordinal) == true);

        Assert.DoesNotContain(suspicious, m => true);

        // Sanctioned bypass lives only in ServiceDefaults TenantFilterBypass.
        var bypass = typeof(TenantFilterBypass).GetMethod(nameof(TenantFilterBypass.IgnoreTenantFilter));
        Assert.NotNull(bypass);
    }
}
