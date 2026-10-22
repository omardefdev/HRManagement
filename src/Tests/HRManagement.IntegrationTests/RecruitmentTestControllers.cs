using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.IntegrationTests;

internal static class RecruitmentTestControllers
{
    public static async Task<RecruitmentApplicationsController> CreateApplicationsController(
        HRCoreDbContext db,
        IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().Build();
        var (provider, validator) = await RecruitmentLegalSourceTestSupport.CreateProviderAndValidatorAsync(db);
        return new RecruitmentApplicationsController(
            db,
            null!,
            new RecruitmentApplicationIntakeService(db),
            new RecruitmentSelectionService(db),
            new RecruitmentOfferService(
                db,
                Mock.Of<IPolicyOrchestrator>(),
                Mock.Of<ICodeGenerator>(),
                validator,
                Mock.Of<IWorkflowStarter>(),
                configuration,
                Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
                // HCM-027: these tests do not exercise offer terms; Payroll is reported unavailable.
                new HR.Core.Infrastructure.Integration.UnavailablePayrollGradeLookup()),
            new RecruitmentAppealService(
                db,
                Mock.Of<IPolicyOrchestrator>(),
                Mock.Of<IWorkflowStarter>(),
                configuration,
                Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
                Mock.Of<IWorkflowInstanceActionService>(),
                new SelectionCommitteeService(db),
                new RecruitmentAppealWorkflowHandler(db)),
            new RecruitmentInterviewService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new RecruitmentScoringService(
                db,
                new RecruitmentInterviewService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
                Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            AllowAllAuthorization(),
            provider,
            Mock.Of<IPolicyOrchestrator>(),
            configuration,
            new HirePersonMatchService(db));
    }

    private static Microsoft.AspNetCore.Authorization.IAuthorizationService AllowAllAuthorization()
    {
        var mock = new Mock<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        mock.Setup(a => a.AuthorizeAsync(
                It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<object?>(),
                It.IsAny<string>()))
            .ReturnsAsync(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
        mock.Setup(a => a.AuthorizeAsync(
                It.IsAny<System.Security.Claims.ClaimsPrincipal>(),
                It.IsAny<object?>(),
                It.IsAny<IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement>>()))
            .ReturnsAsync(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
        return mock.Object;
    }
}
