using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Privacy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HR.Core.Infrastructure.Data;

/// <summary>
/// PLAN-39 (section 4, D-39-4, P-3): HR.Core refuses to start when its Data Protection key ring cannot read the national
/// IDs already stored, instead of generating a new ring over protected data and breaking every employee screen
/// (<see cref="PiiFieldEncryptionService"/> throws on each read after a key loss).
/// <para>
/// Runs as an <see cref="IHrStartupCheck"/>, before migrations and seeds. It reads one protected value with raw SQL
/// (<c>LIMIT 1</c>, so the materialization interceptor that would decrypt it is not involved): first without a tenant
/// (the administrator and a <c>BYPASSRLS</c> role see every row), then, because <c>Employees</c> is under tenant row
/// level security (TEN-002C), once per tenant of <c>Tenants</c> with <c>app.current_tenant</c> set for that query only.
/// Protected data with no <c>key-*.xml</c> in the ring directory is a lost ring without trying to decrypt (an unprotect
/// on an empty ring would create a key). No protected data: nothing to check.
/// </para>
/// <para>
/// Enforced in Production and with <c>DataProtection:RequirePersistentKeyRing=true</c>; the emergency switch
/// <c>DataProtection:AllowKeyRingReset=true</c> lets the start go on with a <c>Critical</c> log and the counter
/// <c>dataprotection_key_ring_reset_total</c>. Messages name the service and the key-ring path, never a key or a
/// protected value.
/// </para>
/// </summary>
public sealed class EmployeePiiKeyRingCanary(
    IConfiguration configuration,
    IHostEnvironment environment,
    IDataProtectionProvider dataProtectionProvider,
    HrDataProtectionRegistration registration,
    ILogger<EmployeePiiKeyRingCanary> logger) : IHrStartupCheck
{
    public const string ConnectionStringName = "HRCoreDb";
    private const string ProtectedSampleSql =
        "SELECT \"NationalId\" FROM \"Employees\" WHERE \"NationalId\" LIKE 'CfDJ8%' LIMIT 1";

    private static readonly Meter Meter = new("HRManagement.DataProtection");
    private static readonly Counter<long> Resets = Meter.CreateCounter<long>(
        "dataprotection_key_ring_reset_total",
        description: "Starts that went on although the Data Protection key ring did not match the protected data (AllowKeyRingReset)");

    public string Name => "employee-pii-key-ring-canary";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var enforced = DataProtectionStartupGuard.IsEnforced(configuration, environment);
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString) || ProductionConfigurationGuard.IsPlaceholder(connectionString))
        {
            if (enforced)
                Refuse("ConnectionStrings:HRCoreDb غير مضبوط فلا يمكن فحص البيانات المحمية / is not set, the protected data cannot be checked");
            return;
        }

        CanaryResult outcome;
        try
        {
            outcome = await ProbeAsync(connectionString, configuration.GetValue("Database:RunMigrationsAtStartup", true), cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or ArgumentException)
        {
            // Enforced: a database the canary cannot read cannot be shown to match the key ring, so the start stops.
            if (enforced)
                Refuse($"قاعدة HR.Core غير مقروءة للفحص / the HR.Core database cannot be read for the check ({ex.GetType().Name})");
            logger.LogWarning("Key-ring canary of HR.Core skipped ({Environment}): the database is not reachable ({Error})", environment.EnvironmentName, ex.GetType().Name);
            return;
        }
        switch (outcome.Kind)
        {
            case CanaryOutcome.NoProtectedData:
                logger.LogInformation("Key-ring canary of HR.Core: {Detail}", outcome.Detail);
                return;
            case CanaryOutcome.Readable:
                logger.LogInformation("Key-ring canary of HR.Core: a protected national ID decrypts with the key ring at {Path}", registration.KeyRingPath);
                return;
        }

        var message =
            $"حلقة مفاتيح حماية البيانات لا تطابق البيانات المخزنة في HR.Core (المسار {registration.KeyRingPath ?? "(none)"}): {outcome.DetailAr}. "
            + $"The Data Protection key ring does not match the protected data of HR.Core (path {registration.KeyRingPath ?? "(none)"}): {outcome.Detail}. "
            + "Restore the key ring (backup of the dp_hr_core volume); DataProtection:AllowKeyRingReset=true is the emergency switch.";

        if (configuration.GetValue(DataProtectionExtensions.AllowKeyRingResetKey, false))
        {
            Resets.Add(1, new KeyValuePair<string, object?>("service", "HR.Core"));
            logger.LogCritical("Key-ring canary: {Message} Start goes on because DataProtection:AllowKeyRingReset=true; protected national IDs stay unreadable.", message);
            return;
        }

        if (!enforced)
        {
            logger.LogError("Key-ring canary (not enforced in {Environment}): {Message}", environment.EnvironmentName, message);
            return;
        }

        Refuse(message);
    }

    private void Refuse(string message)
    {
        logger.LogCritical("Key-ring canary: {Message}", message);
        throw new InvalidOperationException(message);
    }

    private async Task<CanaryResult> ProbeAsync(string connectionString, bool migrationsAtStartup, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var exists = new NpgsqlCommand("SELECT to_regclass('public.\"Employees\"') IS NOT NULL", connection))
        {
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
            {
                // A fresh database that this start migrates is empty; with migrations applied before the start (hrm-bootstrap)
                // a missing table is the wrong database, and that must not pass as "no protected data".
                return migrationsAtStartup
                    ? new CanaryResult(CanaryOutcome.NoProtectedData, "Employees does not exist yet (migrated at this start)", "")
                    : new CanaryResult(CanaryOutcome.Mismatch, "Employees does not exist although migrations run before the start", "جدول Employees غير موجود رغم أن الترحيلات تسبق الإقلاع");
            }
        }

        var sample = await ScalarAsync(connection, ProtectedSampleSql, null, cancellationToken);
        if (sample is null)
        {
            var tenants = new List<string>();
            // Every tenant (review F5: a cap could miss protected rows of a later tenant); each probe uses the
            // (TenantId, ...) index and the loop stops at the first protected value.
            await using (var list = new NpgsqlCommand("SELECT \"Id\"::text FROM \"Tenants\" ORDER BY \"Id\"", connection))
            await using (var reader = await list.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    tenants.Add(reader.GetString(0));
            }

            foreach (var tenant in tenants)
            {
                sample = await ScalarAsync(connection, ProtectedSampleSql, tenant, cancellationToken);
                if (sample is not null)
                    break;
            }
        }

        if (sample is null)
            return new CanaryResult(CanaryOutcome.NoProtectedData, "no protected national ID stored", "");

        var directory = registration.KeyRingPath;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || Directory.GetFiles(directory, "key-*.xml").Length == 0)
        {
            return new CanaryResult(CanaryOutcome.Mismatch,
                "protected national IDs are stored but the key ring holds no key (a new ring would be generated)",
                "توجد أرقام وطنية محمية وحلقة المفاتيح فارغة (كانت ستُولَّد حلقة جديدة)");
        }

        try
        {
            _ = dataProtectionProvider.CreateProtector(PiiFieldEncryptionService.Purpose).Unprotect(sample);
            return new CanaryResult(CanaryOutcome.Readable, "", "");
        }
        catch (CryptographicException ex)
        {
            return new CanaryResult(CanaryOutcome.Mismatch,
                $"a stored national ID does not decrypt with this key ring ({ex.GetType().Name})",
                "رقم وطني مخزن لا يُفك بهذه الحلقة");
        }
    }

    /// <summary>One value; with a tenant, inside a transaction whose <c>app.current_tenant</c> is local to it.</summary>
    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql, string? tenant, CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync(cancellationToken) as string;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @tenant, true)", connection, transaction))
        {
            set.Parameters.AddWithValue("tenant", tenant);
            await set.ExecuteScalarAsync(cancellationToken);
        }

        await using var query = new NpgsqlCommand(sql, connection, transaction);
        var value = await query.ExecuteScalarAsync(cancellationToken) as string;
        await transaction.RollbackAsync(cancellationToken);
        return value;
    }

    private enum CanaryOutcome { NoProtectedData, Readable, Mismatch }

    private sealed record CanaryResult(CanaryOutcome Kind, string Detail, string DetailAr);
}
