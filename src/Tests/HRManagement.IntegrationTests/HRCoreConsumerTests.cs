using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class HRCoreConsumerTests
{
    private static async Task<MassTransitTestScope> CreateScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                connection.Open();
                connections.Add(connection);
                IntegrationTestHelpers.AddSqliteDbContext<HRCoreDbContext>(services, connection);
                IntegrationTestHelpers.AddHRCoreConsumerDependencies(services);
            },
            cfg =>
            {
                cfg.AddConsumer<TrainingCompletedConsumer>();
                cfg.AddConsumer<LeaveApprovedConsumer>();
                cfg.AddConsumer<SeparationCompletedConsumer>();
            },
            enableInboxFilter: true);

        await scope.GetDb<HRCoreDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    [Fact]
    public async Task TrainingCompleted_Adds_Qualification()
    {
        await using var scope = await CreateScopeAsync();
        var db = scope.GetDb<HRCoreDbContext>();
        var empId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        db.Employees.Add(new Employee
        {
            Id = empId,
            FirstName = "موظف",
            LastName = "اختبار",
            FullName = "موظف اختبار",
            NationalId = "123",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ITrainingCompleted>(new
        {
            EmployeeId = empId,
            CourseId = courseId,
            Score = 90m,
            CompletedAt = DateTime.UtcNow,
            CertificateFileId = (Guid?)null,
            SchemaVersion = 3,
            CorrelationId = (string?)null,
            SourceSystem = "Test"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ITrainingCompleted>(scope.Harness);
        db.ChangeTracker.Clear();
        var quals = await db.Qualifications.Where(q => q.EmployeeId == empId).ToListAsync();
        Assert.Single(quals);
    }

    [Fact]
    public async Task TrainingCompleted_DuplicateMessageId_Ignored()
    {
        await using var scope = await CreateScopeAsync();
        var db = scope.GetDb<HRCoreDbContext>();
        var empId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var messageId = NewId.NextGuid();

        db.Employees.Add(new Employee
        {
            Id = empId,
            FirstName = "موظف",
            LastName = "اختبار",
            FullName = "موظف اختبار",
            NationalId = "456",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active
        });
        db.IntegrationMessageReceipts.Add(new IntegrationMessageReceipt
        {
            MessageId = messageId.ToString(),
            ConsumerType = "TrainingCompleted", // MassTransit endpoint name (Consumer suffix stripped)
            TenantId = HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId,
            ReceivedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ITrainingCompleted>(new
        {
            EmployeeId = empId,
            CourseId = courseId,
            Score = 80m,
            CompletedAt = DateTime.UtcNow,
            CertificateFileId = (Guid?)null,
            SchemaVersion = 3,
            CorrelationId = (string?)null,
            SourceSystem = "Test"
        }, ctx =>
        {
            ctx.MessageId = messageId;
            ctx.Headers.Set(HRManagement.ServiceDefaults.TenantIdMiddleware.HeaderName,
                HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId.ToString("D"));
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ITrainingCompleted>(scope.Harness);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.Qualifications.Where(q => q.EmployeeId == empId).ToListAsync());
    }

    [Fact]
    public async Task LeaveApproved_Sets_Employee_OnLeave()
    {
        await using var scope = await CreateScopeAsync();
        var db = scope.GetDb<HRCoreDbContext>();
        var empId = Guid.NewGuid();
        var leaveId = Guid.NewGuid();

        db.Employees.Add(new Employee
        {
            Id = empId,
            FirstName = "موظف",
            LastName = "إجازة",
            FullName = "موظف إجازة",
            NationalId = "789",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ILeaveApproved>(new
        {
            TransactionId = Guid.NewGuid(),
            LeaveRequestId = leaveId,
            EmployeeId = empId,
            ApprovedBy = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            LeaveType = "Annual",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(5),
            DurationDays = 5
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ILeaveApproved>(scope.Harness);
        db.ChangeTracker.Clear();
        var employee = await db.Employees.FindAsync(empId);
        Assert.Equal(HRManagement.SharedKernel.Enums.EmploymentStatus.OnLeave, employee!.CurrentStatus);
    }

    [Fact]
    public async Task SeparationCompleted_Sets_TerminalStatus_And_History()
    {
        await using var scope = await CreateScopeAsync();
        var db = scope.GetDb<HRCoreDbContext>();
        var empId = Guid.NewGuid();

        db.Employees.Add(new Employee
        {
            Id = empId,
            FirstName = "موظف",
            LastName = "فصل",
            FullName = "موظف فصل",
            NationalId = "456",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ISeparationCompleted>(new
        {
            EmployeeId = empId,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow.Date,
            OccurredAt = DateTime.UtcNow,
            ContractId = (Guid?)null,
            SeparationRecordId = (Guid?)null,
            CorrelationId = (string?)null,
            SourceSystem = "Test"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ISeparationCompleted>(scope.Harness);
        db.ChangeTracker.Clear();
        var employee = await db.Employees.FindAsync(empId);
        Assert.Equal(HRManagement.SharedKernel.Enums.EmploymentStatus.Resigned, employee!.CurrentStatus);
        Assert.True(await db.EmploymentStatusHistories.AnyAsync(h =>
            h.EmployeeId == empId && h.NewStatus == nameof(HRManagement.SharedKernel.Enums.EmploymentStatus.Resigned)));
        Assert.True(await db.EmploymentRecords.AnyAsync(r => r.EmployeeId == empId));
    }
}
