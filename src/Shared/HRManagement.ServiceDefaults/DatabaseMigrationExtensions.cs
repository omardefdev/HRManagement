using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HRManagement.ServiceDefaults;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Same logical key as <c>scripts/run-migrations.sh</c> (session advisory lock).
    /// </summary>
    public const long MigrationAdvisoryLockKey = 8675309;

    /// <summary>
    /// Runs EF Core migrations when <c>Database:RunMigrationsAtStartup</c> is true (default).
    /// Set to false in production when migrations are applied from CI/CD (<c>dotnet ef database update</c> or equivalent).
    /// For Npgsql, acquires <c>pg_advisory_lock</c> before <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/>.
    /// </summary>
    public static async Task TryMigrateAsync<TContext>(this IServiceProvider services, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Database");
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetService<IHostEnvironment>();
        var isProduction = environment?.IsProduction()
            ?? string.Equals(
                configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"],
                Environments.Production,
                StringComparison.OrdinalIgnoreCase);

        if (!configuration.GetValue("Database:RunMigrationsAtStartup", true))
        {
            logger.LogInformation(
                "Skipping EF migrations at startup for {Context} (Database:RunMigrationsAtStartup=false).",
                typeof(TContext).Name);
            return;
        }

        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var isNpgsql = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
            if (isNpgsql)
            {
                var connectionString = db.Database.GetDbConnection().ConnectionString;
                await EnsureNpgsqlDatabaseExistsAsync(connectionString, cancellationToken);
                await MigrateWithAdvisoryLockAsync(db, connectionString, logger, typeof(TContext).Name, cancellationToken);
                await ApplyTenantRlsHardeningAsync(db, logger, typeof(TContext).Name, isProduction, cancellationToken);
            }
            else
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (isProduction)
            {
                logger.LogCritical(ex, "EF migration failed for {Context} in Production; aborting startup.", typeof(TContext).Name);
                throw;
            }

            logger.LogWarning(ex, "Database migration failed for {Context}; continuing startup (non-production).", typeof(TContext).Name);
        }
    }

    private static async Task MigrateWithAdvisoryLockAsync(
        DbContext db,
        string? connectionString,
        ILogger logger,
        string contextName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        await using var lockConn = new NpgsqlConnection(connectionString);
        await lockConn.OpenAsync(cancellationToken);
        await using (var lockCmd = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationAdvisoryLockKey})", lockConn))
        {
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation(
            "Acquired pg_advisory_lock({LockKey}) for {Context} migrations.",
            MigrationAdvisoryLockKey,
            contextName);

        try
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationAdvisoryLockKey})", lockConn);
                await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch (Exception unlockEx)
            {
                logger.LogWarning(unlockEx, "Failed to release pg_advisory_unlock for {Context}; connection dispose will release it.", contextName);
            }
        }
    }

    /// <summary>
    /// TEN-002C: re-applies ENABLE + FORCE row level security and the tenant_isolation policy
    /// (USING and WITH CHECK) after migrations, then verifies no tenant table was left unhardened.
    /// <para>
    /// Without FORCE, PostgreSQL exempts the table owner from RLS — and the migrating role owns
    /// the tables — so RLS would silently have no effect. Verification failures abort startup in
    /// Production rather than run with tenant isolation disabled.
    /// </para>
    /// </summary>
    public static async Task ApplyTenantRlsHardeningAsync(
        DbContext db,
        ILogger logger,
        string contextName,
        bool isProduction,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                TenantRlsMigrationHelper.HardenAllTenantIdTablesSql,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply tenant RLS hardening for {Context}.", contextName);
            if (isProduction)
                throw;
            return;
        }

        var unhardened = await ListUnhardenedTenantTablesAsync(db, cancellationToken);
        if (unhardened.Count == 0)
        {
            logger.LogInformation("Tenant RLS hardening verified for {Context}.", contextName);
            return;
        }

        var detail = string.Join(", ", unhardened);
        if (isProduction)
        {
            throw new InvalidOperationException(
                $"Tenant isolation is not enforced for {contextName}. Unhardened tables: {detail}. " +
                "Tables must have RLS enabled and forced with a tenant_isolation policy carrying WITH CHECK. " +
                "This usually means the migrating role does not own them — run migrations as the *_migrator role.");
        }

        logger.LogWarning(
            "Tenant RLS not fully hardened for {Context} (non-production). Unhardened tables: {Tables}.",
            contextName,
            detail);
    }

    /// <summary>Names of tenant tables missing ENABLE, FORCE, or a WITH CHECK policy.</summary>
    public static async Task<IReadOnlyList<string>> ListUnhardenedTenantTablesAsync(
        DbContext db,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        var connection = db.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = TenantRlsMigrationHelper.VerifyTenantRlsSql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.GetString(0);
                var enabled = reader.GetBoolean(1);
                var forced = reader.GetBoolean(2);
                var withCheck = reader.GetBoolean(3);
                results.Add($"{table} (enabled={enabled}, forced={forced}, withCheck={withCheck})");
            }
        }
        finally
        {
            if (opened)
                await db.Database.CloseConnectionAsync();
        }

        return results;
    }

    /// <summary>
    /// ينشئ قاعدة البيانات المحددة في سلسلة الاتصال إن لم تكن موجودة، ثم يطبّق EF ترحيل الجداول.
    /// </summary>
    private static async Task EnsureNpgsqlDatabaseExistsAsync(string? connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database)) return;
        if (!Regex.IsMatch(database, @"^[a-zA-Z0-9_]+$")) return;

        builder.Database = "postgres";
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using (var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = $1", conn))
        {
            check.Parameters.AddWithValue(database);
            var exists = await check.ExecuteScalarAsync(cancellationToken);
            if (exists != null) return;
        }

        var createSql = $"""CREATE DATABASE "{database}" """;
        await using var create = new NpgsqlCommand(createSql, conn);
        await create.ExecuteNonQueryAsync(cancellationToken);
    }
}
