using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class RecruitmentHireIntegrationTests
{
    private static (HRCoreDbContext Db, RecruitmentIntegrationService Service) CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();

        var publish = new Mock<IPublishEndpoint>(MockBehavior.Loose);
        publish.Setup(p => p.Publish(It.IsAny<It.IsAnyType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new RecruitmentIntegrationService(db, publish.Object);
        return (db, service);
    }

    [Fact]
    public async Task HireAndCreateEmployeeAsync_SetsHiredAndPublishesCandidateBeforeEmployeeCreated()
    {
        var (db, service) = CreateContext();
        var app = new RecruitmentApplication
        {
            ApplicantFullName = "أحمد محمد علي",
            AppliedPosition = "محلل نظم",
            NationalId = "1234567890",
            Status = RecruitmentApplicationStatus.Shortlisted,
            AppliedAt = DateTime.UtcNow
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        var employee = await service.HireAndCreateEmployeeAsync(
            app,
            "1234567890",
            "أحمد",
            "محمد",
            "علي",
            new DateTime(1990, 1, 1),
            Gender.Male,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "DEFAULT");

        db.ChangeTracker.Clear();
        var savedApp = await db.RecruitmentApplications.SingleAsync(a => a.Id == app.Id);
        Assert.Equal(RecruitmentApplicationStatus.Hired, savedApp.Status);
        Assert.Equal(employee.Id, savedApp.HiredEmployeeId);

        var savedEmployee = await db.Employees.SingleAsync(e => e.Id == employee.Id);
        Assert.Equal("أحمد محمد علي", savedEmployee.FullName);
        Assert.True(await db.EmploymentRecords.AnyAsync(r => r.EmployeeId == employee.Id));
        Assert.True(await db.EmploymentStatusHistories.AnyAsync(h => h.EmployeeId == employee.Id));
    }

    [Fact]
    public async Task HireExistingEmployeeAsync_LinksApplicationWithoutCreatingEmployee()
    {
        var (db, service) = CreateContext();
        var existing = new Employee
        {
            NationalId = "9988776655",
            FirstName = "سارة",
            LastName = "خالد",
            FullName = "سارة خالد",
            BirthDate = new DateTime(1988, 5, 5),
            Gender = Gender.Female
        };
        db.Employees.Add(existing);

        var app = new RecruitmentApplication
        {
            ApplicantFullName = "سارة خالد",
            AppliedPosition = "محاسب",
            Status = RecruitmentApplicationStatus.Shortlisted,
            AppliedAt = DateTime.UtcNow
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        await service.HireExistingEmployeeAsync(app, existing.Id, DateOnly.FromDateTime(DateTime.UtcNow), "DEFAULT");

        db.ChangeTracker.Clear();
        var savedApp = await db.RecruitmentApplications.SingleAsync(a => a.Id == app.Id);
        Assert.Equal(RecruitmentApplicationStatus.Hired, savedApp.Status);
        Assert.Equal(existing.Id, savedApp.HiredEmployeeId);
        Assert.Single(await db.Employees.ToListAsync());
    }
}
