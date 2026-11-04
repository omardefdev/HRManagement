using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-40 (§5): the end of a start-up or background scope that publishes. No request guard runs there, so the scope
/// flushes what is still staged; without it the messages would be discarded with the scope.
/// </summary>
public static class OutboxScope
{
    public static int CountUnsaved(DbContext db) =>
        db.ChangeTracker.Entries<OutboxMessage>().Count(e => e.State == EntityState.Added);

    /// <returns>The number of messages that were still unsaved.</returns>
    public static async Task<int> EnsureFlushedAsync(DbContext db, CancellationToken ct = default)
    {
        var unsaved = CountUnsaved(db);
        if (unsaved > 0)
            await db.SaveChangesAsync(ct);
        return unsaved;
    }
}
