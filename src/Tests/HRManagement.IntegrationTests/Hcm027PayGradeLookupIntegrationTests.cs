using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using Payroll.Web.Controllers;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-027 (E2): Payroll <c>GET api/internal/pay-grades/{gradeCode}?asOf=</c> over HTTP (TestServer) against a migrated
/// Payroll database (Testcontainers PostgreSQL): S2S policy, tenant from <c>X-Tenant-ID</c>, active scale at the date.
/// Skips when Docker is unavailable.
/// </summary>
public sealed class Hcm027PayGradeLookupIntegrationTests(Hcm027PayrollFixture fixture) : IClassFixture<Hcm027PayrollFixture>
{
    private const string HrCoreWithScope = "svc-hr-core|payroll.grades.read";

    [SkippableFact]
    public async Task Returns_grade_of_the_active_scale_effective_at_the_date()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        await fixture.SeedScalesAsync(tenant);

        var current = await GetAsync(tenant, "G5", "2026-11-20", HrCoreWithScope);
        await AssertStatusAsync(HttpStatusCode.OK, current);
        var body = await current.Content.ReadFromJsonAsync<PayGradeResponse>();
        Assert.Equal("G5", body!.GradeCode);
        Assert.Equal(4000m, body.MinSalary);
        Assert.Equal(6000m, body.MaxSalary);
        Assert.Equal(4500m, body.BaseSalary);
        Assert.Equal(Hcm027PayrollFixture.ScaleCode("SC-2026", tenant), body.SalaryScaleCode);

        var previous = await (await GetAsync(tenant, "G5", "2025-06-01", HrCoreWithScope)).Content.ReadFromJsonAsync<PayGradeResponse>();
        Assert.Equal(Hcm027PayrollFixture.ScaleCode("SC-2025", tenant), previous!.SalaryScaleCode);
        Assert.Equal(3000m, previous.MinSalary);
    }

    [SkippableFact]
    public async Task Inactive_or_not_yet_effective_scale_is_ignored()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        await fixture.SeedScalesAsync(tenant);

        // SC-INACTIVE (IsActive=false) is the latest scale; SC-2027 starts later than the date.
        var body = await (await GetAsync(tenant, "G5", "2026-12-31", HrCoreWithScope)).Content.ReadFromJsonAsync<PayGradeResponse>();
        Assert.Equal(Hcm027PayrollFixture.ScaleCode("SC-2026", tenant), body!.SalaryScaleCode);
    }

    [SkippableFact]
    public async Task Grade_of_another_tenant_is_not_visible()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var owner = Guid.NewGuid();
        await fixture.SeedScalesAsync(owner);

        var otherTenant = await GetAsync(Guid.NewGuid(), "G5", "2026-11-20", HrCoreWithScope);
        var noTenant = await GetAsync(null, "G5", "2026-11-20", HrCoreWithScope);

        Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noTenant.StatusCode);
    }

    [SkippableFact]
    public async Task Unknown_grade_returns_404_and_bad_date_returns_400()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        await fixture.SeedScalesAsync(tenant);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(tenant, "G99", "2026-11-20", HrCoreWithScope)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(tenant, "G5", "20-11-2026", HrCoreWithScope)).StatusCode);
    }

    [SkippableTheory]
    [InlineData("svc-hr-core|file-storage.references")]   // right client, missing scope
    [InlineData("svc-payroll|payroll.grades.read")]       // scope, wrong client
    [InlineData("svc-compliance|payroll.grades.read dsar.subject.read")]
    [InlineData("user")]                                   // interactive user
    public async Task Unauthorised_caller_is_forbidden(string principal)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        await fixture.SeedScalesAsync(tenant);

        var response = await GetAsync(tenant, "G5", "2026-11-20", principal);

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
    }

    [SkippableFact]
    public async Task Anonymous_caller_is_challenged()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);

        var response = await GetAsync(Guid.NewGuid(), "G5", "2026-11-20", principal: null);

        await AssertStatusAsync(HttpStatusCode.Unauthorized, response);
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
            Assert.Fail($"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<HttpResponseMessage> GetAsync(Guid? tenantId, string grade, string asOf, string? principal)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/pay-grades/{grade}?asOf={asOf}");
        if (tenantId is Guid tenant)
            request.Headers.Add(TenantIdMiddleware.HeaderName, tenant.ToString("D"));
        if (principal is not null)
            request.Headers.Add(TestPrincipalHandler.HeaderName, principal);
        return await fixture.Client.SendAsync(request);
    }

    private sealed record PayGradeResponse(
        string GradeCode,
        string? GradeNameAr,
        decimal MinSalary,
        decimal MaxSalary,
        decimal BaseSalary,
        string SalaryScaleCode,
        DateTime ScaleEffectiveFrom,
        DateTime? ScaleEffectiveTo);
}

