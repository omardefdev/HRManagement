using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-39 (section 3), extending the PLAN-38 bootstrap tests on the same fixture: the keycloak role made by
/// deploy/bootstrap/ensure-keycloak-role.sql owns keycloak_db and cannot connect to another database; on an existing
/// server whose keycloak_db the administrator owns, the bootstrap refuses to go on without the switch, the documented
/// transfer (transfer-keycloak-ownership.sql) refuses while a session is connected, then moves every object of
/// keycloak_db and nothing elsewhere, and a second run changes nothing.
/// </summary>
public sealed class Ops039KeycloakRoleBootstrapTests : IClassFixture<Ops038BootstrapFixture>
{
    private const string Skip = "Set RUN_TESTCONTAINERS=true and start Docker to run this test.";
    private const string OwnersInKeycloakDb =
        "SELECT coalesce(string_agg(o || '=' || n, ',' ORDER BY o), '') FROM (SELECT pg_get_userbyid(c.relowner) o, count(*) n " +
        "FROM pg_class c JOIN pg_namespace s ON s.oid = c.relnamespace WHERE s.nspname NOT IN ('pg_catalog','information_schema') " +
        "AND s.nspname NOT LIKE 'pg_toast%' GROUP BY 1) x";

    private readonly Ops038BootstrapFixture _f;

    public Ops039KeycloakRoleBootstrapTests(Ops038BootstrapFixture fixture) => _f = fixture;

