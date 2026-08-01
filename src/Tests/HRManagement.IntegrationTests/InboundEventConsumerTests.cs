using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Infrastructure.Consumers;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.MessageContracts.Events;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class InboundEventConsumerTests
{
    [Fact]
    public async Task TransactionRegisteredInDiwan_Updates_ServiceTransaction()
    {
        await using var scope = await CreateContractsScopeAsync();
        var db = scope.GetDb<ContractsDbContext>();
        var txId = Guid.NewGuid();
        db.ServiceTransactions.Add(new HR.Contracts.Domain.Entities.ServiceTransaction
        {
            Id = txId,
            EmployeeId = Guid.NewGuid(),
            TransactionType = HR.Contracts.Domain.Enums.ServiceTransactionType.Appointment
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ITransactionRegisteredInDiwan>(new
        {
            TransactionId = txId,
            DiwanNumber = "DIWAN-2026-001",
            RegisteredAt = DateTime.UtcNow,
            SourceSystem = "HR.Core",
            CorrelationId = (string?)null
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ITransactionRegisteredInDiwan>(scope.Harness);
        db.ChangeTracker.Clear();
        var tx = await db.ServiceTransactions.FindAsync(txId);
        Assert.NotNull(tx);
        Assert.Equal("DIWAN-2026-001", tx!.ReferenceNumber);
    }

    private static async Task<MassTransitTestScope> CreateContractsScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
                connection.Open();
                connections.Add(connection);
                IntegrationTestHelpers.AddSqliteDbContext<ContractsDbContext>(services, connection);
                services.AddSingleton(Mock.Of<IEventPublisher>());
                services.AddSingleton(Mock.Of<IEmployeeLifecycleGate>());
                services.AddMediatR(cfg =>
                    cfg.RegisterServicesFromAssemblyContaining<ProcessTransactionRegisteredInDiwanHandler>());
                services.AddScoped<IContractsRepository, ContractsRepository>();
            },
            cfg => cfg.AddConsumer<TransactionRegisteredInDiwanConsumer>());

        await scope.GetDb<ContractsDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }
}
