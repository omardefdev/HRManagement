using HRManagement.MessageContracts.Events;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Infrastructure;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;

namespace HRManagement.UnitTests;

public class ContractActivatedConsumerTests
{
    private sealed record ContractActivatedMessage(
        Guid ContractId,
        Guid EmployeeId,
        string ContractType,
        DateTime StartDate,
        DateTime? EndDate,
        decimal? BaseSalary = null,
        string? PayGradeCode = null,
        string? CorrelationId = null,
        string? SourceSystem = null) : IContractActivated;

    [Fact]
    public async Task Consume_adds_payroll_adjustment_row()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddPayrollInfrastructure();
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.EnsureCreatedAsync();

        var consumer = new ContractActivatedConsumer(
            scope.ServiceProvider.GetRequiredService<IMediator>());

        var msg = new ContractActivatedMessage(
            Guid.NewGuid(), Guid.NewGuid(), "PERM", DateTime.UtcNow, null);

        var context = new Mock<ConsumeContext<IContractActivated>>();
        context.SetupGet(c => c.Message).Returns(msg);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MessageId).Returns(NewId.NextGuid());

        await consumer.Consume(context.Object);

        Assert.Single(db.PayrollAdjustments);
        Assert.Single(db.EmployeePayProfiles.Where(p => p.IsActive));
    }
}
