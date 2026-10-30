using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class HRCoreTransferExecutedConsumerTests
{
    [Fact]
    public async Task Consume_closes_open_employment_record_and_adds_new_one()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var fromOrg = Guid.NewGuid();
        var toOrg = Guid.NewGuid();
        var executedAt = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(tenantId);
        tenant.Setup(t => t.IsPlatformOperator).Returns(false);
        tenant.Setup(t => t.IsPlatformImpersonating).Returns(false);

        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase($"transfer-{Guid.NewGuid():N}")
            .AddInterceptors(new TenantIdStampInterceptor(tenant.Object))
            .Options;

        TenantIdFlow.Set(tenantId);
        try
        {
            await using var db = new HRCoreDbContext(options, tenant.Object);
            await db.Database.EnsureCreatedAsync();
            await LegislationRegisterSeeder.SeedAsync(db);
            var legislationProvider = new LegislationRegisterProvider(db);
            await ServiceRegimeSeeder.SeedAsync(db, legislationProvider);
            var legalValidator = new RecruitmentLegalSourceValidator(legislationProvider);
            var regimeValidator = new ServiceRegimeValidator(db, legalValidator);
            var transitionService = new ServiceRegimeTransitionService(db, regimeValidator);
            var exceptionService = new ServiceRegimeExceptionService(db, transitionService);

            db.Employees.Add(new Employee
            {
                Id = employeeId,
                TenantId = tenantId,
                FullName = "Emp",
                FirstName = "Emp",
                LastName = "Test",
                NationalId = "TRF000000001",
                PrimaryOrgUnitId = fromOrg
            });
            db.EmploymentRecords.Add(new EmploymentRecord
            {
                EmployeeId = employeeId,
                TenantId = tenantId,
                PositionTitle = "Old",
                OrgUnitId = fromOrg,
                StartDate = executedAt.AddYears(-1).Date,
                EmploymentType = EmploymentType.Permanent,
                LegalRegimeId = "law50"
            });
            await db.SaveChangesAsync();

            var message = new TransferExecutedMessage
            {
                EmployeeId = employeeId,
                FromOrgUnitId = fromOrg,
                ToOrgUnitId = toOrg,
                ExecutedAt = executedAt,
                FromLegalRegimeId = "law50"
            };

            var contextMock = new Mock<ConsumeContext<ITransferExecuted>>();
            contextMock.Setup(c => c.Message).Returns(message);
            contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

            var consumer = new TransferExecutedConsumer(
                db,
                transitionService,
                exceptionService,
                Mock.Of<Microsoft.Extensions.Logging.ILogger<TransferExecutedConsumer>>());
            await consumer.Consume(contextMock.Object);

            var records = await db.EmploymentRecords
                .IgnoreQueryFilters()
                .Where(r => r.EmployeeId == employeeId)
                .OrderBy(r => r.StartDate)
                .ToListAsync();

            Assert.Equal(2, records.Count);
            Assert.Equal(executedAt.Date, records[0].EndDate);
            Assert.Equal("closed", records[0].Status);
            Assert.Null(records[1].EndDate);
            Assert.Equal(toOrg, records[1].OrgUnitId);
            Assert.Equal("law50", records[1].LegalRegimeId);

            var employee = await db.Employees.IgnoreQueryFilters().SingleAsync(e => e.Id == employeeId);
            Assert.Equal(toOrg, employee.PrimaryOrgUnitId);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private sealed class TransferExecutedMessage : ITransferExecuted
    {
        public Guid EmployeeId { get; init; }
        public Guid FromOrgUnitId { get; init; }
        public Guid ToOrgUnitId { get; init; }
        public DateTime ExecutedAt { get; init; }
        public string? FromLegalRegimeId { get; init; }
        public Guid? TenantId { get; init; }
    }
}
