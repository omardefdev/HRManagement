using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-40 (D-40-6): how a party consumer ends. The work and its <c>*PartyCompleted</c> run in <c>work</c>. On a transient
/// failure with retries left it rethrows and publishes nothing. On a business failure, or a transient one on the last
/// attempt, it undoes the party's work back to a savepoint, publishes <c>*PartyFaulted</c> once and returns, so the fault
/// commits with the inbox receipt and the saga sees exactly one outcome. The savepoint also keeps the outbox transaction
/// usable when the failure was a database error (PostgreSQL refuses every statement after one until a rollback).
/// </summary>
public static class PartyAckFlow
{
    private const string Savepoint = "party_ack";

    public static async Task RunAsync(ConsumeContext context, Func<Task> work, Func<Exception, Task> publishFaulted)
    {
        var db = context.TryGetPayload<IServiceProvider>(out var provider) ? provider.GetService<DbContext>() : null;
        var transaction = db?.Database.CurrentTransaction;
        var useSavepoint = transaction is { SupportsSavepoints: true };
        if (useSavepoint)
            await transaction!.CreateSavepointAsync(Savepoint, context.CancellationToken);

        try
        {
            await work();
        }
        catch (Exception ex) when (!MessagingFaults.ShouldRetry(ex, context))
        {
            if (useSavepoint)
                await transaction!.RollbackToSavepointAsync(Savepoint, context.CancellationToken);
            db?.ChangeTracker.Clear();
            await publishFaulted(ex);
        }
    }
}