/// <summary>HCM-027: migrated Payroll PostgreSQL + a TestServer exposing only the pay-grade lookup controller.</summary>
public sealed class Hcm027PayrollFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private IHost? _app;

    public bool Available { get; private set; }
    public string UnavailableReason { get; private set; } = "Docker/PostgreSQL unavailable.";
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
            await using (var db = CreateContext())
                await db.Database.MigrateAsync();

            _app = BuildHost(_postgres.GetConnectionString());
            await _app.StartAsync();
            Client = _app.GetTestClient();
            Available = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Docker/PostgreSQL unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            _app.Dispose();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    /// <summary>G5 in SC-2025 (3000–5000), SC-2026 (4000–6000), SC-2027 (from 2027), SC-INACTIVE (latest, inactive).</summary>
    public async Task SeedScalesAsync(Guid tenantId)
    {
        await using var db = CreateContext();
        AddScale(db, tenantId, "SC-2025", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31), true, 3000, 5000);
        AddScale(db, tenantId, "SC-2026", new DateTime(2026, 1, 1), null, true, 4000, 6000);
        AddScale(db, tenantId, "SC-2027", new DateTime(2027, 1, 1), null, true, 7000, 8000);
        AddScale(db, tenantId, "SC-INACTIVE", new DateTime(2026, 6, 1), null, false, 9000, 10000);
        await db.SaveChangesAsync();
    }

    private static void AddScale(
        PayrollDbContext db, Guid tenantId, string code, DateTime from, DateTime? to, bool active, decimal min, decimal max)
    {
        var scale = new SalaryScale
        {
            TenantId = tenantId,
            // IX_SalaryScales_Code is unique across tenants, so codes carry the tenant.
            Code = ScaleCode(code, tenantId),
            NameAr = $"سلّم {code}",
            EffectiveFrom = DateTime.SpecifyKind(from, DateTimeKind.Utc),
            EffectiveTo = to is null ? null : DateTime.SpecifyKind(to.Value, DateTimeKind.Utc),
            IsActive = active
        };
        scale.PayGrades.Add(new PayGrade
        {
            TenantId = tenantId,
            SalaryScaleId = scale.Id,
            GradeCode = "G5",
            GradeNameAr = "الدرجة الخامسة",
            BaseSalary = min + 500,
            MinSalary = min,
            MaxSalary = max
        });
        db.SalaryScales.Add(scale);
    }

    public static string ScaleCode(string code, Guid tenantId) => $"{code}-{tenantId:N}";

    private PayrollDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString(), npgsql =>
                npgsql.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .Options);

    /// <summary>
    /// A bare host (no WebApplication defaults such as IIS integration or host filtering) with only what the
    /// endpoint needs in Payroll: S2S policies, TenantIdMiddleware, tenant-aware PayrollDbContext.
    /// </summary>
    private static IHost BuildHost(string connectionString) =>
        new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers()
                        .ConfigureApplicationPartManager(manager =>
                        {
                            manager.ApplicationParts.Add(new AssemblyPart(typeof(PayGradeLookupInternalController).Assembly));
                            manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(PayGradeLookupInternalController)));
                        });
                    services.AddAuthentication(TestPrincipalHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestPrincipalHandler>(TestPrincipalHandler.SchemeName, _ => { });
                    services.AddServiceToServiceAuthorizationPolicies();
                    services.AddHttpContextAccessor();
                    services.AddTenantContext();
                    services.AddDbContext<PayrollDbContext>((sp, options) =>
                        options.UseNpgsql(connectionString)
                            .AddInterceptors(sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseHrTenantId();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }))
            .Build();

    /// <summary>Exposes only the controller under test from the Payroll.Web assembly.</summary>
    private sealed class SingleControllerFeatureProvider(Type controller) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(controller.GetTypeInfo());
        }
    }
}

/// <summary>
/// Test authentication: <c>X-Test-Principal: clientId|scope1 scope2</c> builds a Keycloak-like service-account
/// principal; <c>user</c> builds an interactive user; no header leaves the request anonymous.
/// </summary>
internal sealed class TestPrincipalHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string HeaderName = "X-Test-Principal";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(AuthenticateResult.NoResult());

        List<Claim> claims;
        if (value == "user")
        {
            claims = [new(ClaimTypes.NameIdentifier, "user-1"), new("preferred_username", "alice"), new(ClaimTypes.Role, "hr-core")];
        }
        else
        {
            var parts = value.Split('|', 2);
            claims =
            [
                new("preferred_username", $"service-account-{parts[0]}"),
                new("azp", parts[0]),
                new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("N"))
            ];
            if (parts.Length > 1)
                claims.Add(new Claim("scope", parts[1]));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
