using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;
using HR.Health.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Records.Archive.Domain.Enums;
using Records.Archive.Domain.Services;
using Records.Archive.Infrastructure.Data;
using ArchiveSeparationPartyAckConsumer = Records.Archive.Infrastructure.Consumers.SeparationPartyAckConsumer;
using HealthSeparationPartyAckConsumer = HR.Health.Infrastructure.Consumers.SeparationPartyAckConsumer;

namespace HRManagement.UnitTests;

public class RecordsArchivePartyCompensationTests
{
    [Fact]
    public async Task SeparationParty_compensate_clears_staged_checklist()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateArchiveDb(tenantId);
        var separationId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var consumer = new ArchiveSeparationPartyAckConsumer(db);

        foreach (var step in ArchiveChecklistDefaults.CreateFor(separationId))
        {
            step.TenantId = tenantId;
            if (step.Step == ArchiveChecklistStep.Ingested)
                ArchiveChecklistDefaults.Mark(step, null, $"archive:separation-party:{separationId:N}");
            db.ArchiveChecklists.Add(step);
        }
        await db.SaveChangesAsync();
        Assert.True(await db.ArchiveChecklists.AnyAsync(c =>
            c.CorrelationId == separationId && c.IsCompleted));

        await consumer.Consume(CreateContext(separationId, employeeId, compensate: true));

        Assert.False(await db.ArchiveChecklists.AnyAsync(c =>
            c.CorrelationId == separationId && c.IsCompleted));
        Assert.Empty(await db.ArchiveItems.ToListAsync());
    }

    private static ConsumeContext<IExecuteSeparationParty> CreateContext(
        Guid separationId, Guid employeeId, bool compensate)
    {
        var message = new Mock<IExecuteSeparationParty>();
        message.Setup(m => m.SeparationRecordId).Returns(separationId);
        message.Setup(m => m.EmployeeId).Returns(employeeId);
        message.Setup(m => m.Party).Returns(ProcessParties.Archive);
        message.Setup(m => m.Compensate).Returns(compensate);

        var context = new Mock<ConsumeContext<IExecuteSeparationParty>>();
        context.Setup(c => c.Message).Returns(message.Object);
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        context.Setup(c => c.Publish(It.IsAny<ISeparationPartyCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        context.Setup(c => c.Publish(It.IsAny<ISeparationPartyFaulted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return context.Object;
    }

    private static ArchiveDbContext CreateArchiveDb(Guid tenantId)
    {
        var tenant = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<ArchiveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TenantIdStampInterceptor(tenant))
            .Options;
        return new ArchiveDbContext(options, tenant);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => true;
        public Guid RequireTenantId() => tenantId;
    }
}

public class HealthPartyCompensationTests
{
    [Fact]
    public async Task SeparationParty_compensate_restores_insurance()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateHealthDb(tenantId);
        var employeeId = Guid.NewGuid();
        var separationId = Guid.NewGuid();
        db.InsuranceCards.Add(new InsuranceCard
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            MemberNumber = "M-1",
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = InsuranceCardStatus.Active
        });
        await db.SaveChangesAsync();

        var consumer = new HealthSeparationPartyAckConsumer(db);
        await consumer.Consume(CreateContext(separationId, employeeId, compensate: false));

        var card = await db.InsuranceCards.SingleAsync();
        Assert.True(card.IsCancelled);

        await consumer.Consume(CreateContext(separationId, employeeId, compensate: true));

        card = await db.InsuranceCards.SingleAsync();
        Assert.False(card.IsCancelled);
        Assert.Equal(InsuranceCardStatus.Active, card.Status);
    }

    private static ConsumeContext<IExecuteSeparationParty> CreateContext(
        Guid separationId, Guid employeeId, bool compensate)
    {
        var message = new Mock<IExecuteSeparationParty>();
        message.Setup(m => m.SeparationRecordId).Returns(separationId);
        message.Setup(m => m.EmployeeId).Returns(employeeId);
        message.Setup(m => m.Party).Returns(ProcessParties.Health);
        message.Setup(m => m.Compensate).Returns(compensate);

        var context = new Mock<ConsumeContext<IExecuteSeparationParty>>();
        context.Setup(c => c.Message).Returns(message.Object);
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        context.Setup(c => c.Publish(It.IsAny<ISeparationPartyCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        context.Setup(c => c.Publish(It.IsAny<ISeparationPartyFaulted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return context.Object;
    }

    private static HealthDbContext CreateHealthDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<HealthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HealthDbContext(options, new FixedTenantContext(tenantId));
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
