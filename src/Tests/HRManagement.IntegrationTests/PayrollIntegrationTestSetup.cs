using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<PaymentOrderService>();
        services.AddScoped<IPaymentOrderCoordinator, PaymentOrderCoordinator>();
        services.AddScoped<BankReconciliationService>();
        services.AddScoped<IBankReconciliationCoordinator, BankReconciliationCoordinator>();
        services.AddSingleton<IHostEnvironment>(_ =>
        {
            var env = new Mock<IHostEnvironment>();
            env.Setup(e => e.ContentRootPath).Returns(Directory.GetCurrentDirectory());
            env.Setup(e => e.EnvironmentName).Returns(Environments.Development);
            env.Setup(e => e.ApplicationName).Returns("Payroll.IntegrationTests");
            return env.Object;
        });
    }
}
