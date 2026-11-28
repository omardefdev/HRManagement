using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;
using HRManagement.IntegrationTests.Support;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-60 (the plan's PostgreSQL class): on PostgreSQL itself — the partial unique index, the <c>xmin</c> token between two
/// submissions, the backfill on planted data, and (P4) the execution saved in one unit with its outbox row.
/// </summary>
[Collection("PostgreSql")]
public sealed partial class Sep060SeparationPostgresIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly Guid Tenant = Guid.Parse("a0000060-0000-4000-8000-0000000000d1");

    private static ContractsDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<ContractsDbContext>().UseNpgsql(connection).Options, new Msg040FixedTenant(Tenant));

    private async Task<string> MigratedAsync(string name)
    {
        var connection = await fixture.CreateDatabaseAsync($"sep060p_{name}_{Guid.NewGuid():N}"[..40]);
        await using var db = Db(connection);
        await db.Database.MigrateAsync();
        return connection;
    }

    private static SeparationRecord Draft(Guid employee) =>
        new() { TenantId = Tenant, EmployeeId = employee, SeparationType = SeparationType.Resignation, EffectiveDate = new DateOnly(2026, 12, 31) };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_partial_index_refuses_a_second_open_record_and_accepts_one_after_a_cancellation()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await MigratedAsync("index");
        var employee = Guid.NewGuid();
        var first = Draft(employee);
        await using (var db = Db(connection))
        {
            db.SeparationRecords.Add(first);
            await db.SaveChangesAsync();
        }

        await using (var db = Db(connection))
        {
            db.SeparationRecords.Add(Draft(employee));
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.True(HR.Contracts.Application.Features.SeparationRecords.SeparationRecordFactory.IsOpenRecordViolation(refused));
        }

        await using (var db = Db(connection))
        {
            var stored = await db.SeparationRecords.SingleAsync(r => r.Id == first.Id);
            SeparationWorkflowRules.Transition(stored, SeparationStatus.Cancelled, Guid.NewGuid(), "إدخال مكرر");
            await db.SaveChangesAsync();
        }

        await using (var db = Db(connection))
        {
            db.SeparationRecords.Add(Draft(employee));
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.SeparationRecords.CountAsync(r => r.EmployeeId == employee));
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Of_two_concurrent_submissions_xmin_lets_one_through()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await MigratedAsync("xmin");
        var record = Draft(Guid.NewGuid());
        await using (var db = Db(connection))
        {
            db.SeparationRecords.Add(record);
            await db.SaveChangesAsync();
        }

        await using var one = Db(connection);
        await using var two = Db(connection);
        var a = await one.SeparationRecords.SingleAsync(r => r.Id == record.Id);
        var b = await two.SeparationRecords.SingleAsync(r => r.Id == record.Id);
        var firstInstance = Guid.NewGuid();
        SeparationWorkflowRules.Transition(a, SeparationStatus.PendingApproval, Guid.NewGuid(), null,
            new SeparationSubmission(firstInstance, "SEPARATION-APPROVAL", 1));
        SeparationWorkflowRules.Transition(b, SeparationStatus.PendingApproval, Guid.NewGuid(), null,
            new SeparationSubmission(Guid.NewGuid(), "SEPARATION-APPROVAL", 1));

        await one.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => two.SaveChangesAsync());

        await using var check = Db(connection);
        var stored = await check.SeparationRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id);
        Assert.Equal((SeparationStatus.PendingApproval, firstInstance), (stored.Status, stored.WorkflowInstanceId!.Value));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_backfill_on_planted_data_gives_the_four_combinations_their_statuses_and_cancels_the_unstarted_duplicate()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await fixture.CreateDatabaseAsync($"sep060b_{Guid.NewGuid():N}"[..40]);
        await using (var db = Db(admin))
            await db.GetService<IMigrator>().MigrateAsync(Sep060MigrationPostgreSqlTests.PreviousMigration);

        var draft = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, Guid.NewGuid(), published: false);
        var cancelled = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, Guid.NewGuid(), published: true, cancelled: true);
        var executing = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, Guid.NewGuid(), published: true);
        var completed = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, Guid.NewGuid(), published: true);
        await Sep060MigrationPostgreSqlTests.SagaAsync(admin, Tenant, completed, "Completed");
        var employee = Guid.NewGuid();
        var kept = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, employee, published: true, createdAt: DateTime.UtcNow.AddDays(-5));
        var duplicate = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, employee, published: false);
        // P6's review (finding 5): the old compensation reset the flag and kept the saga row. A compensated saga cancels its record (not
        // a legacy draft to execute again), a saga still compensating keeps it Executing, and a newer live draft of the same employee
        // stays open (the compensated record is not open, so it is no duplicate's keeper).
        var compensatedEmployee = Guid.NewGuid();
        var compensated = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, compensatedEmployee, published: false,
            createdAt: DateTime.UtcNow.AddDays(-9));
        await Sep060MigrationPostgreSqlTests.SagaAsync(admin, Tenant, compensated, "Cancelled");
        var newerDraft = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, compensatedEmployee, published: false);
        var compensating = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, Guid.NewGuid(), published: false);
        await Sep060MigrationPostgreSqlTests.SagaAsync(admin, Tenant, compensating, "Compensating");

        await using (var db = Db(admin))
            await db.Database.MigrateAsync();

        await using var read = Db(admin);
        var statuses = await read.SeparationRecords.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Status);
        Assert.Equal(SeparationStatus.Draft, statuses[draft]);
        Assert.Equal(SeparationStatus.Cancelled, statuses[cancelled]);
        Assert.Equal(SeparationStatus.Executing, statuses[executing]);
        Assert.Equal(SeparationStatus.Completed, statuses[completed]);
        Assert.Equal(SeparationStatus.Executing, statuses[kept]);
        Assert.Equal(SeparationStatus.Cancelled, statuses[duplicate]);
        Assert.True((await read.SeparationRecords.AsNoTracking().SingleAsync(r => r.Id == draft)).IsLegacyDraft);
        Assert.Equal(SeparationStatus.Cancelled, statuses[compensated]);
        var compensatedRow = await read.SeparationRecords.AsNoTracking().SingleAsync(r => r.Id == compensated);
        Assert.True(compensatedRow.IsCancelled && !compensatedRow.IsLegacyDraft);
        Assert.Equal(SeparationStatus.Draft, statuses[newerDraft]);
        Assert.Equal(SeparationStatus.Executing, statuses[compensating]);
    }
}