    private (string Name, string Value) KeycloakPassword => ("HRM_KEYCLOAK_PASSWORD", _f.Password("POSTGRES_KEYCLOAK_PASSWORD"));

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Keycloak_role_owns_keycloak_db_and_cannot_connect_to_hr_core_db()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        Assert.Equal(1L, await Ops038BootstrapFixture.ScalarAsync<long>(_f.AdminConnection("postgres"),
            "SELECT count(*) FROM pg_roles WHERE rolname = 'keycloak' AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole"));
        Assert.Equal("keycloak", await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("postgres"),
            "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'keycloak_db'"));

        // Keycloak creates its tables as keycloak (the database owner owns schema public through pg_database_owner).
        await using (var kc = new NpgsqlConnection(_f.Connection("keycloak_db", "keycloak", _f.Password("POSTGRES_KEYCLOAK_PASSWORD"))))
        {
            await kc.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS ops039_probe (id int); DROP TABLE ops039_probe;", kc);
            await cmd.ExecuteNonQueryAsync();
        }

        // The service databases (PUBLIC has no CONNECT on them, ensure-databases.sql). The maintenance database "postgres"
        // keeps PostgreSQL's PUBLIC grant, as for every PLAN-38 role (recorded as a residual in the PLAN-39 report).
        foreach (var db in new[] { "hr_core_db", "payroll_db", "archive_db", "gateway_audit_db" })
        {
            await using var other = new NpgsqlConnection(_f.Connection(db, "keycloak", _f.Password("POSTGRES_KEYCLOAK_PASSWORD")));
            var error = await Assert.ThrowsAsync<PostgresException>(() => other.OpenAsync());
            Assert.Equal("42501", error.SqlState);   // insufficient_privilege: permission denied for database
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Existing_server_transfer_moves_every_keycloak_db_object_and_nothing_elsewhere()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);

        // The picture of a server from before PLAN-39: Keycloak's objects belong to the administrator, and another
        // database holds an administrator-owned object that must stay as it is.
        await _f.PsqlAsync("keycloak_db", "-c \"ALTER DATABASE keycloak_db OWNER TO postgres; " +
            "CREATE TABLE IF NOT EXISTS realm (id varchar(36) PRIMARY KEY, name text); " +
            "CREATE TABLE IF NOT EXISTS client (id varchar(36) PRIMARY KEY, realm_id varchar(36) REFERENCES realm(id)); " +
            "CREATE SEQUENCE IF NOT EXISTS ops039_seq; CREATE OR REPLACE VIEW ops039_view AS SELECT id FROM realm; " +
            "CREATE OR REPLACE FUNCTION ops039_fn() RETURNS int LANGUAGE sql AS 'SELECT 1'; " +
            "DO \\$\\$ BEGIN CREATE TYPE ops039_kind AS ENUM ('a','b'); EXCEPTION WHEN duplicate_object THEN NULL; END \\$\\$;\"");
        await _f.PsqlAsync("archive_db", "-c \"CREATE TABLE IF NOT EXISTS ops039_control (id int)\"");
        try
        {
            var before = await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("keycloak_db"), OwnersInKeycloakDb);
            Assert.Contains("postgres=", before);

            // Without the switch the bootstrap stops: Keycloak would not be able to read tables it does not own.
            var (refusedExit, refused) = await _f.PsqlResultAsync("postgres", $"-v transfer=false -f {Ops038BootstrapFixture.BootstrapDir}/ensure-keycloak-role.sql", KeycloakPassword);
            Assert.NotEqual(0, refusedExit);
            Assert.Contains("run the ownership transfer", refused);
            Assert.Equal(before, await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("keycloak_db"), OwnersInKeycloakDb));

            // With the switch but a session still connected to keycloak_db (Keycloak running): refused, nothing changed.
            await using (var holder = new NpgsqlConnection(_f.AdminConnection("keycloak_db")))
            {
                await holder.OpenAsync();
                var (busyExit, busy) = await _f.PsqlResultAsync("postgres", $"-v transfer=true -f {Ops038BootstrapFixture.BootstrapDir}/ensure-keycloak-role.sql", KeycloakPassword);
                Assert.NotEqual(0, busyExit);
                Assert.Contains("another session is connected to keycloak_db", busy);
            }

            Assert.Equal(before, await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("keycloak_db"), OwnersInKeycloakDb));
            NpgsqlConnection.ClearAllPools();

            var (exit, output) = await _f.PsqlResultAsync("postgres", $"-v transfer=true -f {Ops038BootstrapFixture.BootstrapDir}/ensure-keycloak-role.sql", KeycloakPassword);
            Assert.True(exit == 0, output);
            Assert.Contains("[keycloak-transfer]", output);
            var after = await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("keycloak_db"), OwnersInKeycloakDb);
            Assert.StartsWith("keycloak=", after);
            Assert.DoesNotContain("postgres=", after);
            Assert.Equal("keycloak", await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("keycloak_db"),
                "SELECT pg_get_userbyid(proowner) FROM pg_proc WHERE proname = 'ops039_fn'"));
            Assert.Equal("keycloak", await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("keycloak_db"),
                "SELECT pg_get_userbyid(typowner) FROM pg_type WHERE typname = 'ops039_kind'"));
            Assert.Equal("keycloak", await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("postgres"),
                "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'keycloak_db'"));

            // Nothing outside keycloak_db moved, and the administrator is still the superuser it was.
            Assert.Equal("postgres", await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("archive_db"),
                "SELECT pg_get_userbyid(relowner) FROM pg_class WHERE relname = 'ops039_control'"));
            Assert.Equal(0L, await Ops038BootstrapFixture.ScalarAsync<long>(_f.AdminConnection("postgres"),
                "SELECT count(*) FROM pg_database WHERE datname <> 'keycloak_db' AND datdba = 'keycloak'::regrole"));
            Assert.True(await Ops038BootstrapFixture.ScalarAsync<bool>(_f.AdminConnection("postgres"),
                "SELECT rolsuper FROM pg_roles WHERE rolname = 'postgres'"));

            // Keycloak can now read and write what it owns.
            await using (var kc = new NpgsqlConnection(_f.Connection("keycloak_db", "keycloak", _f.Password("POSTGRES_KEYCLOAK_PASSWORD"))))
            {
                await kc.OpenAsync();
                await using var cmd = new NpgsqlCommand("INSERT INTO realm VALUES ('r1', 'x') ON CONFLICT DO NOTHING; SELECT count(*) FROM client;", kc);
                await cmd.ExecuteScalarAsync();
            }

            // Idempotent: a second transfer (and the plain step) change nothing.
            NpgsqlConnection.ClearAllPools();
            var (again, againOutput) = await _f.PsqlResultAsync("postgres", $"-v transfer=true -f {Ops038BootstrapFixture.BootstrapDir}/ensure-keycloak-role.sql", KeycloakPassword);
            Assert.True(again == 0, againOutput);
            Assert.Equal(after, await Ops038BootstrapFixture.ScalarAsync<string>(_f.AdminConnection("keycloak_db"), OwnersInKeycloakDb));
            var (plain, plainOutput) = await _f.PsqlResultAsync("postgres", $"-v transfer=false -f {Ops038BootstrapFixture.BootstrapDir}/ensure-keycloak-role.sql", KeycloakPassword);
            Assert.True(plain == 0, plainOutput);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await _f.PsqlResultAsync("keycloak_db", "-c \"DROP VIEW IF EXISTS ops039_view; DROP TABLE IF EXISTS client, realm; DROP SEQUENCE IF EXISTS ops039_seq; DROP FUNCTION IF EXISTS ops039_fn(); DROP TYPE IF EXISTS ops039_kind; ALTER DATABASE keycloak_db OWNER TO keycloak;\"");
            await _f.PsqlResultAsync("archive_db", "-c \"DROP TABLE IF EXISTS ops039_control\"");
        }
    }
}
