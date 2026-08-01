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

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Fun002SeparationRecordIntegrationTests
{
    private static async Task<(IServiceScope Scope, SpyEventPublisher Spy, ContractsDbContext Db)> CreateScopeAsync(
        IEmployeeLifecycleGate? gate = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var spy = new SpyEventPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IContractsRepository, ContractsRepository>();
        services.AddSingleton<IEventPublisher>(spy);
        services.AddSingleton<IEmployeeLifecycleGate>(gate ?? new AllowAllGate());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PublishSeparationHandler>());

        var provider = services.BuildServiceProvider();
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
            var (ok, err) = await mediator.Send(new CancelSeparationCommand(id, "اختبار"));
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
            var id = await SeedOpenSeparationAsync(db, future, completeChecklist: true);
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (ok, err) = await mediator.Send(new PublishSeparationCommand(id));
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
            var id = await SeedOpenSeparationAsync(db, DateOnly.FromDateTime(DateTime.UtcNow), completeChecklist: true);
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (ok, err) = await mediator.Send(new PublishSeparationCommand(id));
            Assert.False(ok);
            Assert.Contains("Legal Hold", err ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, spy.SeparationSagaStartedCount);
        }
    }

    [Fact]
    public async Task Cancel_AfterPublish_PreEffective_PublishesCancelSaga()
    {
        var (scope, spy, db) = await CreateScopeAsync();
        using (scope)
        {
            var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
            var id = await SeedOpenSeparationAsync(db, future, completeChecklist: true);
            var record = await db.SeparationRecords.SingleAsync(r => r.Id == id);
            record.SeparationEventPublished = true;
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var (ok, _) = await mediator.Send(new CancelSeparationCommand(id, "قبل النفاذ"));
            Assert.True(ok);
            Assert.Equal(1, spy.SeparationSagaCancelledCount);
            Assert.Equal(id, spy.LastCancelledSeparationRecordId);

            db.ChangeTracker.Clear();
            Assert.True((await db.SeparationRecords.SingleAsync(r => r.Id == id)).IsCancelled);
        }
    }

    private static async Task<Guid> SeedOpenSeparationAsync(
        ContractsDbContext db,
        DateOnly effectiveDate,
        bool completeChecklist = false)
    {
        var record = new SeparationRecord
        {
            EmployeeId = Guid.NewGuid(),
            SeparationType = SeparationType.Resignation,
            EffectiveDate = effectiveDate
        };
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
        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));
    }

    private sealed class BlockHoldGate : IEmployeeLifecycleGate
    {
        public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((false, "الموظف تحت حجز قانوني (Legal Hold) — لا يمكن نشر الفصل."));
    }
}
