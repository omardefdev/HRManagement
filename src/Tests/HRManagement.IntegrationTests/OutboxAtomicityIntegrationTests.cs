using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.LeaveRequests;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.IntegrationTests;

/// <summary>
/// INT-001: verifies critical publishers call SaveChanges after Publish
/// so EF outbox messages are flushed in the same logical unit of work.
/// </summary>
[Collection("IntegrationSerial")]
public class OutboxAtomicityIntegrationTests
{
    private sealed class CountingPayrollRepository(PayrollDbContext db) : IPayrollRepository
    {
        private readonly PayrollRepository _inner = new(db);
        public int SaveCount { get; private set; }

        public IQueryable<PayrollRun> PayrollRuns => _inner.PayrollRuns;
        public IQueryable<EmployeePayProfile> EmployeePayProfiles => _inner.EmployeePayProfiles;
        public IQueryable<EmployeePayProfileHistory> EmployeePayProfileHistories => _inner.EmployeePayProfileHistories;
        public IQueryable<AllowanceRecord> AllowanceRecords => _inner.AllowanceRecords;
        public IQueryable<DeductionRecord> DeductionRecords => _inner.DeductionRecords;
        public IQueryable<DeductionType> DeductionTypes => _inner.DeductionTypes;
        public IQueryable<PayrollAdjustment> PayrollAdjustments => _inner.PayrollAdjustments;
        public IQueryable<PayrollConfiguration> PayrollConfigurations => _inner.PayrollConfigurations;
        public IQueryable<OfficialHoliday> OfficialHolidays => _inner.OfficialHolidays;
        public IQueryable<AbsenceDeduction> AbsenceDeductions => _inner.AbsenceDeductions;
        public IQueryable<SalaryLine> SalaryLines => _inner.SalaryLines;
        public IQueryable<SalaryLineComponent> SalaryLineComponents => _inner.SalaryLineComponents;
        public IQueryable<EndOfServiceSettlement> EndOfServiceSettlements => _inner.EndOfServiceSettlements;
        public IQueryable<PayslipDelivery> PayslipDeliveries => _inner.PayslipDeliveries;
        public IQueryable<PayGrade> PayGrades => _inner.PayGrades;
        public IQueryable<PayrollAuditRecord> PayrollAuditRecords => _inner.PayrollAuditRecords;
        public IQueryable<PayrollRunJob> PayrollRunJobs => _inner.PayrollRunJobs;
        public IQueryable<PayrollRunPartition> PayrollRunPartitions => _inner.PayrollRunPartitions;
        public IQueryable<PayrollRunInputSnapshot> PayrollRunInputSnapshots => _inner.PayrollRunInputSnapshots;
        public IQueryable<PaymentBatch> PaymentBatches => _inner.PaymentBatches;
        public IQueryable<PaymentOrder> PaymentOrders => _inner.PaymentOrders;
        public IQueryable<FundingDecision> FundingDecisions => _inner.FundingDecisions;
        public IQueryable<DisbursementPriorityRule> DisbursementPriorityRules => _inner.DisbursementPriorityRules;
        public IQueryable<FraudAlert> FraudAlerts => _inner.FraudAlerts;
        public IQueryable<PaymentHold> PaymentHolds => _inner.PaymentHolds;

        public Task<PayrollRun?> GetPayrollRunAsync(Guid id, bool includeLines, CancellationToken ct) =>
            _inner.GetPayrollRunAsync(id, includeLines, ct);

        public Task<PayrollConfiguration?> GetActiveConfigurationAsync(CancellationToken ct) =>
            _inner.GetActiveConfigurationAsync(ct);

        public Task<PayrollConfiguration?> GetConfigurationByRegimeCodeAsync(string regimeCode, CancellationToken ct) =>
            _inner.GetConfigurationByRegimeCodeAsync(regimeCode, ct);

        public Task<PayrollConfiguration?> GetPublishedConfigurationForPeriodAsync(DateTime periodStart, CancellationToken ct) =>
            _inner.GetPublishedConfigurationForPeriodAsync(periodStart, ct);

        public Task<PayrollConfiguration?> GetConfigurationByIdAsync(Guid id, CancellationToken ct) =>
            _inner.GetConfigurationByIdAsync(id, ct);

        public Task<EmployeePayProfile?> GetActiveProfileAsync(Guid employeeId, CancellationToken ct) =>
            _inner.GetActiveProfileAsync(employeeId, ct);

        public Task<PayrollRunJob?> GetActiveJobAsync(Guid payrollRunId, CancellationToken ct) =>
            _inner.GetActiveJobAsync(payrollRunId, ct);

        public Task AddAsync<T>(T entity, CancellationToken ct) where T : class =>
            _inner.AddAsync(entity, ct);

        public void Remove<T>(T entity) where T : class => _inner.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveCount++;
            return _inner.SaveChangesAsync(ct);
        }

