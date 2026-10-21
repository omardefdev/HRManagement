using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class EstablishmentHireIntegrationTests
{
    [Fact]
    public async Task HirePartyCompensate_VacatesPosition_RevertsApplicationAndOffer()
    {
        await using var env = await CreateEnvAsync();
        var (appId, employeeId, positionId) = await SeedHiredApplicationAsync(env.Db);

        await env.Harness.Bus.Publish<IExecuteHireParty>(new
        {
            ServiceTransactionId = Guid.NewGuid(),
            EmployeeId = employeeId,
            Party = ProcessParties.HrCore,
            ContractId = (Guid?)null,
            RecruitmentApplicationId = appId,
            CorrelationId = "compensate-test",
            TenantId = (Guid?)null,
            Compensate = true
        });

        Assert.True(await env.Harness.Consumed.Any<IExecuteHireParty>());

        var position = await env.Db.FundedPositions.AsNoTracking().SingleAsync(p => p.Id == positionId);
        Assert.Equal(FundedPositionStatus.Vacant, position.Status);
        Assert.Null(position.OccupantEmployeeId);

        var app = await env.Db.RecruitmentApplications.AsNoTracking()
            .Include(a => a.ActiveOfferLetter)
            .SingleAsync(a => a.Id == appId);
        Assert.Equal(RecruitmentApplicationStatus.OfferAccepted, app.Status);
        Assert.Null(app.HiredEmployeeId);
        Assert.False(app.ActiveOfferLetter!.IsConvertedToEmployee);
    }

    [Fact]
    public async Task HirePartyVerify_FaultsWhenPositionNotOccupiedByEmployee()
    {
        await using var env = await CreateEnvAsync();
        var appId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await env.Harness.Bus.Publish<IExecuteHireParty>(new
        {
            ServiceTransactionId = Guid.NewGuid(),
            EmployeeId = employeeId,
            Party = ProcessParties.HrCore,
            ContractId = (Guid?)null,
            RecruitmentApplicationId = appId,
            CorrelationId = "verify-fault",
            TenantId = (Guid?)null,
            Compensate = false
        });

        Assert.True(await env.Harness.Published.Any<IHirePartyFaulted>(
            x => x.Context.Message.Party == ProcessParties.HrCore));
    }

    private static async Task<(Guid AppId, Guid EmployeeId, Guid PositionId)> SeedHiredApplicationAsync(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-COMP", NameAr = "HR", NameEn = "HR" };
        db.OrgUnits.Add(org);
        var employee = new Employee
        {
            NationalId = "9999999999",
            FirstName = "Test",
            LastName = "User",
            FullName = "Test User",
            BirthDate = DateTime.UtcNow.AddYears(-30),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            PrimaryOrgUnitId = org.Id,
            CurrentStatus = EmploymentStatus.PendingActivation
        };
        db.Employees.Add(employee);

        var position = new FundedPosition
        {
            PositionCode = "VAC-COMP",
            GradeCode = "G1",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Occupied,
            OrgUnitId = org.Id,
            OccupantEmployeeId = employee.Id
        };
        db.FundedPositions.Add(position);

        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-COMP",
            FundedPositionId = position.Id,
            JobTitle = "Tester",
            Status = RequisitionStatus.Closed,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 0
        };
        db.RecruitmentRequisitions.Add(req);

        var offer = new OfferLetter
        {
            FundedPositionId = position.Id,
            OfferNumber = "OFF-COMP",
            Status = OfferStatus.Accepted,
            OfferedSalary = 1000,
            ProposedStartDate = DateTime.UtcNow,
            IsConvertedToEmployee = true,
            AcceptedDate = DateTime.UtcNow
        };

        var app = new RecruitmentApplication
        {
            ApplicantFullName = "Test User",
            RequisitionId = req.Id,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Hired,
            HiredEmployeeId = employee.Id
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        offer.RecruitmentApplicationId = app.Id;
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        app.ActiveOfferLetterId = offer.Id;
        await db.SaveChangesAsync();
        return (app.Id, employee.Id, position.Id);
    }

    private static async Task<TestEnv> CreateEnvAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<HRCoreDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<EstablishmentService>();
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<ProcessPartyAckConsumers>();
            cfg.UsingInMemory((context, busCfg) => busCfg.ConfigureEndpoints(context));
        });

        var provider = services.BuildServiceProvider(true);
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await db.Database.EnsureCreatedAsync();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return new TestEnv(provider, scope, harness, db);
    }

    private sealed class TestEnv(ServiceProvider provider, IServiceScope scope, ITestHarness harness, HRCoreDbContext db) : IAsyncDisposable
    {
        public HRCoreDbContext Db => db;
        public ITestHarness Harness => harness;

        public async ValueTask DisposeAsync()
        {
            await harness.Stop();
            scope.Dispose();
            await provider.DisposeAsync();
        }
    }
}
