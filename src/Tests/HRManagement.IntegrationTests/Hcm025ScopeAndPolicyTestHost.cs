using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
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

/// <summary>PLAN-25 (P5): the in-process HR.Core host on a per-class SQLite file, an HQ with two branches, and HTTP helpers.</summary>
public sealed class Hcm025ScopeAndPolicyTestHost : IAsyncLifetime
{
    private static readonly Regex AntiforgeryTokenPattern = new(
        "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hr-hcm025-p5-{Guid.NewGuid():N}.db");

    public WebApplicationFactory<HRCoreWebProgram> Host { get; private set; } = null!;
    public Guid Hq { get; private set; }
    public Guid Branch1 { get; private set; }
    public Guid Branch2 { get; private set; }

    public async Task InitializeAsync()
    {
        Host = new WebApplicationFactory<HRCoreWebProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false");
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
                    ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
                    ["NationalScope:IsLabScale"] = "false"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });

        await WithDbAsync(async db =>
        {
            await db.Database.EnsureCreatedAsync();
            await LegislationRegisterSeeder.SeedAsync(db);
            await WorkflowPackSeeder.SeedAsync(db);
            var hq = Unit("HQ", null, null);
            var b1 = Unit("B1", hq.Id, hq.MaterializedPath);
            var b2 = Unit("B2", hq.Id, hq.MaterializedPath);
            db.OrgUnits.AddRange(hq, b1, b2);
            await db.SaveChangesAsync();
            (Hq, Branch1, Branch2) = (hq.Id, b1.Id, b2.Id);
            return true;
        });
    }

    private static OrgUnit Unit(string code, Guid? parent, string? parentPath)
    {
        var unit = new OrgUnit
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Code = $"{code}-P25-{Guid.NewGuid():N}"[..14],
            NameAr = code,
            NameEn = code,
            ParentId = parent
        };
        unit.MaterializedPath = $"{parentPath ?? "/"}{unit.Id:D}/";
        return unit;
    }

    public async Task<RecruitmentRequisition> SeedRequisitionAsync(Guid orgUnitId, RequisitionStatus status = RequisitionStatus.Draft) =>
        await WithDbAsync(async db =>
        {
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var position = new FundedPosition
            {
                TenantId = TenantDefaults.DefaultTenantId,
                PositionCode = $"VAC-P25-{suffix}",
                GradeCode = "G5",
                BudgetYear = 2026,
                Status = FundedPositionStatus.Vacant,
                OrgUnitId = orgUnitId
            };
            db.FundedPositions.Add(position);
            var requisition = new RecruitmentRequisition
            {
                TenantId = TenantDefaults.DefaultTenantId,
                RequisitionNumber = $"REQ-P25-{suffix}",
                FundedPositionId = position.Id,
                JobTitle = "محلل",
                Status = status,
                RequestedDate = DateTime.UtcNow,
                VacancyCount = 1,
                LegalSourceId = Syr001LegislationIds.Law50_2004
            };
            db.RecruitmentRequisitions.Add(requisition);
            await db.SaveChangesAsync();
            return requisition;
        });

    public async Task<T> WithDbAsync<T>(Func<HRCoreDbContext, Task<T>> query)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            return await query(scope.ServiceProvider.GetRequiredService<HRCoreDbContext>());
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    public HttpClient Client(Guid userId, string roles, Guid? scopeOrgUnitId = null)
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, TenantDefaults.DefaultTenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        if (scopeOrgUnitId is Guid scope)
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeOrgUnitHeader, scope.ToString("D"));
        return client;
    }

    public static async Task<string> TokenAsync(HttpClient client, string pageUrl)
    {
        var page = await client.GetAsync(pageUrl);
        var raw = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {pageUrl} returned {(int)page.StatusCode}");
        var match = AntiforgeryTokenPattern.Match(raw);
        Assert.True(match.Success, $"No antiforgery token on {pageUrl}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string token, params (string Key, string Value)[] fields)
    {
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
        form.Add(new("__RequestVerificationToken", token));
        return client.PostAsync(url, new FormUrlEncodedContent(form));
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            if (global::System.IO.File.Exists(_dbPath))
                global::System.IO.File.Delete(_dbPath);
        }
        catch
        {
            // best effort temp cleanup
        }
    }
}
