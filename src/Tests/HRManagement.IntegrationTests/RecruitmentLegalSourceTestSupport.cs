using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.IntegrationTests;

internal static class RecruitmentLegalSourceTestSupport
{
    public static async Task SeedLegislationAndRegimesAsync(HRCoreDbContext db)
    {
        await LegislationRegisterSeeder.SeedAsync(db);
        var provider = new LegislationRegisterProvider(db);
        await ServiceRegimeSeeder.SeedAsync(db, provider);
    }

    public static async Task<RecruitmentLegalSourceValidator> CreateValidatorAsync(HRCoreDbContext db)
    {
        await SeedLegislationAndRegimesAsync(db);
        var provider = new LegislationRegisterProvider(db);
        return new RecruitmentLegalSourceValidator(provider);
    }

    public static async Task<(LegislationRegisterProvider Provider, RecruitmentLegalSourceValidator Validator)>
        CreateProviderAndValidatorAsync(HRCoreDbContext db)
    {
        await SeedLegislationAndRegimesAsync(db);
        var provider = new LegislationRegisterProvider(db);
        return (provider, new RecruitmentLegalSourceValidator(provider));
    }

    public static async Task<(ServiceRegimeValidator RegimeValidator, ServiceRegimeProvider RegimeProvider)>
        CreateRegimeSupportAsync(HRCoreDbContext db)
    {
        await SeedLegislationAndRegimesAsync(db);
        var legislationProvider = new LegislationRegisterProvider(db);
        var legalValidator = new RecruitmentLegalSourceValidator(legislationProvider);
        return (new ServiceRegimeValidator(db, legalValidator), new ServiceRegimeProvider(db));
    }

    public static async Task<(ServiceRegimeValidator RegimeValidator, ServiceRegimeExceptionService ExceptionService)>
        CreateRegimeGovernanceAsync(HRCoreDbContext db)
    {
        var (regimeValidator, _) = await CreateRegimeSupportAsync(db);
        var transitionService = new ServiceRegimeTransitionService(db, regimeValidator);
        var exceptionService = new ServiceRegimeExceptionService(db, regimeValidator, transitionService);
        return (regimeValidator, exceptionService);
    }

    public static async Task<RecruitmentHireClosureService> CreateHireClosureServiceAsync(
        HRCoreDbContext db,
        IPublishEndpoint publish,
        IConfiguration config,
        ILogger<RecruitmentHireClosureService>? logger = null)
    {
        var validator = await CreateValidatorAsync(db);
        var (regimeValidator, exceptionService) = await CreateRegimeGovernanceAsync(db);
        return new RecruitmentHireClosureService(
            db,
            new EstablishmentService(),
            new FundedPositionVersionService(db),
            new RequisitionCloseService(),
            validator,
            regimeValidator,
            exceptionService,
            new ReserveListService(
                db,
                Microsoft.Extensions.Options.Options.Create(
                    config.GetSection(RecruitmentOptions.SectionName).Get<RecruitmentOptions>() ?? new RecruitmentOptions()),
                NullLogger<ReserveListService>.Instance),
            publish,
            config,
            logger ?? NullLogger<RecruitmentHireClosureService>.Instance);
    }
}
