using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Payroll.Application;
using Payroll.Application.Abstractions;
using Payroll.Application.Options;
using Payroll.Infrastructure.Repositories;
using Payroll.Infrastructure.Services;

namespace HRManagement.IntegrationTests;

internal static class PayrollIntegrationTestSetup
{
    public static void AddPayrollConsumerServices(IServiceCollection services)
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IPayrollRepository, PayrollRepository>();
        services.AddPayrollApplication();

        services.AddScoped<IPaymentDisbursementGate, PaymentDisbursementGate>();
        services.Configure<PayrollBatchOptions>(o =>
        {
            o.MaxDegreeOfParallelism = 1;
            o.PartitionQueueCapacity = 4;
            o.BulkWriteSize = 25;
            o.DefaultPartitionSize = 200;
        });
        services.AddSingleton(Mock.Of<IPayrollEventPublisher>());
        // QA-034 (moved from PLAN-59): the calculator asks HR.Core for service-regime payroll blockers and refuses without
        // a client; integration hosts get the declared fake (nobody blocked). The blocked path itself is PLAN-59.
        services.TryAddScoped<IHrCoreServiceRegimeClient, FakeHrCoreServiceRegimeClient>();
        services.AddScoped<PaymentOrderService>();
        services.AddScoped<IPaymentOrderCoordinator, PaymentOrderCoordinator>();
        services.AddScoped<BankReconciliationService>();
        services.AddScoped<IBankReconciliationCoordinator, BankReconciliationCoordinator>();
        services.AddSingleton<IHostEnvironment>(_ =>
        {
            var env = new Mock<IHostEnvironment>();
            env.Setup(e => e.ContentRootPath).Returns(RepoPaths.Root);
            env.Setup(e => e.EnvironmentName).Returns(Environments.Development);
            env.Setup(e => e.ApplicationName).Returns("Payroll.IntegrationTests");
            return env.Object;
        });
    }
}
