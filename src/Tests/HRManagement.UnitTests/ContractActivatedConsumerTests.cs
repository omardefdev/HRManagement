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
        // PLAN-55: the activation goes through PLAN-53's applier, which stages IPayProfileChangeApplied on the bus. This host has no
        // bus, so it gets a double (the outbox itself is proved on PostgreSQL and on the stack).
        var bus = new Mock<IPublishEndpoint>();
        services.AddSingleton(bus.Object);
        // The applier's rules read their settings (Payroll:SalaryChange): an empty configuration gives the defaults, as a service has.
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
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
        // The host's handler was given the registered applier: the activation is an applied request, and its event was staged once.
        Assert.Single(db.PayProfileChangeRequests.Where(r => r.SourceReference == msg.ContractId.ToString("D")));
        bus.Verify(b => b.Publish<IPayProfileChangeApplied>(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
