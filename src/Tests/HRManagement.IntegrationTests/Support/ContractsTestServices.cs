using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Infrastructure.Repositories;
using HR.Contracts.Infrastructure.Services;
using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HRManagement.IntegrationTests.Support;

/// <summary>
/// QA-034: the HR.Contracts application layer for integration hosts in one place: repository, MediatR handlers of
/// HR.Contracts.Application, the lifecycle gate, the deterministic code-generation and policy clients and the HR.Core
/// active-regime client. Hosts that hand-built this list
/// drifted when <c>PublishSeparationHandler</c> started to need <see cref="IHrCoreActiveRegimeClient"/>; the shared
/// hosts build with ValidateOnBuild, so a new dependency now fails <c>IntegrationHostValidationTests</c> by name.
/// Registrations made by the host before this call win (TryAdd), e.g. a spy publisher or a mock gate.
/// </summary>
public static class ContractsTestServices
{
    public static IServiceCollection AddContractsApplicationForTests(
        this IServiceCollection services,
        IHrCoreActiveRegimeClient? activeRegimeClient = null)
    {
        IntegrationTestHelpers.AddDeterministicServiceClients(services);
        services.TryAddScoped<IContractsRepository, ContractsRepository>();
        services.TryAddSingleton<IEmployeeLifecycleGate, PassThroughEmployeeLifecycleGate>();
        services.TryAddSingleton(activeRegimeClient ?? new FakeHrCoreActiveRegimeClient());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PublishSeparationHandler>());
        return services;
    }
}
