using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class DiwanPublisherTests
{
    [Fact]
    public async Task RegisterCorrespondence_WithServiceTransactionId_Publishes_Event()
    {
        await using var scope = await CreateScopeAsync();
        var db = scope.GetDb<HRCoreDbContext>();
        using var serviceScope = scope.Provider.CreateScope();
        var integration = serviceScope.ServiceProvider.GetRequiredService<DiwanIntegrationService>();
        var txId = Guid.NewGuid();

        await integration.RegisterCorrespondenceAsync(new DiwanCorrespondence
        {
            ReferenceNumber = "DIWAN-REF-99",
            Subject = "Test correspondence",
            Direction = CorrespondenceDirection.Inbound,
            RegisteredAt = DateTime.UtcNow,
            ServiceTransactionId = txId
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ITransactionRegisteredInDiwan>(scope.Harness);
    }

    private static async Task<MassTransitTestScope> CreateScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
                connection.Open();
                connections.Add(connection);
                IntegrationTestHelpers.AddSqliteDbContext<HRCoreDbContext>(services, connection);
                services.AddScoped<DiwanIntegrationService>();
            },
            cfg => cfg.AddConsumer<TransactionRegisteredInDiwanStubConsumer>());

        await scope.GetDb<HRCoreDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    private sealed class TransactionRegisteredInDiwanStubConsumer : IConsumer<ITransactionRegisteredInDiwan>
    {
        public Task Consume(ConsumeContext<ITransactionRegisteredInDiwan> context) => Task.CompletedTask;
    }
}
