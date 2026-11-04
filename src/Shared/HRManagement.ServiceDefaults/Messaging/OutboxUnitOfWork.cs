using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-40 (§1, D-40-2): one transaction for state and outbox where a single save is not enough (an intermediate save
/// for a database-generated value, <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, handlers that save inside). Inside the
/// database execution strategy it opens a transaction, runs <paramref name="body"/> (intermediate saves allowed), then
/// saves once more, so every <c>OutboxMessage</c> the body staged is written, and commits. When a transaction is already
/// open on the context it joins it: it saves but neither begins nor commits (the owner commits).
/// <para>The body can run again when the execution strategy retries a transient failure: keep it re-runnable.</para>
/// </summary>
public static class OutboxUnitOfWork
{
    public static async Task ExecuteAsync(DbContext db, Func<CancellationToken, Task> body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        await ExecuteAsync<bool>(
            db,
            async token =>
            {
                await body(token);
                return true;
            },
            ct);
    }

    public static async Task<T> ExecuteAsync<T>(DbContext db, Func<CancellationToken, Task<T>> body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(body);

        // Joined (ambient transaction) or a provider without transactions (the EF in-memory test provider).
        if (db.Database.CurrentTransaction is not null || !db.Database.IsRelational())
        {
            var joined = await body(ct);
            await db.SaveChangesAsync(ct);
            return joined;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (db, body),
            async (_, state, token) =>
            {
                await using var tx = await state.db.Database.BeginTransactionAsync(token);
                var result = await state.body(token);
                await state.db.SaveChangesAsync(token);
                await tx.CommitAsync(token);
                return result;
            },
            verifySucceeded: null,
            ct);
    }
}
