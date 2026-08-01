using MediatR;
using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Insurance;
using HRManagement.Integration.Abstractions.Payroll;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Application;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.EndOfService;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Integration;
using Payroll.Infrastructure.Repositories;
using Payroll.Web.Controllers;
using System.Security.Claims;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class PayrollRunLifecycleIntegrationTests
{
    private static async Task<(ServiceProvider Root, AsyncServiceScope Scope, PayrollDbContext Db, IMediator Mediator)> CreatePayrollScopeAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseSqlite(connection));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);

        var root = services.BuildServiceProvider();
        var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.EnsureCreatedAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return (root, scope, db, mediator);
    }

    private static TempDataDictionary CreateTempData(HttpContext httpContext) =>
        new(httpContext, Mock.Of<ITempDataProvider>());

    private static ClaimsPrincipal CreateUser(Guid userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        ], "TestAuth"));

    [Fact]
    public async Task PayrollRun_Draft_Calculate_Post_Lifecycle()
    {
        var (root, scope, db, mediator) = await CreatePayrollScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var empId = Guid.NewGuid();
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(empId, 12000m));
        var config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        config.EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.PayrollConfigurations.Add(config);
        await db.SaveChangesAsync();

        var periodStart = new DateTime(2026, 5, 1);
        var periodEnd = new DateTime(2026, 5, 31);

        var (created, runId, createErr) = await mediator.Send(new CreatePayrollRunCommand(
            periodStart, periodEnd, DateTime.UtcNow));
        Assert.True(created, createErr);
        Assert.NotNull(runId);

        var (calculated, lines, calcErr) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
        Assert.True(calculated, calcErr);
        Assert.True(lines >= 1);

        db.ChangeTracker.Clear();
        var run = await db.PayrollRuns.Include(r => r.SalaryLines).FirstAsync(r => r.Id == runId);
        Assert.Equal(PayrollRunStatus.Calculated, run.Status);
        Assert.NotEmpty(run.SalaryLines);

        var (submitted, submitErr) = await mediator.Send(new SubmitPayrollRunForReviewCommand(runId.Value));
        Assert.True(submitted, submitErr);

        db.ChangeTracker.Clear();
        run = await db.PayrollRuns.FindAsync(runId);
        Assert.Equal(PayrollRunStatus.UnderReview, run!.Status);

        var approverId = Guid.NewGuid();
        var (approved, approveErr) = await mediator.Send(new ApprovePayrollRunCommand(runId.Value, approverId, "PayrollApprover", null));
        Assert.True(approved, approveErr);
        db.ChangeTracker.Clear();
        run = await db.PayrollRuns.FindAsync(runId);
        Assert.Equal(PayrollRunStatus.Approved, run!.Status);
    }

    [Fact]
    public async Task AddSalaryLine_WhenRunApproved_DoesNotPersistLine()
    {
        var (root, scope, db, _) = await CreatePayrollScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Approved
        };
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        var controller = new PayrollRunsController(db, Mock.Of<IMediator>());
        var line = new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1000m,
            TotalDeductions = 100m,
            PayComponents = "manual"
        };

        var result = await controller.AddSalaryLine(line);

        var view = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(line, view.Model);
        Assert.Empty(await db.SalaryLines.ToListAsync());
    }

    [Fact]
    public async Task ApprovePayrollRun_WhenApproverIsCreator_ReturnsError()
    {
        var (root, scope, db, mediator) = await CreatePayrollScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var creatorId = Guid.NewGuid();
        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.UnderReview,
            CreatedBy = creatorId
        };
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        var (success, error) = await mediator.Send(new ApprovePayrollRunCommand(run.Id, creatorId, "PayrollApprover", null));

        Assert.False(success);
        Assert.Equal("لا يمكن لمنشئ التشغيل اعتماد نفس التشغيل.", error);
    }

    [Fact]
    public async Task PostPayrollRun_WhenApproverIsPoster_ReturnsError()
    {
        var (root, scope, db, mediator) = await CreatePayrollScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var approverId = Guid.NewGuid();
        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Approved,
            TotalNetPay = 900m,
            ValidatedAtCalculate = true,
            ResultsFrozen = true
        };
        run.SalaryLines.Add(new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1000m,
            TotalDeductions = 100m,
            NetPay = 900m
        });
        run.Approvals.Add(new PayrollRunApproval
        {
            PayrollRunId = run.Id,
            ApproverId = approverId,
            Decision = ApprovalDecision.Approved,
            DecidedAt = DateTime.UtcNow
        });
        db.PayrollRuns.Add(run);
        db.PayrollAuditRecords.Add(new PayrollAuditRecord
        {
            PayrollRunId = run.Id,
            AuditorId = Guid.NewGuid(),
            ClosedBy = Guid.NewGuid(),
            ClosedAt = DateTime.UtcNow,
            Findings = "ok",
            Status = AuditStatus.Closed
        });
        await db.SaveChangesAsync();

        var (success, error) = await mediator.Send(new PostPayrollRunCommand(run.Id, approverId));

        Assert.False(success);
        Assert.Equal("لا يمكن لنفس المستخدم اعتماد التشغيل ثم ترحيله.", error);
    }

    [Fact]
    public async Task PayrollAuditClose_WhenCloserIsAuditor_ReturnsError()
    {
        var (root, scope, db, _) = await CreatePayrollScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var userId = Guid.NewGuid();
        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.UnderReview
        };
        var audit = new PayrollAuditRecord
        {
            PayrollRunId = run.Id,
            AuditorId = userId,
            Findings = "ready",
            Status = AuditStatus.Open
        };
        db.PayrollRuns.Add(run);
        db.PayrollAuditRecords.Add(audit);
        await db.SaveChangesAsync();

        var controller = new PayrollAuditRecordsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreateUser(userId) }
            }
        };
        controller.TempData = CreateTempData(controller.HttpContext);

        var result = await controller.CloseAudit(audit.Id);

        Assert.IsType<RedirectToActionResult>(result);
        db.ChangeTracker.Clear();
        var stored = await db.PayrollAuditRecords.FindAsync(audit.Id);
        Assert.NotNull(stored);
        Assert.Equal(AuditStatus.Open, stored!.Status);
        Assert.Null(stored.ClosedAt);
        Assert.Null(stored.ClosedBy);
    }

    [Fact]
    public async Task ExportTreasury_WhenRunNotPosted_Throws()
    {
        var (root, scope, db, _) = await CreatePayrollScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Approved
        };
        run.SalaryLines.Add(new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1000m,
            TotalDeductions = 100m,
            NetPay = 900m
        });
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        var service = new PayrollExternalExportService(
            db,
            Mock.Of<IEmployeeLookup>(),
            Mock.Of<IPayrollDisbursementGateway>(),
            Mock.Of<ISocialInsuranceReporter>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportRunAsync(run.Id));
        Assert.Equal("يجب ترحيل التشغيل قبل التصدير.", ex.Message);
    }

    [Fact]
    public async Task MarkPaid_WhenApproverIsPayer_ReturnsError()
    {
        var (root, scope, db, mediator) = await CreatePayrollScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var approverId = Guid.NewGuid();
        var settlement = new EndOfServiceSettlement
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            BaseSalary = 5000m,
            NetSettlement = 5000m,
            Status = EosStatus.Approved,
            ApprovedBy = approverId
        };
        db.EndOfServiceSettlements.Add(settlement);
        await db.SaveChangesAsync();

        var (success, error) = await mediator.Send(new MarkEosSettlementPaidCommand(settlement.Id, approverId));

        Assert.False(success);
        Assert.Equal("لا يمكن لنفس المستخدم اعتماد التسوية وتسجيل صرفها.", error);
    }
}