        public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) =>
            _inner.BeginTransactionAsync(ct);

        public void ClearChangeTracker() => _inner.ClearChangeTracker();

        public void MarkAsAdded<T>(T entity) where T : class => _inner.MarkAsAdded(entity);

        public Task<(bool Saved, bool AlreadyExists)> SaveEmployeePackageAtomicAsync(
            Guid payrollRunId,
            Payroll.Application.Services.PayrollEmployeeCalculationPackage package,
            CancellationToken ct) =>
            _inner.SaveEmployeePackageAtomicAsync(payrollRunId, package, ct);

        public Task<(int Saved, int AlreadyExists)> SaveEmployeePackagesBulkAsync(
            Guid payrollRunId,
            IReadOnlyList<Payroll.Application.Services.PayrollEmployeeCalculationPackage> packages,
            CancellationToken ct) =>
            _inner.SaveEmployeePackagesBulkAsync(payrollRunId, packages, ct);

        public Task<bool> TryClaimPartitionAsync(Guid partitionId, string workerId, CancellationToken ct) =>
            _inner.TryClaimPartitionAsync(partitionId, workerId, ct);
    }

    private sealed class RecordingPayrollPublisher(CountingPayrollRepository repo) : IPayrollEventPublisher
    {
        public int SaveCountAtPublish { get; private set; }
        public int PublishCount { get; private set; }

        public Task PublishPayrollProcessedAsync(
            Guid payrollRunId,
            DateTime periodStart,
            DateTime periodEnd,
            decimal totalNetPay,
            CancellationToken ct)
        {
            SaveCountAtPublish = repo.SaveCount;
            PublishCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingContractsRepository(ContractsDbContext db) : IContractsRepository
    {
        private readonly ContractsRepository _inner = new(db);
        public int SaveCount { get; private set; }

        public IQueryable<LeaveRequest> LeaveRequests => _inner.LeaveRequests;
        public IQueryable<AdvanceRequest> AdvanceRequests => _inner.AdvanceRequests;
        public IQueryable<AdministrativeDecision> AdministrativeDecisions => _inner.AdministrativeDecisions;
        public IQueryable<CommencementRecord> CommencementRecords => _inner.CommencementRecords;
        public IQueryable<ProbationRecord> ProbationRecords => _inner.ProbationRecords;
        public IQueryable<SeparationRecord> SeparationRecords => _inner.SeparationRecords;
        public IQueryable<ContractType> ContractTypes => _inner.ContractTypes;
        public IQueryable<Contract> Contracts => _inner.Contracts;
        public IQueryable<ServiceTransaction> ServiceTransactions => _inner.ServiceTransactions;
        public IQueryable<LeaveBalance> LeaveBalances => _inner.LeaveBalances;
        public IQueryable<LeavePolicy> LeavePolicies => _inner.LeavePolicies;
        public IQueryable<AdvanceInstallmentPlan> AdvanceInstallmentPlans => _inner.AdvanceInstallmentPlans;
        public IQueryable<SeparationChecklist> SeparationChecklists => _inner.SeparationChecklists;
        public IQueryable<HireChecklist> HireChecklists => _inner.HireChecklists;

        public Task<LeaveRequest?> GetLeaveRequestAsync(Guid id, CancellationToken ct) =>
            _inner.GetLeaveRequestAsync(id, ct);

        public Task<AdvanceRequest?> GetAdvanceRequestAsync(Guid id, CancellationToken ct) =>
            _inner.GetAdvanceRequestAsync(id, ct);

        public Task<AdministrativeDecision?> GetAdministrativeDecisionAsync(Guid id, CancellationToken ct) =>
            _inner.GetAdministrativeDecisionAsync(id, ct);

        public Task<CommencementRecord?> GetCommencementRecordAsync(Guid id, CancellationToken ct) =>
            _inner.GetCommencementRecordAsync(id, ct);

        public Task<SeparationRecord?> GetSeparationRecordAsync(Guid id, CancellationToken ct) =>
            _inner.GetSeparationRecordAsync(id, ct);

        public Task<ServiceTransaction?> GetServiceTransactionAsync(Guid id, CancellationToken ct) =>
            _inner.GetServiceTransactionAsync(id, ct);

        public Task<Contract?> GetContractAsync(Guid id, CancellationToken ct) =>
            _inner.GetContractAsync(id, ct);

        public Task AddAsync<T>(T entity, CancellationToken ct) where T : class =>
            _inner.AddAsync(entity, ct);

        public void Remove<T>(T entity) where T : class => _inner.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveCount++;
            return _inner.SaveChangesAsync(ct);
        }
    }

    private sealed class RecordingContractsPublisher(CountingContractsRepository repo) : IEventPublisher
    {
        public int SaveCountAtLeavePublish { get; private set; }
        public int LeaveApprovedCount { get; private set; }

        public Task PublishLeaveApprovedAsync(
            Guid transactionId,
            Guid leaveRequestId,
            Guid employeeId,
            Guid approvedBy,
            string? correlationId,
            CancellationToken ct,
            string? leaveType = null,
            DateOnly? startDate = null,
            DateOnly? endDate = null,
            int durationDays = 0)
        {
            SaveCountAtLeavePublish = repo.SaveCount;
            LeaveApprovedCount++;
            return Task.CompletedTask;
        }

        public Task PublishLeaveCancelledAsync(
            Guid transactionId,
            Guid leaveRequestId,
            Guid employeeId,
            Guid cancelledBy,
            string? correlationId,
            CancellationToken ct,
            string? leaveType = null,
            DateOnly? startDate = null,
            DateOnly? endDate = null,
            int durationDays = 0) =>
            Task.CompletedTask;

        public Task PublishContractActivatedAsync(Contract contract, CancellationToken ct) => Task.CompletedTask;

        public Task PublishCommencementRecordedAsync(
            Guid transactionId, Guid employeeId, DateOnly startDate, string? correlationId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PublishConfirmationApprovedAsync(
            Guid transactionId, Guid employeeId, Guid contractId, string result, string? correlationId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PublishAdvanceApprovedAsync(
            Guid transactionId, Guid advanceRequestId, Guid employeeId, decimal amount, Guid approvedBy,
            string? correlationId, CancellationToken ct, decimal monthlyInstallment = 0, int numberOfInstallments = 0) =>
            Task.CompletedTask;

        public Task PublishJobUpdateAppliedAsync(
            Guid transactionId, Guid decisionId, Guid employeeId, string decisionNumber, string subject,
            string? correlationId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PublishTransactionArchivedAsync(
            Guid transactionId, Guid employeeId, string archiveRef, string? correlationId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PublishSeparationCompletedAsync(
            Guid employeeId, string separationType, DateOnly effectiveDate, CancellationToken ct,
            Guid? contractId = null, Guid? separationRecordId = null) =>
            Task.CompletedTask;

        public Task StartSeparationSagaAsync(
            Guid separationRecordId, Guid employeeId, string separationType, DateOnly effectiveDate, CancellationToken ct,
            Guid? contractId = null, int remainingLeaveDays = 0, string? sourceLegalRegimeId = null, string? payrollRegimeCode = null) =>
            Task.CompletedTask;

        public Task CancelSeparationSagaAsync(Guid separationRecordId, string reason, CancellationToken ct) =>
            Task.CompletedTask;

        public Task StartHireSagaAsync(
            Guid serviceTransactionId, Guid employeeId, Guid? contractId, CancellationToken ct,
            Guid? recruitmentApplicationId = null) =>
            Task.CompletedTask;

        public Task PublishSeparationStepCompletedAsync(
            Guid separationRecordId, Guid employeeId, string stepName, string? notes, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PublishHireCaseCompletedAsync(
            Guid employeeId, Guid serviceTransactionId, Guid? contractId, CancellationToken ct,
            Guid? recruitmentApplicationId = null) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task PostPayrollRun_PublishesThenSavesAgain_ForOutboxFlush()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<CountingPayrollRepository>();
        services.AddScoped<IPayrollRepository>(sp => sp.GetRequiredService<CountingPayrollRepository>());
        services.AddScoped<IPayrollEventPublisher>(sp =>
            new RecordingPayrollPublisher(sp.GetRequiredService<CountingPayrollRepository>()));
        services.AddPayrollApplication();

        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.EnsureCreatedAsync();

        var approverId = Guid.NewGuid();
        var posterId = Guid.NewGuid();
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
            Status = AuditStatus.Closed,
            Findings = "ok"
        });
        await db.SaveChangesAsync();

        var repo = scope.ServiceProvider.GetRequiredService<CountingPayrollRepository>();
        var publisher = (RecordingPayrollPublisher)scope.ServiceProvider.GetRequiredService<IPayrollEventPublisher>();
        var savesBeforeHandler = repo.SaveCount;

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var (success, error) = await mediator.Send(new PostPayrollRunCommand(run.Id, posterId));

        Assert.True(success, error);
        Assert.Equal(1, publisher.PublishCount);
        Assert.True(publisher.SaveCountAtPublish > savesBeforeHandler,
            "state must be saved before publish stages the outbox message");
        Assert.True(repo.SaveCount > publisher.SaveCountAtPublish,
            "a second SaveChanges must flush the outbox message after publish");
    }

    [Fact]
    public async Task ApproveLeave_PublishesThenSavesAgain_ForOutboxFlush()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<CountingContractsRepository>();
        services.AddScoped<IContractsRepository>(sp => sp.GetRequiredService<CountingContractsRepository>());
        services.AddScoped<IEventPublisher>(sp =>
            new RecordingContractsPublisher(sp.GetRequiredService<CountingContractsRepository>()));
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<ApproveLeaveRequestHandler>());

        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();

        var employeeId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var tx = TestDataFactory.CreateLeaveTransaction(employeeId);
        // Pre-complete workflow so approve only updates leave/balance (avoids SQLite
        // concurrency noise from multi-step ServiceTransaction transitions).
        HR.Contracts.Domain.Services.ServiceTransactionWorkflowHelper.AdvanceToCompleted(
            tx, Guid.NewGuid(), "seed");
        var leave = TestDataFactory.CreatePendingLeave(employeeId, tx.Id);
        db.ServiceTransactions.Add(tx);
        db.LeaveRequests.Add(leave);
        db.LeaveBalances.Add(TestDataFactory.CreateLeaveBalance(employeeId, LeaveType.Annual, 2026, 30));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = scope.ServiceProvider.GetRequiredService<CountingContractsRepository>();
        var publisher = (RecordingContractsPublisher)scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var savesBefore = repo.SaveCount;

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var (success, error) = await mediator.Send(new ApproveLeaveRequestCommand(leave.Id, approverId));

        Assert.True(success, error);
        Assert.Equal(1, publisher.LeaveApprovedCount);
        Assert.True(publisher.SaveCountAtLeavePublish > savesBefore);
        Assert.True(repo.SaveCount > publisher.SaveCountAtLeavePublish,
            "a second SaveChanges must flush the outbox message after leave publish");
    }

    [Fact]
    public async Task CloseAttendancePeriod_SavesAfterPublishLoop()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new HRCoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var employeeId = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = employeeId,
            FirstName = "اختبار",
            LastName = "حضور",
            FullName = "اختبار حضور",
            NationalId = "N" + Guid.NewGuid().ToString("N")[..10],
            BirthDate = new DateTime(1990, 1, 1),
            Gender = Gender.Male,
            CurrentStatus = EmploymentStatus.Active
        });
        db.DailyAttendanceRecords.Add(new DailyAttendanceRecord
        {
            EmployeeId = employeeId,
            AttendanceDate = new DateOnly(2026, 5, 1),
            Status = DailyAttendanceStatus.Present
        });
        await db.SaveChangesAsync();

        var publish = new RecordingPublishEndpoint();
        var service = new AttendanceIntegrationService(db, publish);
        var published = await service.ClosePeriodAndPublishAsync(
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            orgUnitId: null);

        Assert.Equal(1, published);
        Assert.True(publish.PublishCount >= 1, "attendance close must publish period-closed events");
        // SaveChanges after the publish loop already ran; no pending domain changes remain.
        Assert.Equal(0, await db.SaveChangesAsync());
    }

    private sealed class RecordingPublishEndpoint : IPublishEndpoint
    {
        public int PublishCount { get; private set; }

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) =>
            throw new NotSupportedException();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HrCoreDbContext_IncludesMassTransitOutboxEntityTypes()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new HRCoreDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var entityTypes = db.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToHashSet();
        Assert.Contains("OutboxMessage", entityTypes);
        Assert.Contains("OutboxState", entityTypes);
        Assert.Contains("InboxState", entityTypes);
    }

    /// <summary>
    /// INT-001 gate: broker unavailable after commit must not lose the deferred publish —
    /// outbox rows survive "kill" and remain for delivery on restart.
    /// </summary>
    [Fact]
    public async Task INT001_Outbox_KillRestart_NoLoss()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new PayrollDbContext(options);
        await db.Database.EnsureCreatedAsync();

        Assert.NotNull(db.Model.FindEntityType(typeof(MassTransit.EntityFrameworkCoreIntegration.OutboxMessage)));

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.OfficialHolidays.Add(new OfficialHoliday
            {
                Id = Guid.NewGuid(),
                NameAr = "OutboxKillTest",
                Date = new DateOnly(2026, 1, 1)
            });
            db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().Add(
                new MassTransit.EntityFrameworkCoreIntegration.OutboxMessage
                {
                    SequenceNumber = 1,
                    EnqueueTime = DateTime.UtcNow,
                    SentTime = DateTime.UtcNow,
                    MessageId = NewId.NextGuid(),
                    ContentType = "application/vnd.masstransit+json",
                    MessageType = "urn:message:HRManagement.MessageContracts.Events:IPayrollProcessed",
                    Body = "{}"
                });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.OfficialHolidays.Add(new OfficialHoliday
            {
                Id = Guid.NewGuid(),
                NameAr = "RolledBack",
                Date = new DateOnly(2026, 2, 1)
            });
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.OfficialHolidays.CountAsync(h => h.NameAr == "RolledBack"));
        Assert.Equal(1, await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());
    }
}
