using System.Net;
using System.Security.Claims;
using HRManagement.Gateway.Controllers;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.Web.Shared.Html;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-67 (D-67-3, P-6, G-UX-04): the gateway's registry of user names. It records a signed-in user from the token's claims, once an hour,
/// never a service account; its lookup answers interactive staff only (PLAN-50's definition), names of the caller's agency only
/// (all agencies for platform-admin), at most 50 ids, the name only; and a page shows «النظام», a name, or «مستخدم غير معروف» with the id.
/// </summary>
public sealed class Ux067UserNamesTests
{
    private static readonly Guid AgencyA = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid AgencyB = Guid.Parse("00000000-0000-4000-8000-0000000000b2");
    private const string SubA = "6f1c2a40-1111-4a7e-9a10-00000000000a";
    private const string SubB = "6f1c2a40-2222-4a7e-9a10-00000000000b";

    private static ClaimsPrincipal User(string? sub, string? name, Guid? tenant, params string[] roles)
    {
        var claims = new List<Claim>();
        if (sub is not null) claims.Add(new Claim("sub", sub));
        if (name is not null) claims.Add(new Claim("name", name));
        if (tenant is { } t) claims.Add(new Claim(TenantContext.TenantClaimType, t.ToString("D")));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", ClaimTypes.Role));
    }

    private static (ServiceProvider Services, string Database) Provider()
    {
        var database = "ux067-names-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection()
            .AddDbContext<GatewayAuditDbContext>(o => o.UseInMemoryDatabase(database))
            .AddMemoryCache()
            .AddLogging()
            .AddSingleton<GatewayUserNameRegistry>()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHrManagementAuthorizationPolicies();
        return (services.BuildServiceProvider(), database);
    }

