using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HRManagement.IntegrationTests.Support;
using HRManagement.SharedKernel.Legal;
using HRManagement.TestSupport.Integration;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Fun002SeparationRecordIntegrationTests
{
    private static async Task<(IServiceScope Scope, SpyEventPublisher Spy, ContractsDbContext Db)> CreateScopeAsync(
        IEmployeeLifecycleGate? gate = null,
        FakeHrCoreActiveRegimeClient? activeRegimes = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var spy = new SpyEventPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IEventPublisher>(spy);
        services.AddSingleton<IEmployeeLifecycleGate>(gate ?? new AllowAllGate());
        services.AddContractsApplicationForTests(activeRegimes);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();
        return (scope, spy, db);
    }

    [Fact]
    public async Task Cancel_SoftCancels_DoesNotDelete()
    {
        var (scope, spy, db) = await CreateScopeAsync();
        using (scope)
        {
            var id = await SeedOpenSeparationAsync(db, DateOnly.FromDateTime(DateTime.UtcNow));
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (ok, err) = await mediator.Send(new CancelSeparationCommand(id, "اختبار", Guid.NewGuid()));
            Assert.True(ok);
            Assert.Null(err);

            db.ChangeTracker.Clear();
            var record = await db.SeparationRecords.SingleAsync(r => r.Id == id);
            Assert.True(record.IsCancelled);
            Assert.Equal(0, spy.SeparationSagaCancelledCount);
        }
    }

    [Fact]
    public async Task Publish_FutureEffectiveDate_Rejected()
    {
        var (scope, spy, db) = await CreateScopeAsync();
        using (scope)
        {
            var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, Guid.NewGuid(), null, future);
            var (ok, err) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);
            Assert.False(ok);
            Assert.Contains("النفاذ", err ?? "");
            Assert.Equal(0, spy.SeparationSagaStartedCount);
        }
    }

    [Fact]
    public async Task Publish_LegalHold_Rejected()
    {
        var (scope, spy, db) = await CreateScopeAsync(new BlockHoldGate());
        using (scope)
        {
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, Guid.NewGuid(), null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
            var (ok, err) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);
            Assert.False(ok);
            Assert.Contains("Legal Hold", err ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, spy.SeparationSagaStartedCount);
        }
    }

    // QA-034 (AC-QA-34e): the separation publish snapshots the employee's active regime from HR.Core.
    [Fact]
    public async Task Publish_with_law50_regime_publishes_once_and_snapshots_the_regime()
    {
        var regimes = new FakeHrCoreActiveRegimeClient();
        var (scope, spy, db) = await CreateScopeAsync(activeRegimes: regimes);
        using (scope)
        {
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, Guid.NewGuid(), null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
            var (ok, err) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);
            Assert.True(ok, err);
            Assert.Equal(1, spy.SeparationSagaStartedCount);
            Assert.Single(regimes.Requests);

            db.ChangeTracker.Clear();
            var record = await db.SeparationRecords.SingleAsync(r => r.Id == id);
            Assert.Equal(FakeHrCoreActiveRegimeClient.Law50, record.SourceLegalRegimeId);
            Assert.Equal(SeparationStatus.Executing, record.Status);
            Assert.True(record.SeparationEventPublished);
        }
    }

    [Fact]
    public async Task Publish_without_an_active_regime_is_refused_without_an_event()
    {
        var (scope, spy, db) = await CreateScopeAsync(activeRegimes: new FakeHrCoreActiveRegimeClient(FakeRegimeMode.NoRegime));
        using (scope)
        {
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, Guid.NewGuid(), null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
            var (ok, err) = await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id);
            Assert.False(ok);
            Assert.Equal(ServiceRegimeSeparationRules.RegimeRulesNotFoundMessage, err);
            Assert.Equal(0, spy.SeparationSagaStartedCount);
            db.ChangeTracker.Clear();
            Assert.False((await db.SeparationRecords.SingleAsync(r => r.Id == id)).SeparationEventPublished);
        }
    }

    // Recorded product defect (→ CON): with HR.Core unreachable the publish throws instead of returning an error;
    // this pins today's behaviour (nothing is published, nothing is saved) until the CON plan decides the fallback.
    [Fact]
    public async Task Publish_with_hr_core_unavailable_fails_without_an_event()
    {
        var (scope, spy, db) = await CreateScopeAsync(activeRegimes: new FakeHrCoreActiveRegimeClient(FakeRegimeMode.Unavailable));
        using (scope)
        {
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, Guid.NewGuid(), null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
            await Assert.ThrowsAsync<HttpRequestException>(() => SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id));
            Assert.Equal(0, spy.SeparationSagaStartedCount);
            db.ChangeTracker.Clear();
            Assert.False((await db.SeparationRecords.SingleAsync(r => r.Id == id)).SeparationEventPublished);
        }
    }

    // PLAN-60 (§1, rule 2): before, an executed record was cancelled while its date was in the future and its saga compensated. An
    // executing separation is never cancelled here: undoing it is PLAN-63's compensation path.
    [Fact]
    public async Task Cancel_after_the_execution_is_refused_until_PLAN63()
    {
        var (scope, spy, db) = await CreateScopeAsync();
        using (scope)
        {
            var id = await SeparationJourneyDriver.ReadyToExecuteAsync(scope.ServiceProvider, Guid.NewGuid(), null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
            Assert.True((await SeparationJourneyDriver.ExecuteAsync(scope.ServiceProvider, id)).Success);

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (ok, err) = await mediator.Send(new CancelSeparationCommand(id, "قبل النفاذ", Guid.NewGuid()));
            Assert.False(ok);
            Assert.Equal(SeparationWorkflowRules.ExecutingCancelMessage, err);
            Assert.Equal(0, spy.SeparationSagaCancelledCount);
            Assert.Equal(1, spy.SeparationSagaStartedCount);

            db.ChangeTracker.Clear();
            var stored = await db.SeparationRecords.SingleAsync(r => r.Id == id);
            Assert.False(stored.IsCancelled);
            Assert.Equal(SeparationStatus.Executing, stored.Status);
        }
    }

    private static async Task<Guid> SeedOpenSeparationAsync(
        ContractsDbContext db,
        DateOnly effectiveDate,
        bool completeChecklist = false)
    // completeChecklist is kept for the draft's own items only; a record is approved through SeparationJourneyDriver.
    {
        var record = new SeparationRecord
        {
            EmployeeId = Guid.NewGuid(),
            SeparationType = SeparationType.Resignation,
            EffectiveDate = effectiveDate
        };
        // PLAN-60: a record ready for the execution is approved (through the state machine) with its pre-execution items done.
        if (completeChecklist)
            SeparationTestJourney.MoveTo(record, SeparationStatus.Approved);
        db.SeparationRecords.Add(record);
        foreach (var item in SeparationChecklistDefaults.CreateForRecord(record.Id))
        {
            if (completeChecklist)
            {
                item.IsCompleted = true;
                item.CompletedAt = DateTime.UtcNow;
                item.CompletedBy = Guid.NewGuid();
                item.EvidenceRef = "test:seed";
            }
            db.SeparationChecklists.Add(item);
        }
        await db.SaveChangesAsync();
        return record.Id;
    }

    private sealed class AllowAllGate : IEmployeeLifecycleGate
    {
        public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));
    }

    private sealed class BlockHoldGate : IEmployeeLifecycleGate
    {
        public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((false, "الموظف تحت حجز قانوني (Legal Hold) — لا يمكن نشر الفصل."));
    }
}
