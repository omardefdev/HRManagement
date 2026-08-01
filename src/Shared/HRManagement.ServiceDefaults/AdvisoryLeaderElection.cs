using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>PostgreSQL advisory-lock helper for cluster-wide singleton jobs (HA-DR).</summary>
public static class AdvisoryLeaderElection
{
    public static async Task<bool> TryRunAsLeaderAsync(
        DbContext db,
        long lockKey,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql())
        {
            await action(cancellationToken);
            return true;
        }

        await using var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT pg_try_advisory_lock({lockKey})";
            var acquired = await command.ExecuteScalarAsync(cancellationToken) as bool? ?? false;
            if (!acquired)
                return false;

            try
            {
                await action(cancellationToken);
            }
            finally
            {
                await using var unlock = connection.CreateCommand();
                unlock.CommandText = $"SELECT pg_advisory_unlock({lockKey})";
                await unlock.ExecuteScalarAsync(cancellationToken);
            }

            return true;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
