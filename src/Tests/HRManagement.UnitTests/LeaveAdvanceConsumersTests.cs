using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Infrastructure;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;
using Records.Archive.Infrastructure.Data;
using ArchiveLeaveApprovedConsumer = Records.Archive.Infrastructure.Consumers.LeaveApprovedConsumer;

namespace HRManagement.UnitTests;

public class LeaveAdvanceConsumersTests
{
    private static (IServiceProvider Sp, PayrollDbContext Db) CreatePayrollServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddPayrollInfrastructure();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<PayrollDbContext>();
        return (sp, db);
    }

    [Fact]
    public async Task PayrollAdvanceConsumer_Should_Be_Idempotent()
    {
        var (sp, db) = CreatePayrollServices();
        await db.Database.EnsureCreatedAsync();
        var consumer = new AdvanceApprovedConsumer(sp.GetRequiredService<IMediator>(), db);
        var messageId = NewId.NextGuid();

        var message = Mock.Of<IAdvanceApproved>(m =>
            m.TransactionId == Guid.NewGuid() &&
            m.AdvanceRequestId == Guid.NewGuid() &&
            m.EmployeeId == Guid.NewGuid() &&
            m.Amount == 1000m &&
            m.MonthlyInstallment == 500m &&
            m.NumberOfInstallments == 2 &&
            m.ApprovedBy == Guid.NewGuid() &&
            m.CorrelationId == "corr-1" &&
            m.SourceSystem == "HR.Contracts");

        for (var i = 0; i < 2; i++)
        {
            var context = new Mock<ConsumeContext<IAdvanceApproved>>();
            context.SetupGet(c => c.Message).Returns(message);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            context.SetupGet(c => c.MessageId).Returns(messageId);
            await consumer.Consume(context.Object);
        }

        Assert.Equal(2, await db.DeductionRecords.CountAsync());
        Assert.Equal(1, await db.IntegrationMessageReceipts.CountAsync());
    }

    [Fact]
    public async Task PayrollLeaveConsumer_Should_Be_Idempotent()
    {
        var (sp, db) = CreatePayrollServices();
        await db.Database.EnsureCreatedAsync();
        var consumer = new LeaveApprovedConsumer(sp.GetRequiredService<IMediator>(), db);
        var messageId = NewId.NextGuid();

        var message = Mock.Of<ILeaveApproved>(m =>
            m.TransactionId == Guid.NewGuid() &&
            m.LeaveRequestId == Guid.NewGuid() &&
            m.EmployeeId == Guid.NewGuid() &&
            m.LeaveType == "Annual" &&
            m.DurationDays == 3 &&
            m.ApprovedBy == Guid.NewGuid() &&
            m.CorrelationId == "corr-2" &&
            m.SourceSystem == "HR.Contracts");

        for (var i = 0; i < 2; i++)
        {
            var context = new Mock<ConsumeContext<ILeaveApproved>>();
            context.SetupGet(c => c.Message).Returns(message);
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            context.SetupGet(c => c.MessageId).Returns(messageId);
            await consumer.Consume(context.Object);
        }

        Assert.Equal(1, await db.PayrollAdjustments.CountAsync());
        Assert.Equal(1, await db.IntegrationMessageReceipts.CountAsync());
    }

    [Fact]
    public async Task ArchiveLeaveConsumer_Should_Be_Idempotent()
    {
        var options = new DbContextOptionsBuilder<ArchiveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ArchiveDbContext(options);
        var codeClient = new Mock<ICodeGenerationClient>();
        codeClient.Setup(c => c.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "ARC-LEAVE-2026-00001" });
        // PLAN-37: archive consumers use the required form (it throws instead of returning null).
        codeClient.Setup(c => c.GenerateRequiredAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "ARC-LEAVE-2026-00001" });
        var consumer = new ArchiveLeaveApprovedConsumer(db, codeClient.Object);

        var message = Mock.Of<ILeaveApproved>(m =>
            m.TransactionId == Guid.NewGuid() &&
            m.LeaveRequestId == Guid.NewGuid() &&
            m.EmployeeId == Guid.NewGuid() &&
            m.ApprovedBy == Guid.NewGuid() &&
            m.CorrelationId == "corr-3" &&
            m.SourceSystem == "HR.Contracts");
        var context = Mock.Of<ConsumeContext<ILeaveApproved>>(c => c.Message == message);

        await consumer.Consume(context);
        await consumer.Consume(context);

        Assert.Equal(1, await db.ArchiveItems.CountAsync());
    }
}
