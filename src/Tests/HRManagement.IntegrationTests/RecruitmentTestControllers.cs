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
                configuration),
            new RecruitmentAppealService(
                db,
                Mock.Of<IPolicyOrchestrator>(),
                Mock.Of<IWorkflowStarter>(),
                configuration),
            new RecruitmentAppealWorkflowUiService(db),
            new RecruitmentInterviewService(db),
            Mock.Of<IWorkflowInstanceActionService>(),
            provider,
            Mock.Of<IPolicyOrchestrator>(),
            configuration);
    }
}