/// <summary>PLAN-60 (§5, P-5's fourth case): the execution in one unit with MassTransit's EF outbox, on PostgreSQL.</summary>
public sealed partial class Sep060SeparationPostgresIntegrationTests
{
    private sealed class FailExecutionSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<SeparationRecord>().Any(e => e.Entity.Status == SeparationStatus.Executing))
                throw new DbUpdateException("PLAN-60 test: the database refused the execution's save.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class Allow : HR.Contracts.Application.Abstractions.IEmployeeLifecycleGate
    {
        public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_execution_saves_the_contracts_end_Executing_and_the_saga_outbox_row_together_or_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await MigratedAsync("outbox");
        var failure = new FailExecutionSave();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseNpgsql(connection).AddInterceptors(failure));
        services.AddSingleton<HR.Contracts.Application.Abstractions.IEmployeeLifecycleGate>(new Allow());
        services.AddSingleton(Moq.Mock.Of<HR.Contracts.Application.Abstractions.IExecutionContextAccessor>(a => a.SourceSystem == "HRContracts"));
        services.AddScoped<HR.Contracts.Application.Abstractions.IEventPublisher, HR.Contracts.Infrastructure.Messaging.MassTransitEventPublisher>();
        services.AddMassTransit(x =>
        {
            x.AddPostgresOutbox<ContractsDbContext>();
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });
        services.AddContractsApplicationForTests();
        await using var provider = services.BuildServiceProvider();

        async Task<(Guid Record, Guid Contract)> ReadyAsync()
        {
            var employee = Guid.NewGuid();
            Guid contractId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
                var type = TestDataFactory.CreateContractType($"S{Guid.NewGuid():N}"[..8]);
                var contract = TestDataFactory.CreatePendingContract(employee, type);
                contract.Status = ContractStatus.Active;
                contract.StartDate = new DateOnly(2024, 1, 1);
                db.ContractTypes.Add(type);
                db.Contracts.Add(contract);
                await db.SaveChangesAsync();
                contractId = contract.Id;
            }

            return (await SeparationJourneyDriver.ReadyToExecuteAsync(provider, employee, contractId, new DateOnly(2026, 11, 1)), contractId);
        }

        Task<long> SagaRowsAsync(Guid record) => Pol047Pg.ScalarAsync<long>(connection,
            $"""SELECT count(*) FROM "OutboxMessage" WHERE "MessageType" LIKE '%IStartSeparationSaga%' AND "Body" LIKE '%{record}%'""");

        // Together: the record, the contract and the saga's outbox row, in the one save.
        var (record, contract) = await ReadyAsync();
        Assert.Equal(0L, await SagaRowsAsync(record));
        Assert.True((await SeparationJourneyDriver.ExecuteAsync(provider, record)).Success);
        Assert.Equal(1L, await SagaRowsAsync(record));
        Assert.Equal("Executing|Terminated|2026-11-01", await Pol047Pg.ScalarAsync<string>(connection, $"""
            SELECT r."Status" || '|' || c."Status" || '|' || c."EndDate"::text
            FROM "SeparationRecords" r JOIN "Contracts" c ON c."Id" = r."ContractId" WHERE r."Id" = '{record}'
            """));

        // Or nothing: the save fails, and neither the status, nor the contract's end, nor the outbox row is kept.
        var (second, secondContract) = await ReadyAsync();
        failure.Armed = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => SeparationJourneyDriver.ExecuteAsync(provider, second));
        Assert.Equal(0L, await SagaRowsAsync(second));
        Assert.Equal("Approved|Active|", await Pol047Pg.ScalarAsync<string>(connection, $"""
            SELECT r."Status" || '|' || c."Status" || '|' || coalesce(c."EndDate"::text, '')
            FROM "SeparationRecords" r JOIN "Contracts" c ON c."Id" = r."ContractId" WHERE r."Id" = '{second}'
            """));
        Assert.NotEqual(contract, secondContract);
    }
}

