using HR.Health.Infrastructure.Consumers;
using HR.Health.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.UnitTests;

public class HealthEmployeeCreatedConsumerTests
{
    private sealed class EmployeeCreatedMessage : IEmployeeCreated
    {
        public Guid EmployeeId { get; init; }
        public string FullName { get; init; } = "";
        public string NationalIdHash { get; init; } = "";
        public string EmploymentType { get; init; } = "";
        public Guid? TenantId { get; init; }
        public Guid? PrimaryOrgUnitId { get; init; }
    }

    [Fact]
    public async Task Duplicate_IEmployeeCreated_Creates_Single_MedicalRecord()
    {
        var employeeId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HealthDbContext>()
            .UseInMemoryDatabase($"health-emp-{employeeId}")
            .Options;

        await using var db = new HealthDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var consumer = new EmployeeCreatedConsumer(db, Mock.Of<ILogger<EmployeeCreatedConsumer>>());
        var message = new EmployeeCreatedMessage { EmployeeId = employeeId, FullName = "Test Employee" };

        for (var i = 0; i < 2; i++)
        {
            var context = Mock.Of<ConsumeContext<IEmployeeCreated>>(c =>
                c.Message == message
                && c.MessageId == Guid.NewGuid()
                && c.CancellationToken == CancellationToken.None);
            await consumer.Consume(context);
        }

        var records = await db.MedicalRecords.Where(r => r.EmployeeId == employeeId).ToListAsync();
        Assert.Single(records);
    }
}
