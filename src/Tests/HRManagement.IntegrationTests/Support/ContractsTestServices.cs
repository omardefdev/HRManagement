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
/// drifted when <c>PublishSeparationHandler</c> (PLAN-60: ExecuteSeparationHandler) started to need <see cref="IHrCoreActiveRegimeClient"/>; the shared
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
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ExecuteSeparationHandler>());
        // PLAN-60: the one creation path of a separation, and the Separation section's defaults.
        services.AddOptions<SeparationOptions>();
        services.TryAddScoped<SeparationRecordFactory>();
        services.TryAddSingleton<HRManagement.ServiceDefaults.Workflow.CrossService.IWorkflowApprovalClient>(
            new HRManagement.TestSupport.Workflow.FakeWorkflowApprovalClient());
        services.TryAddSingleton<HRManagement.ServiceDefaults.Integration.IHrCoreLookupClient, StubHrCoreLookupClient>();
        services.TryAddScoped<SeparationApprovalService>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<HRManagement.ServiceDefaults.Localization.ICalendarService>(
            _ => new HRManagement.ServiceDefaults.Localization.DamascusCalendarService());
        services.TryAddSingleton<SeparationClock>();
        return services;
    }
}

/// <summary>PLAN-60: names an employee «موظف اختبار» and knows no unit (a host may register its own first).</summary>
public sealed class StubHrCoreLookupClient : HRManagement.ServiceDefaults.Integration.IHrCoreLookupClient
{
    public Task<IReadOnlyList<HRManagement.ServiceDefaults.Integration.HrCoreLookupItem>> GetEmployeesByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HRManagement.ServiceDefaults.Integration.HrCoreLookupItem>>(
            ids.Select(id => new HRManagement.ServiceDefaults.Integration.HrCoreLookupItem(id, "موظف اختبار", null)).ToList());

    public Task<IReadOnlyList<HRManagement.ServiceDefaults.Integration.HrCoreLookupItem>> GetOrgUnitsByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HRManagement.ServiceDefaults.Integration.HrCoreLookupItem>>([]);
}