/// <summary>
/// PLAN-60 (P6's review, finding 7): two issuances of one record's decision at once. The record's <c>xmin</c> lets one through; the other
/// answers with the winner's decision instead of a 500, and its own number is never saved.
/// </summary>
public sealed partial class Sep060SeparationPostgresIntegrationTests
{
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Of_two_concurrent_issuances_the_loser_answers_with_the_winners_decision_and_saves_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await MigratedAsync("race");
        var codes = new HRManagement.TestSupport.CodeGeneration.FakeCodeGenerationClient();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseNpgsql(connection));
        services.AddSingleton(Moq.Mock.Of<HR.Contracts.Application.Abstractions.IExecutionContextAccessor>(a => a.SourceSystem == "HRContracts"));
        services.AddSingleton(Moq.Mock.Of<HR.Contracts.Application.Abstractions.IEventPublisher>());
        services.AddContractsApplicationForTests();
        services.AddSingleton<HRManagement.ServiceDefaults.CodeGeneration.ICodeGenerationClient>(codes);
        await using var provider = services.BuildServiceProvider();

        var employee = Guid.NewGuid();
        var record = await SeparationJourneyDriver.CreateAsync(provider, employee, null, new DateOnly(2026, 11, 1));
        await SeparationJourneyDriver.ApproveAsync(provider, record);

        // The winner: while the loser's number is generated, another issuance links its decision (and so moves the row's xmin). The
        // side contexts are the host's own (no tenant context), so they see the record the host created.
        ContractsDbContext Plain() => new(new DbContextOptionsBuilder<ContractsDbContext>().UseNpgsql(connection).Options);
        var winner = Guid.NewGuid();
        codes.GenerateHandler = _ =>
        {
            codes.GenerateHandler = null;
            using var other = Plain();
            var row = other.SeparationRecords.Single(r => r.Id == record);
            other.AdministrativeDecisions.Add(new AdministrativeDecision
            {
                Id = winner,
                TenantId = row.TenantId,
                DecisionNumber = "DEC-SEP-2026-00001",
                Subject = "الفائز"
            });
            row.DecisionId = winner;
            other.SaveChanges();
            return new HRManagement.SharedKernel.CodeGeneration.CodeGenerationResult { Code = "DEC-SEP-2026-00002" };
        };

        var result = await SeparationJourneyDriver.InScopeAsync(provider, sp => sp.GetRequiredService<MediatR.IMediator>()
            .Send(new HR.Contracts.Application.Features.SeparationRecords.IssueSeparationDecisionCommand(record, SeparationJourneyDriver.Checker, "المدقق")));

        Assert.True(result.Success, result.Error);
        Assert.True(result.AlreadyIssued);
        Assert.Equal(winner, result.Decision!.Id);
        await using var read = Plain();
        Assert.Equal(winner, (await read.SeparationRecords.AsNoTracking().SingleAsync(r => r.Id == record)).DecisionId);
        Assert.False(await read.AdministrativeDecisions.AsNoTracking().AnyAsync(d => d.DecisionNumber == "DEC-SEP-2026-00002"));
    }
}
