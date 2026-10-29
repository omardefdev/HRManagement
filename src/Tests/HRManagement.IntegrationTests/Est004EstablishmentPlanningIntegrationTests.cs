using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HRManagement.ServiceDefaults;
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

[Collection("IntegrationSerial")]
public class Est004EstablishmentPlanningIntegrationTests : IClassFixture<WebApplicationFactory<HRCoreWebProgram>>, IDisposable
{
    private readonly WebApplicationFactory<HRCoreWebProgram> _factory;
    private readonly string _dbPath;

    public Est004EstablishmentPlanningIntegrationTests(WebApplicationFactory<HRCoreWebProgram> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"hr-est004-{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
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
    }

    [Fact]
    public async Task Br1_mgr_gap_analysis_succeeds_for_br1()
    {
        await SeedPlanningDataAsync();
        using var client = CreateScopedClient(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.Br1OrgUnitId);

        var response = await client.GetAsync(
            $"/api/establishment/gap-analysis?orgUnitId={AcceptanceSeedIds.Br1OrgUnitId:D}&fiscalYear=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"source\":\"FundedPosition\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Br1_mgr_forbidden_for_br2_gap()
    {
        await SeedPlanningDataAsync();
        using var client = CreateScopedClient(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.Br1OrgUnitId);

        var response = await client.GetAsync(
            $"/api/establishment/gap-analysis?orgUnitId={AcceptanceSeedIds.Br2OrgUnitId:D}&fiscalYear=2026");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Central_import_finance_csv()
    {
        await SeedPlanningDataAsync();
        using var client = CreateCentralClient(AcceptanceSeedIds.AgencyAId);
        using var content = BuildCsvUpload(
            "CostCenter,GradeCode,ApprovedCount,ApprovedFTE,BudgetAmount\nCC-API-01,G5,3,3,300000\n");

        var response = await client.PostAsync("/api/establishment/finance/import?fiscalYear=2026", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("batchId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Br1_mgr_forbidden_finance_import()
    {
        await SeedPlanningDataAsync();
        using var client = CreateScopedClient(AcceptanceSeedIds.AgencyAId, AcceptanceSeedIds.Br1OrgUnitId);
        using var content = BuildCsvUpload(
            "CostCenter,GradeCode,ApprovedCount,ApprovedFTE,BudgetAmount\nCC-API-02,G5,2,2,200000\n");

        var response = await client.PostAsync("/api/establishment/finance/import?fiscalYear=2026", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedPlanningDataAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (await db.OrgUnits.AnyAsync())
            return;

        TenantIdFlow.Set(AcceptanceSeedIds.AgencyAId);
        try
        {
            var hq = new OrgUnit
            {
                Id = AcceptanceSeedIds.HqOrgUnitId,
                TenantId = AcceptanceSeedIds.AgencyAId,
                Code = "HQ",
                NameAr = "المركزية",
                NameEn = "HQ",
                MaterializedPath = $"/{AcceptanceSeedIds.HqOrgUnitId:D}/"
            };
            var br1 = new OrgUnit
            {
                Id = AcceptanceSeedIds.Br1OrgUnitId,
                TenantId = AcceptanceSeedIds.AgencyAId,
                ParentId = hq.Id,
                Code = "BR1",
                NameAr = "فرع 1",
                NameEn = "Branch 1",
                MaterializedPath = $"/{AcceptanceSeedIds.HqOrgUnitId:D}/{AcceptanceSeedIds.Br1OrgUnitId:D}/"
            };
            var br2 = new OrgUnit
            {
                Id = AcceptanceSeedIds.Br2OrgUnitId,
                TenantId = AcceptanceSeedIds.AgencyAId,
                ParentId = hq.Id,
                Code = "BR2",
                NameAr = "فرع 2",
                NameEn = "Branch 2",
                MaterializedPath = $"/{AcceptanceSeedIds.HqOrgUnitId:D}/{AcceptanceSeedIds.Br2OrgUnitId:D}/"
            };
            db.OrgUnits.AddRange(hq, br1, br2);

            for (var i = 0; i < 5; i++)
            {
                db.FundedPositions.Add(new FundedPosition
                {
                    TenantId = AcceptanceSeedIds.AgencyAId,
                    PositionCode = $"BR1-{i:D3}",
                    GradeCode = "G5",
                    BudgetYear = 2026,
                    Status = FundedPositionStatus.Vacant,
                    OrgUnitId = br1.Id,
                    CostCenter = AcceptanceSeedIds.Br1SampleCostCenter
                });
            }

            db.WorkforcePlans.Add(new WorkforcePlan
            {
                Id = AcceptanceSeedIds.Br1WorkforcePlanId,
                TenantId = AcceptanceSeedIds.AgencyAId,
                Title = "Integration BR1 plan",
                FiscalYear = 2026,
                OrgUnitId = br1.Id,
                Status = "approved",
                ApprovedAt = DateTime.UtcNow,
                Lines =
                {
                    new WorkforcePlanLine { GradeCode = "G5", PlannedCount = 7, PlannedFTE = 7 }
                }
            });

            await db.SaveChangesAsync();
        }
        finally
        {
            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        }
    }

    private HttpClient CreateScopedClient(Guid tenantId, Guid scopeOrgUnitId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeOrgUnitHeader, scopeOrgUnitId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader,
            $"{HrManagementRoles.BranchManager},{HrManagementRoles.HrCore}");
        return client;
    }

    private HttpClient CreateCentralClient(Guid tenantId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader,
            $"{HrManagementRoles.Admin},{HrManagementRoles.HrCore}");
        return client;
    }

    private static MultipartFormDataContent BuildCsvUpload(string csv)
    {
        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");
        return content;
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.File.Exists(_dbPath))
                System.IO.File.Delete(_dbPath);
        }
        catch
        {
            // best effort temp cleanup
        }
    }
}
