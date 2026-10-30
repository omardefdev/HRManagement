using HR.Contracts.Application.Abstractions;
using HR.Contracts.Infrastructure.Data;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Data;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Establishment;
using HRManagement.IntegrationTests.Support;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Payroll.Application.Abstractions;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>
/// QA-034 (AC-QA-34f): the shared integration hosts build with ValidateScopes + ValidateOnBuild and register the
/// services their production paths need. Some of those are resolved optionally at run time (Payroll asks for
/// <see cref="IHrCoreServiceRegimeClient"/> with GetService and then refuses to calculate), so a missing one never
/// fails the container build; this test names it instead of letting ten payroll tests fail with a business message.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class IntegrationHostValidationTests
{
    private sealed record Host(IServiceCollection Services, Type[] Required, List<IDisposable> Owned);

    private static SqliteConnection OpenSqlite()
    {
        var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        return c;
    }

    private static Host Contracts()
    {
        var connection = OpenSqlite();
        var services = new ServiceCollection();
        services.AddLogging();
        IntegrationTestHelpers.AddDeterministicServiceClients(services);
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton(Mock.Of<IEventPublisher>());
        services.AddContractsApplicationForTests();
        return new Host(services, [typeof(IHrCoreActiveRegimeClient), typeof(IContractsRepository), typeof(IMediator), typeof(IEmployeeLifecycleGate)], [connection]);
    }

    private static Host Payroll()
    {
        var connection = OpenSqlite();
        var services = new ServiceCollection();
        services.AddLogging();
        IntegrationTestHelpers.AddDeterministicServiceClients(services);
        IntegrationTestHelpers.AddSqliteDbContext<PayrollDbContext>(services, connection);
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
        services.AddMassTransitTestHarness();
        return new Host(services, [typeof(IHrCoreServiceRegimeClient), typeof(IPayrollRepository)], [connection]);
    }

    private static Host Establishment()
    {
        var connection = OpenSqlite();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<HRCoreDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<EstablishmentService>();
        services.AddHrEstablishmentTemporal();
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<ProcessPartyAckConsumers>());
        return new Host(services, [typeof(IFundedPositionVersionService)], [connection]);
    }

    /// <summary>Problems of a host: build errors (ValidateOnBuild) and required services that do not resolve.</summary>
    private static List<string> Validate(Host host)
    {
        var problems = new List<string>();
        try
        {
            using var provider = host.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using var scope = provider.CreateScope();
            foreach (var type in host.Required)
            {
                if (scope.ServiceProvider.GetService(type) is null)
                    problems.Add($"required service {type.FullName} is not registered");
            }
        }
        catch (Exception ex)
        {
            problems.Add($"container build failed: {ex.Message}");
        }
        finally
        {
            foreach (var d in host.Owned)
                d.Dispose();
        }

        return problems;
    }

    public static TheoryData<string> HostNames() => new() { "contracts", "payroll", "establishment" };

    private static Host Create(string name) => name switch
    {
        "contracts" => Contracts(),
        "payroll" => Payroll(),
        "establishment" => Establishment(),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(HostNames))]
    public void Shared_host_builds_with_validation_and_registers_required_services(string name)
    {
        var problems = Validate(Create(name));
        Assert.True(problems.Count == 0, $"integration host '{name}':\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Contracts_host_without_the_active_regime_client_is_named()
    {
        var host = Contracts();
        host.Services.RemoveAll<IHrCoreActiveRegimeClient>();

        var problems = Validate(host);

        Assert.Contains(problems, p => p.Contains(nameof(IHrCoreActiveRegimeClient), StringComparison.Ordinal));
    }

    [Fact]
    public void Payroll_host_without_the_service_regime_client_is_named()
    {
        var host = Payroll();
        host.Services.RemoveAll<IHrCoreServiceRegimeClient>();

        var problems = Validate(host);

        Assert.Contains(problems, p => p.Contains(nameof(IHrCoreServiceRegimeClient), StringComparison.Ordinal));
    }

    [Fact]
    public void Establishment_host_without_the_temporal_services_is_named()
    {
        var host = Establishment();
        host.Services.RemoveAll<IFundedPositionVersionService>();

        var problems = Validate(host);

        Assert.Contains(problems, p => p.Contains(nameof(IFundedPositionVersionService), StringComparison.Ordinal));
    }
}
