using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// FUN-029 (E1): migration FUN029_ContractOfferTerms fills ServiceTransactions.RecruitmentApplicationId from the notes
/// marker written by the hire handler — only the new column, only where it is empty. Own container (step-wise
/// migration). Skips without Docker.
/// </summary>
[Trait("Category", "Testcontainers")]
public sealed class Fun029LineageBackfillTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260903184404_SYR009_SeparationRegimeBinding";
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
    public async Task Backfill_fills_the_lineage_column_from_notes_and_touches_nothing_else()
    {
        Skip.If(_unavailable is not null, _unavailable);

        await using var db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString(), npgsql =>
                npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name))
            .Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var tenant = Guid.NewGuid();
        var withMarker = Guid.NewGuid();
        var upperMarker = Guid.NewGuid();
        var noMarker = Guid.NewGuid();
        var badGuid = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var appIdUpper = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await InsertAsync(connection, withMarker, tenant, $"فاطمة أحمد — مصدر توظيف: PERMANENT | recruitmentApplicationId:{appId:D}");
        await InsertAsync(connection, upperMarker, tenant, $"مصدر توظيف: PERMANENT | RECRUITMENTAPPLICATIONID:{appIdUpper.ToString("D").ToUpperInvariant()}");
        await InsertAsync(connection, noMarker, tenant, "مصدر توظيف: PERMANENT");
        await InsertAsync(connection, badGuid, tenant, "recruitmentApplicationId:not-a-guid");
        var notesBefore = await NotesAsync(connection);

        await migrator.MigrateAsync();

        Assert.Equal(appId, await LineageAsync(connection, withMarker));
        Assert.Equal(appIdUpper, await LineageAsync(connection, upperMarker));
        Assert.Null(await LineageAsync(connection, noMarker));
        Assert.Null(await LineageAsync(connection, badGuid));
        Assert.Equal(notesBefore, await NotesAsync(connection));
    }

    private static async Task InsertAsync(NpgsqlConnection connection, Guid id, Guid tenant, string notes)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "ServiceTransactions" ("Id","TenantId","EmployeeId","TransactionType","State","Notes","CreatedAt","UpdatedAt")
            VALUES (@id, @tenant, @employee, 'Appointment', 'InProcess', @notes, now(), now());
            """, connection);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("employee", Guid.NewGuid());
        cmd.Parameters.AddWithValue("notes", notes);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> LineageAsync(NpgsqlConnection connection, Guid id)
    {
        await using var cmd = new NpgsqlCommand(
            """SELECT "RecruitmentApplicationId" FROM "ServiceTransactions" WHERE "Id" = @id;""", connection);
        cmd.Parameters.AddWithValue("id", id);
        var value = await cmd.ExecuteScalarAsync();
        return value is Guid g ? g : null;
    }

    private static async Task<string> NotesAsync(NpgsqlConnection connection)
    {
        await using var cmd = new NpgsqlCommand(
            """SELECT string_agg("Id"::text || '=' || coalesce("Notes", ''), ';' ORDER BY "Id") FROM "ServiceTransactions";""",
            connection);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
