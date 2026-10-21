using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRManagement.ServiceDefaults.Seeding;

public sealed record AcceptanceSeedReceipt(string Fingerprint, string Status);

/// <summary>
/// Acceptance-only receipt table shared by all service databases. It intentionally does not
/// participate in production EF models or migrations.
/// </summary>
public static class AcceptanceSeedReceiptStore
{
    public const string AppliedStatus = "Applied";

    public static async Task EnsureTableAsync(DbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational())
            return;

        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var sql = isPostgres
            ? """
              CREATE TABLE IF NOT EXISTS "SeedPackReceipts" (
                  "TenantId" uuid NOT NULL,
                  "ServiceName" varchar(128) NOT NULL,
                  "PackId" varchar(64) NOT NULL,
                  "Version" varchar(32) NOT NULL,
                  "Fingerprint" varchar(128) NOT NULL,
                  "Status" varchar(32) NOT NULL,
                  "EntityCountsJson" jsonb NOT NULL,
                  "AppliedAt" timestamp with time zone NOT NULL,
                  PRIMARY KEY ("TenantId", "ServiceName", "PackId")
              );
              """
            : """
              CREATE TABLE IF NOT EXISTS "SeedPackReceipts" (
                  "TenantId" TEXT NOT NULL,
                  "ServiceName" TEXT NOT NULL,
                  "PackId" TEXT NOT NULL,
                  "Version" TEXT NOT NULL,
                  "Fingerprint" TEXT NOT NULL,
                  "Status" TEXT NOT NULL,
                  "EntityCountsJson" TEXT NOT NULL,
                  "AppliedAt" TEXT NOT NULL,
                  PRIMARY KEY ("TenantId", "ServiceName", "PackId")
              );
              """;
        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    public static async Task<bool> ShouldApplyAsync(
        DbContext db,
        Guid tenantId,
        string serviceName,
        CancellationToken ct = default)
    {
        if (!db.Database.IsRelational())
            return true;

        await EnsureTableAsync(db, ct);
        var receipt = await ReadAsync(db, tenantId, serviceName, ct);
        if (receipt is null)
            return true;
        if (!string.Equals(receipt.Fingerprint, AcceptanceSeedPackFingerprint.Value, StringComparison.Ordinal)
            || !string.Equals(receipt.Status, AppliedStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{serviceName} contains Acceptance seed receipt with a different fingerprint or state. " +
                "Run scripts/acceptance-reset.ps1 before applying Acceptance v2.");
        }
        return false;
    }

    public static async Task WriteAppliedAsync(
        DbContext db,
        AcceptanceAgency agency,
        string serviceName,
        CancellationToken ct = default)
    {
        if (!db.Database.IsRelational())
            return;

        var counts = JsonSerializer.Serialize(new
        {
            employees = agency.EmployeeCount,
            orgUnits = agency.OrgUnitCount,
            payableEmployees = agency.PayableEmployeeCount,
            currentContracts = agency.CurrentContractCount,
            separations = agency.SeparationCount,
            leaveRequests = agency.Volume.LeaveRequests,
            advances = agency.Volume.Advances,
            transfers = agency.Volume.Transfers,
            secondments = agency.Volume.Secondments,
            loans = agency.Volume.Loans,
            trainingEnrollments = agency.Volume.TrainingEnrollments,
            cards = agency.Volume.IssuedCardsAndRequests,
            assets = agency.Volume.Assets,
            assetAssignments = agency.Volume.AssetAssignments,
            complianceCases = agency.Volume.ComplianceCases,
            dsarRequests = agency.Volume.DsarRequests,
            files = agency.Volume.Files,
            recruitmentApplications = agency.Volume.RecruitmentApplications
        });

        var postgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var sql = postgres
            ? """
              INSERT INTO "SeedPackReceipts"
                  ("TenantId", "ServiceName", "PackId", "Version", "Fingerprint", "Status", "EntityCountsJson", "AppliedAt")
              VALUES (@tenantId, @serviceName, @packId, @version, @fingerprint, @status, CAST(@counts AS jsonb), @appliedAt)
              ON CONFLICT ("TenantId", "ServiceName", "PackId") DO UPDATE SET
                  "Version" = EXCLUDED."Version",
                  "Fingerprint" = EXCLUDED."Fingerprint",
                  "Status" = EXCLUDED."Status",
                  "EntityCountsJson" = EXCLUDED."EntityCountsJson",
                  "AppliedAt" = EXCLUDED."AppliedAt";
              """
            : """
              INSERT INTO "SeedPackReceipts"
                  ("TenantId", "ServiceName", "PackId", "Version", "Fingerprint", "Status", "EntityCountsJson", "AppliedAt")
              VALUES (@tenantId, @serviceName, @packId, @version, @fingerprint, @status, @counts, @appliedAt)
              ON CONFLICT ("TenantId", "ServiceName", "PackId") DO UPDATE SET
                  "Version" = excluded."Version",
                  "Fingerprint" = excluded."Fingerprint",
                  "Status" = excluded."Status",
                  "EntityCountsJson" = excluded."EntityCountsJson",
                  "AppliedAt" = excluded."AppliedAt";
              """;

        await ExecuteAsync(db, sql, ct,
            ("tenantId", agency.Id),
            ("serviceName", serviceName),
            ("packId", AcceptanceSeedCatalog.PackId),
            ("version", AcceptanceSeedCatalog.Version),
            ("fingerprint", AcceptanceSeedPackFingerprint.Value),
            ("status", AppliedStatus),
            ("counts", counts),
            ("appliedAt", AcceptanceSeedCatalog.ReferenceDate));
    }

    private static async Task<AcceptanceSeedReceipt?> ReadAsync(
        DbContext db,
        Guid tenantId,
        string serviceName,
        CancellationToken ct)
    {
        const string sql = """
            SELECT "Fingerprint", "Status"
            FROM "SeedPackReceipts"
            WHERE "TenantId" = @tenantId AND "ServiceName" = @serviceName AND "PackId" = @packId
            """;
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close)
            await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AttachTransaction(db, command);
            AddParameter(command, "tenantId", tenantId);
            AddParameter(command, "serviceName", serviceName);
            AddParameter(command, "packId", AcceptanceSeedCatalog.PackId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct)
                ? new AcceptanceSeedReceipt(reader.GetString(0), reader.GetString(1))
                : null;
        }
        finally
        {
            if (close)
                await connection.CloseAsync();
        }
    }

    private static async Task ExecuteAsync(
        DbContext db,
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close)
            await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AttachTransaction(db, command);
            foreach (var parameter in parameters)
                AddParameter(command, parameter.Name, parameter.Value);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (close)
                await connection.CloseAsync();
        }
    }

    private static void AttachTransaction(DbContext db, DbCommand command)
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is not null)
            command.Transaction = transaction.GetDbTransaction();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