    [Fact]
    public async Task A_sign_in_records_the_name_and_agency_once_and_a_new_name_updates_it()
    {
        var (sp, _) = Provider();
        var registry = sp.GetRequiredService<GatewayUserNameRegistry>();

        await registry.RecordAsync(User(SubA, "سارة الحلبي", AgencyA, "hr-core"), default);
        await registry.RecordAsync(User(SubA, "سارة الحلبي", AgencyA, "hr-core"), default);
        using (var scope = sp.CreateScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>().UserNames.ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(("سارة الحلبي", AgencyA), (row.DisplayName, row.TenantId));
        }

        await registry.RecordAsync(User(SubA, "سارة الحلبي العلي", AgencyA, "hr-core"), default);
        using (var scope = sp.CreateScope())
            Assert.Equal("سارة الحلبي العلي", (await scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>().UserNames.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task A_service_account_and_a_user_without_a_name_are_never_recorded()
    {
        var (sp, _) = Provider();
        var registry = sp.GetRequiredService<GatewayUserNameRegistry>();
        var service = User(SubB, "service-account-svc-payroll", AgencyA);
        ((ClaimsIdentity)service.Identity!).AddClaim(new Claim("azp", ServiceClientIds.All.First()));

        await registry.RecordAsync(service, default);
        await registry.RecordAsync(User(SubB, null, AgencyA, "hr-core"), default);

        using var scope = sp.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>().UserNames.ToListAsync());
    }

    [Theory]
    [InlineData("staff", true)]
    [InlineData("employee", false)]
    [InlineData("service", false)]
    [InlineData("no-tenant", false)]
    [InlineData("platform-admin", true)]
    public async Task The_lookup_is_for_interactive_staff_only(string who, bool allowed)
    {
        var (sp, _) = Provider();
        var user = who switch
        {
            "staff" => User(SubA, "a", AgencyA, HrManagementRoles.HrCore),
            "employee" => User(SubA, "a", AgencyA, HrManagementRoles.HrEmployee),
            "no-tenant" => User(SubA, "a", null, HrManagementRoles.HrCore),
            "platform-admin" => User(SubA, "a", AgencyA, HrManagementRoles.PlatformAdmin),
            _ => User(SubA, "service-account-svc-hr-core", AgencyA, HrManagementRoles.HrCore)
        };
        if (who == "service")
            ((ClaimsIdentity)user.Identity!).AddClaim(new Claim("azp", ServiceClientIds.All.First()));

        var result = await sp.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, HrManagementPolicies.GatewayUserNames);
        Assert.Equal(allowed, result.Succeeded);
    }

    private static async Task<(int Status, IReadOnlyList<UserNamesController.UserNameItem> Items)> LookupAsync(ServiceProvider sp, ClaimsPrincipal user, IReadOnlyList<string> ids)
    {
        using var scope = sp.CreateScope();
        var controller = new UserNamesController(scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        var answer = await controller.ByIds(new UserNamesController.UserNamesRequest(ids), default);
        return answer.Result switch
        {
            OkObjectResult { Value: UserNamesController.UserNamesResponse body } => (200, body.Items),
            ObjectResult o => (o.StatusCode ?? 0, []),
            StatusCodeResult s => (s.StatusCode, []),
            ForbidResult => (403, []),
            _ => (0, [])
        };
    }

    [Fact]
    public async Task The_lookup_answers_the_callers_agency_only_and_platform_admin_all()
    {
        var (sp, _) = Provider();
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
            db.UserNames.AddRange(
                new GatewayUserName { Sub = SubA, DisplayName = "سارة الحلبي", TenantId = AgencyA },
                new GatewayUserName { Sub = SubB, DisplayName = "موظف جهة أخرى", TenantId = AgencyB });
            await db.SaveChangesAsync();
        }

        var (status, items) = await LookupAsync(sp, User("x", "x", AgencyA, HrManagementRoles.HrCore), [SubA, SubB, "unknown-id"]);
        Assert.Equal(200, status);
        var only = Assert.Single(items);
        Assert.Equal((SubA, "سارة الحلبي"), (only.Id, only.Label));

        var (_, all) = await LookupAsync(sp, User("x", "x", AgencyA, HrManagementRoles.PlatformAdmin), [SubA, SubB]);
        Assert.Equal(2, all.Count);

        var (tooMany, _) = await LookupAsync(sp, User("x", "x", AgencyA, HrManagementRoles.HrCore), Enumerable.Range(0, 51).Select(i => i.ToString()).ToList());
        Assert.Equal(400, tooMany);
    }

    [Fact]
    public async Task The_gateway_maps_the_lookup_under_the_user_names_policy()
    {
        await using var factory = new GatewayTestFactory();
        _ = factory.Server;
        var endpoint = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "api/user-names/by-ids");
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == HrManagementPolicies.GatewayUserNames);
        Assert.DoesNotContain(endpoint.Metadata, m => m is IAllowAnonymous);
        Assert.Equal(["POST"], endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods);
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData("system", "<span class=\"hr-user-system\">النظام</span>")]
    [InlineData("hradmin", "hradmin")]
    [InlineData(SubA, "<span class=\"hr-user-name\" data-hr-user-id=\"" + SubA + "\" title=\"" + SubA + "\">مستخدم غير معروف</span>")]
    public void A_page_shows_the_system_a_name_or_an_unknown_user_with_the_id_in_a_tooltip(string? value, string html)
    {
        using var writer = new StringWriter();
        HrNameHtmlExtensions.HrUserName(null!, value).WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        Assert.Equal(html, writer.ToString());
    }

    [Theory]
    [InlineData("law50", "نظام القانون رقم 50")]
    [InlineData("SYR-LAW-50-2004", "القانون رقم 50 لعام 2004")]
    [InlineData("special_judiciary", "نظام خاص — القضاء")]
    public void A_legal_code_reads_by_its_arabic_title(string code, string title) =>
        Assert.Equal(title, HrNameHtmlExtensions.LegalTitleAr(code));
}
