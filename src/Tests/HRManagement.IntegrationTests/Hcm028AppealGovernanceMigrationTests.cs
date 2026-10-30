using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-28 (H4): migration HCM028_AppealGovernance turns the history of the removed direct-reopen path (AppealNotes
/// without a RecruitmentAppeal row) into decided LegacyReopen appeals — insert-only, applications untouched.
/// Uses its own container because it migrates step by step. Skips without Docker.
/// </summary>
[Trait("Category", "Testcontainers")]
public sealed class Hcm028AppealGovernanceMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261022071057_HCM028_RankOverride";
    private PostgreSqlContainer? _postgres;
    private string? _unavailable;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            _unavailable = $"Docker/PostgreSQL unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [SkippableFact]
    public async Task Legacy_appeal_notes_become_decided_legacy_reopen_appeals_without_changing_applications()
    {
        Skip.If(_unavailable is not null, _unavailable);

        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString(), npgsql =>
                npgsql.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
            .Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var tenant = Guid.NewGuid();
        var legacy = Guid.NewGuid();      // reopened through the old path → gets a LegacyReopen appeal
        var withAppeal = Guid.NewGuid();  // already has a RecruitmentAppeal → untouched
        var noNotes = Guid.NewGuid();     // no AppealNotes → untouched
        var reviewedAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await InsertApplicationAsync(connection, legacy, tenant, "  ملاحظات استئناف قديمة  ", reviewedAt);
        await InsertApplicationAsync(connection, withAppeal, tenant, "ملاحظات مع استئناف", reviewedAt);
        await InsertApplicationAsync(connection, noNotes, tenant, null, reviewedAt);
        await ExecuteAsync(connection, $"""
            INSERT INTO "RecruitmentAppeals" ("Id","TenantId","RecruitmentApplicationId","Status","Reason","CreatedAt","UpdatedAt")
            VALUES ('{Guid.NewGuid()}','{tenant}','{withAppeal}',1,'استئناف قائم',now(),now());
            """);
        var applicationsBefore = await SnapshotApplicationsAsync(connection);

        await migrator.MigrateAsync();

        Assert.Equal(applicationsBefore, await SnapshotApplicationsAsync(connection));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT COUNT(*) FROM \"RecruitmentAppeals\";"));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT COUNT(*) FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{withAppeal}';"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT COUNT(*) FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{noNotes}';"));

        await using var read = new NpgsqlCommand(
            $"""
             SELECT "Status", "Outcome", "Reason", "Decision", "DecidedAt", "TenantId", "FiledBy"
             FROM "RecruitmentAppeals" WHERE "RecruitmentApplicationId"='{legacy}';
             """, connection);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal("LegacyReopen", reader.GetString(1));
        Assert.Equal("ملاحظات استئناف قديمة", reader.GetString(2));
        Assert.Equal("مسار قديم — إعادة فتح مباشرة", reader.GetString(3));
        Assert.Equal(reviewedAt, reader.GetDateTime(4).ToUniversalTime());
        Assert.Equal(tenant, reader.GetGuid(5));
        Assert.Equal("HrOnBehalf", reader.GetString(6));
        Assert.False(await reader.ReadAsync());
    }

    /// <summary>Raw SQL against the pre-HCM028_AppealGovernance schema: every NOT NULL column without a default gets a neutral value.</summary>
    private static async Task InsertApplicationAsync(NpgsqlConnection connection, Guid id, Guid tenant, string? appealNotes, DateTime reviewedAt)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = $"'{id}'",
            ["TenantId"] = $"'{tenant}'",
            ["ApplicantFullName"] = "'مرشح قديم'",
            ["AppliedPosition"] = "'محلل'",
            ["Status"] = "3",
            ["AppealStatus"] = "'None'",
            ["ReviewedAt"] = $"'{reviewedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}+00'",
            ["AppealNotes"] = appealNotes is null ? "NULL" : $"'{appealNotes}'"
        };
        await using (var columns = new NpgsqlCommand(
            """
            SELECT column_name, data_type FROM information_schema.columns
            WHERE table_name = 'RecruitmentApplications' AND is_nullable = 'NO' AND column_default IS NULL;
            """, connection))
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                if (values.ContainsKey(name))
                    continue;
                values[name] = reader.GetString(1) switch
                {
                    "uuid" => $"'{Guid.NewGuid()}'",
                    "boolean" => "false",
                    "integer" or "bigint" or "smallint" or "numeric" => "0",
                    "date" => "current_date",
                    var t when t.StartsWith("timestamp", StringComparison.Ordinal) => "now()",
                    _ => "''"
                };
            }
        }

        await ExecuteAsync(connection,
            $"INSERT INTO \"RecruitmentApplications\" ({string.Join(",", values.Keys.Select(k => $"\"{k}\""))}) VALUES ({string.Join(",", values.Values)});");
    }

    private static async Task<string> SnapshotApplicationsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT string_agg("Id"::text || '|' || "Status" || '|' || "AppealStatus" || '|' || COALESCE("AppealNotes", '-')
                   || '|' || COALESCE("ReviewedAt"::text, '-') || '|' || "UpdatedAt"::text, ',' ORDER BY "Id")
            FROM "RecruitmentApplications";
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
