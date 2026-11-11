using System.Diagnostics;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// The server of a collection has 100 connection slots, and every test takes a database of its own, so a pool of its own.
/// PLAN-49 found the shared server full of the idle connections of tests that had finished: the RLS proofs, which take
/// eighteen databases, got 53300 (PR 47). The fixture's connection strings now close an idle connection after a few
/// seconds; this proves it on the server itself, for the owner's pool and for the app role's.
/// </summary>
[Collection("PostgreSql")]
public sealed class PostgreSqlFixtureIdleConnectionTests(PostgreSqlFixture fixture)
{
    /// <summary>Far above the few seconds the pools need, and far below Npgsql's default of five minutes.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_connections_a_test_gave_back_leave_the_shared_server()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var database = TestDatabaseNames.Unique("idle_");
        var owner = await fixture.CreateDatabaseAsync(database);
        var app = await fixture.CreateAppConnectionAsync(database);

        // What a test does: it uses one connection of the owner's pool and one of the app role's, and gives them back.
        await using (var asOwner = new NpgsqlConnection(owner))
        await using (var asApp = new NpgsqlConnection(app))
        {
            await asOwner.OpenAsync();
            await asApp.OpenAsync();

            // The server shows both while the test holds them: the count below reads what it should read.
            Assert.Equal(2, await ConnectionsAsync(database));
        }

        var waited = Stopwatch.StartNew();
        long left;
        while ((left = await ConnectionsAsync(database)) > 0 && waited.Elapsed < Limit)
            await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.True(
            left == 0,
            $"{left} idle connection(s) to {database} are still open {waited.Elapsed.TotalSeconds:F0} s after the test gave them back. " +
            $"The connection strings of {nameof(PostgreSqlFixture)} must close idle connections " +
            $"({nameof(PostgreSqlFixture.IdleConnectionLifetimeSeconds)}): the server has 100 slots for every test of the collection.");
    }

    /// <summary>Counted from the server's default database, so the count never holds the connection that counts.</summary>
    private async Task<long> ConnectionsAsync(string database)
    {
        await using var observer = new NpgsqlConnection(fixture.ConnectionString);
        await observer.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = @database", observer);
        count.Parameters.AddWithValue("database", database);
        return (long)(await count.ExecuteScalarAsync())!;
    }
}
