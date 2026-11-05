using System.Security.Claims;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 §6 (G-COD-05, D-S2S-3): only the owning service issues a number, in the tenant of the request.</summary>
public class S2s042CodeGenerationAuthTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (CodeGenerationInternalController Controller, Mock<ICodeGenerator> Generator) Create(string clientId, Guid? tenant)
    {
        var generator = new Mock<ICodeGenerator>();
        generator.Setup(g => g.GenerateForServiceAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "X-1" });
        generator.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "X-1", IsPreview = true });
        var controller = new CodeGenerationInternalController(generator.Object, Mock.Of<ITenantContext>(t => t.TenantId == tenant))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", clientId)], "Bearer")) }
            }
        };
        return (controller, generator);
    }

    [Theory]
    [InlineData("svc-admin-logistics", CodeEntityKind.AssetTag)]
    [InlineData("svc-admin-logistics", CodeEntityKind.InventorySku)]
    [InlineData("svc-it-infra", CodeEntityKind.ItAssetTag)]
    [InlineData("svc-records-archive", CodeEntityKind.ArchiveReference)]
    [InlineData("svc-compliance", CodeEntityKind.LegalCase)]
    [InlineData("svc-hr-contracts", CodeEntityKind.ServiceTransaction)]
    [InlineData("svc-hr-contracts", CodeEntityKind.AdministrativeDecision)]
    [InlineData("svc-idcard", CodeEntityKind.IdCardSerial)]
    public async Task The_owner_of_the_kind_gets_a_number_recorded_under_its_name(string client, CodeEntityKind kind)
    {
        var (controller, generator) = Create(client, Tenant);

        Assert.IsType<OkObjectResult>(await controller.Generate(new CodeGenerationRequest { EntityKind = kind }, CancellationToken.None));

        generator.Verify(g => g.GenerateForServiceAsync(It.IsAny<CodeGenerationRequest>(), client, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("svc-admin-logistics", CodeEntityKind.AdministrativeDecision)]
    [InlineData("svc-hr-contracts", CodeEntityKind.AssetTag)]
    [InlineData("svc-hr-core", CodeEntityKind.OfferLetter)]
    [InlineData("svc-idcard", CodeEntityKind.CitizenReference)]
    public async Task A_kind_the_client_does_not_own_is_403_and_consumes_nothing(string client, CodeEntityKind kind)
    {
        var (controller, generator) = Create(client, Tenant);

        Assert.IsType<ForbidResult>(await controller.Generate(new CodeGenerationRequest { EntityKind = kind }, CancellationToken.None));

        generator.Verify(g => g.GenerateForServiceAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_tenant_in_the_body_is_overwritten_by_the_tenant_of_the_request()
    {
        var (controller, generator) = Create("svc-admin-logistics", Tenant);
        var request = new CodeGenerationRequest { EntityKind = CodeEntityKind.AssetTag, TenantId = Other, Preview = true };

        await controller.Generate(request, CancellationToken.None);

        Assert.Equal(Tenant, request.TenantId);
        Assert.False(request.Preview);
        generator.Verify(g => g.GenerateForServiceAsync(request, "svc-admin-logistics", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_tenant_on_the_request_is_400_whatever_the_body_says()
    {
        var (controller, generator) = Create("svc-admin-logistics", null);

        var result = await controller.Generate(new CodeGenerationRequest { EntityKind = CodeEntityKind.AssetTag, TenantId = Other }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        generator.Verify(g => g.GenerateForServiceAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_internal_preview_is_a_preview_and_still_checks_the_owner()
    {
        var (controller, generator) = Create("svc-admin-logistics", Tenant);
        var request = new CodeGenerationRequest { EntityKind = CodeEntityKind.AssetTag };

        await controller.Preview(request, CancellationToken.None);

        Assert.True(request.Preview);
        generator.Verify(g => g.GenerateForServiceAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsType<ForbidResult>(await controller.Preview(new CodeGenerationRequest { EntityKind = CodeEntityKind.LegalCase }, CancellationToken.None));
    }

    [Fact]
    public void Users_have_no_generate_action_left()
    {
        Assert.DoesNotContain("Generate", typeof(CodeGenerationController).GetMethods().Select(m => m.Name));
        var routes = typeof(CodeGenerationController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().ToList();
        Assert.Equal("api/codes", Assert.Single(routes).Template);
    }

    [Fact]
    public async Task The_user_preview_runs_in_the_tenant_of_the_user()
    {
        var generator = new Mock<ICodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "X-1", IsPreview = true });
        var controller = new CodeGenerationController(generator.Object, null!, Mock.Of<ITenantContext>(t => t.TenantId == Tenant));
        var request = new CodeGenerationRequest { EntityKind = CodeEntityKind.AssetTag, TenantId = Other, Preview = false };

        await controller.Preview(request, CancellationToken.None);

        Assert.Equal(Tenant, request.TenantId);
        Assert.True(request.Preview);
    }

    private static async Task<bool> Passes(params Claim[] claims)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddServiceToServiceAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        return (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, ServicePolicies.CodeGenerate)).Succeeded;
    }

    [Fact]
    public async Task The_policy_needs_a_service_account_the_workflow_scope_and_an_owner()
    {
        Assert.True(await Passes(new Claim("azp", "svc-admin-logistics"), new Claim("scope", "hr-core.workflow")));
        Assert.False(await Passes(new Claim("azp", "svc-admin-logistics"), new Claim("scope", "hr-core.lookup")));
        Assert.False(await Passes(new Claim("azp", "svc-hr-health"), new Claim("scope", "hr-core.workflow")));
        Assert.False(await Passes(new Claim("azp", "hr-gateway-client"), new Claim("scope", "hr-core.workflow")));
    }
}
