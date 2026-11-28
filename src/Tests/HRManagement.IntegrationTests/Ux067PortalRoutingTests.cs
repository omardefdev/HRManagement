using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
public sealed class Ux067PortalRoutingTests(Ux067PortalHost portal) : IClassFixture<Ux067PortalHost>
{
    private HttpClient Client(bool signedIn) => portal.Client(signedIn);

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

    // ── FIX-CAREERS-REF: HR.Core's flash cookie, through the gateway's careers filter, reaches the confirmation page ──────────

    [Fact]
    public async Task The_confirmation_page_shows_the_reference_number_the_gateway_now_forwards_and_ends_it()
    {
        const string reference = "CA-FIXREF-TEST-0001"; // a test value, not a generated number
        var flash = portal.FlashCookie("Reference", reference);
        // The candidate's browser holds staff and gateway cookies beside HR.Core's flash cookie; the gateway decides what HR.Core sees.
        var forwarded = GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader(
            $".AspNetCore.Cookies=staff; KEYCLOAK_SESSION=kc; {flash}; .AspNetCore.Mvc.CookieTempData=near", forwardCareersSession: true);
        Assert.Equal(flash, forwarded);

        using var client = Client(signedIn: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/careers/submitted");
        request.Headers.Add("Cookie", forwarded);
        using var response = await client.SendAsync(request);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(reference, ReferenceBadge(body));
        Assert.DoesNotContain("data-reference-unavailable", body, StringComparison.Ordinal);
        // Read once: the page ends the flash cookie (scoped to /careers), so a reload shows no number.
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c =>
            c.StartsWith(GatewayUpstreamCookieForwarding.TempDataCookieName + "=;", StringComparison.Ordinal)
            && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)
            && c.Contains("path=/careers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_confirmation_page_without_a_number_says_so_and_shows_no_empty_badge()
    {
        using var client = Client(signedIn: false);
        using var response = await client.GetAsync("/careers/submitted");
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(ReferenceBadge(body));
        Assert.DoesNotContain("class=\"careers-badge\"", body, StringComparison.Ordinal);
        Assert.Contains("data-reference-unavailable", body, StringComparison.Ordinal);
        Assert.Contains("يظهر الرقم المرجعي مرة واحدة فقط", body, StringComparison.Ordinal);
        Assert.Contains("مكتب التوظيف في الجهة المعلنة عن الوظيفة", body, StringComparison.Ordinal);
        // This host runs as Development, whose candidate notification provider is FileDrop: the page may promise the message.
        Assert.Contains("فستصلك رسالة تتضمن الرقم المرجعي", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_in_the_forwarded_flash_cookie_is_shown_once_on_the_page_it_lands_on()
    {
        const string refusal = "الإعلان غير متاح للتقديم";
        var forwarded = GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader(
            $"KEYCLOAK_SESSION=kc; {portal.FlashCookie("Error", refusal)}", forwardCareersSession: true);

        using var client = Client(signedIn: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/careers");
        request.Headers.Add("Cookie", forwarded!);
        using var response = await client.SendAsync(request);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Regex.Matches(body, Regex.Escape(refusal)));
        Assert.Contains("alert alert-danger", body, StringComparison.Ordinal);
    }

    /// <summary>The text of the confirmation page's reference badge, or null when the page shows none.</summary>
    private static string? ReferenceBadge(string html)
    {
        var badge = Regex.Match(html, @"<span class=""careers-badge"" data-reference-number[^>]*>\s*<bdi[^>]*>([^<]*)</bdi>");
        return badge.Success ? badge.Groups[1].Value.Trim() : null;
    }
}

/// <summary>
/// One HR.Core host for the class (xUnit runs <see cref="IAsyncLifetime"/> of a test class once per test): each host hands EF its own
/// logger factory, so each adds an internal service provider, and EF throws past twenty in one test process.
/// </summary>
public sealed class Ux067PortalHost : IAsyncLifetime
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

    /// <summary>
    /// FIX-CAREERS-REF: HR.Core's own flash cookie (<c>name=value</c>) under the careers path base, as HR.Core writes it after an
    /// apply or a refusal, protected by this host's keys.
    /// </summary>
    public string FlashCookie(string key, string value)
    {
        using var scope = _host.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.PathBase = ProductionHostExtensions.PublicCareersGatewayPrefix;
        scope.ServiceProvider.GetRequiredService<ITempDataProvider>()
            .SaveTempData(context, new Dictionary<string, object> { [key] = value });
        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.StartsWith(GatewayUpstreamCookieForwarding.TempDataCookieName + "=", setCookie, StringComparison.Ordinal);
        Assert.Contains("path=/careers", setCookie, StringComparison.OrdinalIgnoreCase);
        return setCookie.Split(';', 2)[0];
    }

    public HttpClient Client(bool signedIn)
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
}
