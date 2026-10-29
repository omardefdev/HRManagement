using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.ServiceDefaults.Tenancy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

public class TenantQuarantineResolveApiTests : IClassFixture<WebApplicationFactory<HRCoreWebProgram>>
{
    private readonly WebApplicationFactory<HRCoreWebProgram> _baseFactory;
    private readonly string _dbPath;

    public TenantQuarantineResolveApiTests(WebApplicationFactory<HRCoreWebProgram> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hr-quarantine-api-{Guid.NewGuid():N}.db");
        _baseFactory = factory;
    }

    private WebApplicationFactory<HRCoreWebProgram> CreateFactory() =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false"); // HCM-026: no background intake job in tests
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false"); // HCM-027: no background offer expiry job in tests
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false"); // HCM-027: no background reserve expiry job in tests
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false"); // COM-030: no background candidate notification job in tests
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false"); // HCM-030 (G3): no background candidate retention job in tests
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
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<HRCoreWebProgram> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader,
            $"{HrManagementRoles.Admin},{HrManagementRoles.PlatformAdmin}");
        return client;
    }

    [Fact]
    public async Task Resolve_AssignTenant_updates_employee_and_closes_quarantine()
    {
        var factory = CreateFactory();
        Guid entryId;
        Guid employeeId;
        Guid agencyTenantId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            await db.Database.EnsureCreatedAsync();

            agencyTenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = agencyTenantId,
                Code = "AGENCY-Q",
                NameAr = "جهة Q",
                IsActive = true
            });

            employeeId = Guid.NewGuid();
            db.Employees.Add(new Employee
            {
                Id = employeeId,
                TenantId = TenantDefaults.DefaultTenantId,
                NationalId = "Q0000000001",
                FirstName = "محجور",
                LastName = "اختبار",
                FullName = "محجور اختبار",
                Gender = Gender.Male,
                CurrentStatus = EmploymentStatus.Active
            });

            entryId = Guid.NewGuid();
            db.TenantQuarantineEntries.Add(new TenantQuarantineEntry
            {
                Id = entryId,
                ServiceName = "HRCore",
                TableName = "Employees",
                RecordId = employeeId,
                Reason = "orphan platform row"
            });
            await db.SaveChangesAsync();
        }

        var client = CreateClient(factory);
        var payload = new
        {
            resolution = "AssignTenant",
            assignedTenantId = agencyTenantId,
            reason = "manual reconciliation AC-08"
        };

        var resp = await client.PostAsJsonAsync($"api/tenant-quarantine/{entryId:D}/resolve", payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        var entry = await verifyDb.TenantQuarantineEntries.FindAsync(entryId);
        Assert.NotNull(entry!.ResolvedAt);
        Assert.Equal(agencyTenantId, entry.AssignedTenantId);

        var employee = await verifyDb.Employees.IgnoreQueryFilters().FirstAsync(e => e.Id == employeeId);
        Assert.Equal(agencyTenantId, employee.TenantId);
    }

    [Fact]
    public async Task List_returns_open_quarantine_entries()
    {
        var factory = CreateFactory();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.TenantQuarantineEntries.Add(new TenantQuarantineEntry
            {
                ServiceName = "HRCore",
                TableName = "Employees",
                RecordId = Guid.NewGuid(),
                Reason = "open"
            });
            await db.SaveChangesAsync();
        }

        var client = CreateClient(factory);
        var resp = await client.GetAsync("api/tenant-quarantine?resolved=false");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
    }
}

public class QuarantineBlocksOperationTests : IClassFixture<WebApplicationFactory<HRCoreWebProgram>>
{
    private readonly WebApplicationFactory<HRCoreWebProgram> _baseFactory;
    private readonly string _dbPath;

    public QuarantineBlocksOperationTests(WebApplicationFactory<HRCoreWebProgram> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hr-quarantine-block-{Guid.NewGuid():N}.db");
        _baseFactory = factory;
    }

    private WebApplicationFactory<HRCoreWebProgram> CreateFactory(Guid tenantId) =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false"); // HCM-026: no background intake job in tests
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false"); // HCM-027: no background offer expiry job in tests
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false"); // HCM-027: no background reserve expiry job in tests
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false"); // COM-030: no background candidate notification job in tests
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false"); // HCM-030 (G3): no background candidate retention job in tests
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
            });
        });

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
    public async Task Patch_employee_returns_409_when_quarantined()
    {
        var tenantId = TenantDefaults.DefaultTenantId;
        var factory = CreateFactory(tenantId);
        Guid employeeId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            await db.Database.EnsureCreatedAsync();

            employeeId = Guid.NewGuid();
            db.Employees.Add(new Employee
            {
                Id = employeeId,
                TenantId = tenantId,
                NationalId = "Q0000000002",
                FirstName = "blocked",
                LastName = "emp",
                FullName = "blocked emp",
                Gender = Gender.Male,
                CurrentStatus = EmploymentStatus.Active
            });
            db.TenantQuarantineEntries.Add(new TenantQuarantineEntry
            {
                ServiceName = "HRCore",
                TableName = "Employees",
                RecordId = employeeId,
                Reason = "orphan"
            });
            await db.SaveChangesAsync();
        }

        var client = ClientForTenant(factory, tenantId);
        var resp = await client.PatchAsJsonAsync(
            $"api/employees/{employeeId:D}",
            new { notes = "should fail" });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(QuarantinedRecordException.ErrorCode, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_employee_returns_409_when_quarantined_row_is_hidden_by_tenant_filter()
    {
        var requestTenantId = Guid.NewGuid();
        var factory = CreateFactory(requestTenantId);
        Guid employeeId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            await db.Database.EnsureCreatedAsync();

            employeeId = Guid.NewGuid();
            db.Employees.Add(new Employee
            {
                Id = employeeId,
                TenantId = TenantDefaults.DefaultTenantId,
                NationalId = "Q0000000003",
                FirstName = "hidden",
                LastName = "quarantined",
                FullName = "hidden quarantined",
                Gender = Gender.Male,
                CurrentStatus = EmploymentStatus.Active
            });
            db.TenantQuarantineEntries.Add(new TenantQuarantineEntry
            {
                ServiceName = "HRCore",
                TableName = "Employees",
                RecordId = employeeId,
                Reason = "orphan platform row"
            });
            await db.SaveChangesAsync();
        }

        var client = ClientForTenant(factory, requestTenantId);
        var resp = await client.PatchAsJsonAsync(
            $"api/employees/{employeeId:D}",
            new { notes = "should remain blocked" });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(QuarantinedRecordException.ErrorCode, body, StringComparison.OrdinalIgnoreCase);
    }
}
