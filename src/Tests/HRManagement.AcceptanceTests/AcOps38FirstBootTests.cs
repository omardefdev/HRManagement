using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-38 (AC-OPS-38a..h) on the least-privilege stack <c>hrm-leastpriv</c> (docker-compose.acceptance-leastpriv.yml,
/// env file .env.leastpriv, started from empty volumes): hrm-bootstrap builds databases, roles, migrations and broker
/// users; every application connects as its *_app role and mq_* user; the delayed-message plugin works; durable messages
/// survive a broker recreate; secrets are generated without being printed; the guard and the mandatory seeds stop a start.
/// Scenarios that change roles, passwords or containers restore them and prove the restore. Secret values are read from
/// the lab env file into memory only and never written to evidence.
/// </summary>
[RequiredAcceptance]
[Collection(AcOps38Collection.Name)]
public class AcOps38FirstBootTests
{
    private const string Ac = "AC-OPS-38";
    private const string Project = "hrm-leastpriv";
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly string[] ComposeFiles =
    [
        "docker-compose.yml", "docker-compose.services.yml", "docker-compose.dev-profiles.yml",
        "docker-compose.full-stack-gateway.yml", "docker-compose.acceptance.yml", "docker-compose.acceptance-prodlike.yml",
        "docker-compose.acceptance-leastpriv.yml",
    ];

    private static readonly (string Service, string Database, string Role, string Port)[] Apps =
    [
        ("hr-gateway", "gateway_audit_db", "gateway_audit_app", "6200"),
        ("hr-core-web", "hr_core_db", "hr_core_app", "6210"),
        ("hr-contracts-web", "hr_contracts_db", "hr_contracts_app", "6220"),
        ("hr-health-web", "hr_health_db", "hr_health_app", "6230"),
        ("hr-movements-web", "hr_movements_db", "hr_movements_app", "6240"),
        ("hr-training-web", "hr_training_db", "hr_training_app", "6250"),
        ("payroll-web", "payroll_db", "payroll_app", "6260"),
        ("idcard-web", "idcard_db", "idcard_app", "6270"),
        ("records-archive-web", "archive_db", "archive_app", "6280"),
        ("admin-logistics-web", "admin_db", "admin_app", "6290"),
        ("it-infra-web", "it_infra_db", "it_infra_app", "6300"),
        ("dev-analytics-web", "analytics_db", "analytics_app", "6310"),
        ("compliance-web", "compliance_db", "compliance_app", "6320"),
        ("auth-directory-web", "auth_directory_db", "auth_directory_app", "6330"),
        ("file-storage-web", "file_storage_db", "file_storage_app", "6340"),
    ];

    private static string Root => AcceptanceHarness.RepoRoot;
    private static string EnvFile => Path.Combine(Root, ".env.leastpriv");

    // ------------------------------------------------------------------------------------------------ helpers

    private static Dictionary<string, string> LabEnv()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(EnvFile))
            return values;
        foreach (var line in File.ReadAllLines(EnvFile))
        {
            var m = Regex.Match(line, "^([A-Z][A-Z0-9_]*)=(.*)$");
            if (m.Success)
                values[m.Groups[1].Value] = m.Groups[2].Value.Trim('"');
        }
        return values;
    }

    private static void SetLabEnv(string key, string value)
    {
        var lines = File.ReadAllLines(EnvFile).Select(l => l.StartsWith(key + "=", StringComparison.Ordinal) ? $"{key}={value}" : l);
        File.WriteAllText(EnvFile, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string exe, string? stdin, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = Root,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        // The compose child must not see secret-looking variables of this shell (they would win over the env file).
        foreach (var k in psi.Environment.Keys.Where(k => Regex.IsMatch(k, "(SECRET|PASSWORD|_PASS|TOKEN)$")).ToList())
            psi.Environment.Remove(k);
        using var p = Process.Start(psi)!;
        if (stdin is not null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await o + await e);
    }

    private static Task<(int ExitCode, string Output)> ComposeAsync(params string[] args)
    {
        var all = new List<string> { "compose", "-p", Project, "--env-file", EnvFile };
        foreach (var f in ComposeFiles)
            all.AddRange(["-f", f]);
        all.AddRange(["--profile", "full-stack"]);
        all.AddRange(args);
        return RunAsync("docker", null, [.. all]);
    }

    private static async Task<string> ContainerAsync(string service)
    {
        var (_, output) = await RunAsync("docker", null, "ps", "-a", "-q",
            "--filter", $"label=com.docker.compose.project={Project}", "--filter", $"label=com.docker.compose.service={service}");
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }

    /// <summary>psql as the administrator inside the postgres container (peer access, no password on any command line).</summary>
    private static async Task<string> AdminSqlAsync(string database, string sql)
    {
        var pg = await ContainerAsync("postgresql");
        var (exit, output) = await RunAsync("docker", null, "exec", pg, "psql", "-X", "-v", "ON_ERROR_STOP=1",
            "-U", LabEnv()["POSTGRES_USER"], "-d", database, "-At", "-c", sql);
        Assert.True(exit == 0, $"admin psql on {database} failed: {output}");
        return output.Trim();
    }

    /// <summary>
    /// psql as a login role over TCP to the service name (the image trusts loopback connections, so 127.0.0.1 would
    /// accept any password); the password reaches psql through stdin, never a command line.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RoleSqlAsync(string role, string password, string database, string sql)
    {
        var pg = await ContainerAsync("postgresql");
        return await RunAsync("docker", password, "exec", "-i", "-e", "Q=" + sql, pg, "sh", "-c",
            $"PGPASSWORD=\"$(cat)\" psql -X -h postgresql -v ON_ERROR_STOP=1 -U {role} -d {database} -At -c \"$Q\"");
    }

    private static async Task<string> RabbitCtlAsync(params string[] args)
    {
        var mq = await ContainerAsync("rabbitmq");
        var (exit, output) = await RunAsync("docker", null, ["exec", mq, "rabbitmqctl", "-q", .. args]);
        Assert.True(exit == 0, output);
        return output.Trim();
    }

    private static HttpClient Management()
    {
        var env = LabEnv();
        var http = new HttpClient { BaseAddress = new Uri("http://localhost:15683/api/"), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{env["RABBITMQ_DEFAULT_USER"]}:{env["RABBITMQ_DEFAULT_PASS"]}")));
        return http;
    }

    private static async Task<(long Ready, long Acked)> QueueAsync(string queue)
    {
        using var http = Management();
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"queues/%2F/{queue}"));
        var ready = doc.RootElement.TryGetProperty("messages_ready", out var r) ? r.GetInt64() : 0;
        var acked = doc.RootElement.TryGetProperty("message_stats", out var s) && s.TryGetProperty("ack", out var a) ? a.GetInt64() : 0;
        return (ready, acked);
    }

    private static async Task<bool> HealthyAsync(string port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            return (await http.GetAsync($"http://localhost:{port}/health/ready")).IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilAsync(TimeSpan timeout, Func<Task<bool>> probe)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await probe())
                return true;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        return await probe();
    }

    private static async Task<(IBusControl Bus, ServiceProvider Provider)> BusAsync(string user, string password, string prefix)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = "localhost",
            ["RabbitMQ:Port"] = "6573",
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = user,
            ["RabbitMQ:Password"] = password,
            ["MassTransit:EndpointPrefix"] = prefix,
            ["MassTransit:RetryCount"] = "0",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddServiceMassTransit(config, enableInboxFilter: false);
        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
        return (bus, provider);
    }

    private static async Task PublishPayrollProcessedAsync(Guid runId)
    {
        var env = LabEnv();
        var (bus, provider) = await BusAsync("mq_payroll", env["RABBITMQ_PAYROLL_PASS"], "payroll-web");
        try
        {
            await bus.Publish<IPayrollProcessed>(new
            {
                PayrollRunId = runId,
                PeriodStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
                TotalNetPay = 1m,
                TenantId = (Guid?)Tenant,
            }, ctx => ctx.Headers.Set(TenantIdMiddleware.HeaderName, Tenant.ToString("D")));
        }
        finally
        {
            await bus.StopAsync();
            await provider.DisposeAsync();
        }
    }

    private static EvidenceRecorder Recorder(string key, string scenario)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, "platform-operator (lab)");
        return recorder;
    }

    private static async Task RequireStackAsync(EvidenceRecorder recorder)
    {
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, File.Exists(EnvFile) && !string.IsNullOrEmpty(await ContainerAsync("hrm-bootstrap")),
            "مكدس hrm-leastpriv غير قائم", "The hrm-leastpriv stack is not running (docker-compose.acceptance-leastpriv.yml with .env.leastpriv).");
    }

    private static async Task<long> PgClassTotalAsync()
    {
        long total = 0;
        foreach (var app in Apps)
            total += long.Parse(await AdminSqlAsync(app.Database, "SELECT count(*) FROM pg_class"), CultureInfo.InvariantCulture);
        return total;
    }

    private static async Task<(int ExitCode, string Logs)> RerunBootstrapAsync()
    {
        var (exit, output) = await ComposeAsync("up", "--no-deps", "--force-recreate", "--exit-code-from", "hrm-bootstrap", "hrm-bootstrap");
        return (exit, output);
    }

    // ------------------------------------------------------------------------------------------------ 38a

    [SkippableFact]
    public async Task Ac38a_empty_volumes_bootstrap_every_database_and_every_application_connects_least_privilege()
    {
        var r = Recorder("38a", "First boot from empty volumes: databases, migrations, *_app and mq_* connections");
        await RequireStackAsync(r);

        var boot = await ContainerAsync("hrm-bootstrap");
        var (_, state) = await RunAsync("docker", null, "inspect", "-f", "{{.State.Status}} {{.State.ExitCode}}", boot);
        Assert.Equal("exited 0", state.Trim());
        var (_, bootLogs) = await RunAsync("docker", null, "logs", boot);
        Assert.Contains("step 8: result", bootLogs);
        Assert.Equal(15, Regex.Matches(bootLogs, @"migrated by [a-z_]+_migrator").Count);

        var databases = await AdminSqlAsync("postgres",
            "SELECT count(*) FROM pg_database WHERE datname IN ('keycloak_db','gateway_audit_db','hr_core_db','hr_contracts_db','hr_health_db','hr_movements_db','hr_training_db','payroll_db','idcard_db','archive_db','admin_db','it_infra_db','analytics_db','compliance_db','auth_directory_db','file_storage_db')");
        Assert.Equal("16", databases);

        var checks = new List<object>();
        foreach (var app in Apps)
        {
            var history = long.Parse(await AdminSqlAsync(app.Database, "SELECT count(*) FROM \"__EFMigrationsHistory\""), CultureInfo.InvariantCulture);
            var owners = await AdminSqlAsync(app.Database,
                "SELECT COALESCE(string_agg(DISTINCT pg_get_userbyid(c.relowner), ','), '') FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','S')");
            var users = await AdminSqlAsync("postgres",
                $"SELECT COALESCE(string_agg(DISTINCT usename, ','), '') FROM pg_stat_activity WHERE datname='{app.Database}' AND backend_type='client backend'");
            var healthy = await WaitUntilAsync(TimeSpan.FromMinutes(3), () => HealthyAsync(app.Port));
            checks.Add(new { app.Service, app.Database, history, owners, connectedAs = users, healthy });
            Assert.True(history > 0, $"{app.Database}: no migration history");
            Assert.Equal(app.Role.Replace("_app", "_migrator", StringComparison.Ordinal), owners);
            Assert.True(healthy, $"{app.Service} is not ready");
            // Negative: no application connection with the administrator (only the role of the service, if any).
            Assert.True(users.Length == 0 || users == app.Role, $"{app.Database} has connections as {users}");
        }

        var connections = (await RabbitCtlAsync("list_connections", "user"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l != "user").ToList();
        Assert.NotEmpty(connections);
        Assert.All(connections, u => Assert.StartsWith("mq_", u, StringComparison.Ordinal));
        r.WriteScenarioDbChecks(new { databases = 16, applications = checks, brokerUsers = connections.Distinct().OrderBy(u => u, StringComparer.Ordinal) });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 38b

    [SkippableFact]
    public async Task Ac38b_second_bootstrap_changes_nothing_and_a_rotated_password_is_used_while_the_old_one_is_refused()
    {
        var r = Recorder("38b", "Second bootstrap run and a password rotation (POSTGRES_PAYROLL_APP_PASSWORD)");
        await RequireStackAsync(r);

        var before = await PgClassTotalAsync();
        var (exit, logs) = await RerunBootstrapAsync();
        Assert.True(exit == 0, logs);
        Assert.Contains("\"migrationsAppliedTotal\": 0", logs);
        Assert.Equal(15, Regex.Matches(logs, @"migrated by [a-z_]+_migrator: 0 migrations applied").Count);
        var after = await PgClassTotalAsync();
        Assert.Equal(before, after);

        const string key = "POSTGRES_PAYROLL_APP_PASSWORD";
        var old = LabEnv()[key];
        var rotated = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        bool restored = false;
        try
        {
            SetLabEnv(key, rotated);
            var (up, upOut) = await ComposeAsync("up", "-d", "payroll-web");
            Assert.True(up == 0, upOut);
            Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(4), () => HealthyAsync("6260")), "Payroll is not ready after the rotation");
            Assert.Equal("payroll_app", await AdminSqlAsync("postgres",
                "SELECT COALESCE(string_agg(DISTINCT usename, ','), '') FROM pg_stat_activity WHERE datname='payroll_db' AND backend_type='client backend'"));
            var (newExit, _) = await RoleSqlAsync("payroll_app", rotated, "payroll_db", "SELECT 1");
            Assert.Equal(0, newExit);
            var (oldExit, oldOut) = await RoleSqlAsync("payroll_app", old, "payroll_db", "SELECT 1");
            Assert.NotEqual(0, oldExit);
            Assert.Contains("password authentication failed", oldOut, StringComparison.Ordinal); // SQLSTATE 28P01
            Assert.DoesNotContain(old, oldOut, StringComparison.Ordinal);
        }
        finally
        {
            SetLabEnv(key, old);
            var (back, backOut) = await ComposeAsync("up", "-d", "payroll-web");
            restored = back == 0 && await WaitUntilAsync(TimeSpan.FromMinutes(4), () => HealthyAsync("6260"));
            Assert.True(restored, "restore of POSTGRES_PAYROLL_APP_PASSWORD failed: " + backOut);
        }
        var (restoredExit, _) = await RoleSqlAsync("payroll_app", old, "payroll_db", "SELECT 1");
        Assert.Equal(0, restoredExit);
        r.WriteScenarioDbChecks(new
        {
            pgClassBefore = before,
            pgClassAfter = after,
            secondRunMigrationsApplied = 0,
            rotation = "new password accepted, old refused (28P01)",
            restored
        });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 38c

    [SkippableFact]
    public async Task Ac38c_mq_payroll_publishes_and_is_consumed_while_roles_and_users_cannot_cross_their_boundary()
    {
        var r = Recorder("38c", "mq_payroll publishes IPayrollProcessed; hr_core_app has no DDL and no payroll_db; mq_payroll cannot declare an hr-core-web queue");
        await RequireStackAsync(r);
        var env = LabEnv();

        var (_, ackedBefore) = await QueueAsync("dev-analytics-web-payroll-processed");
        var runId = Guid.NewGuid();
        await PublishPayrollProcessedAsync(runId);
        Assert.True(await WaitUntilAsync(TimeSpan.FromSeconds(60), async () => (await QueueAsync("dev-analytics-web-payroll-processed")).Acked > ackedBefore),
            "Dev.Analytics (mq_dev_analytics) did not consume the message published by mq_payroll");

        var (ddlExit, ddlOut) = await RoleSqlAsync("hr_core_app", env["POSTGRES_HR_CORE_APP_PASSWORD"], "hr_core_db", "CREATE TABLE ops038_probe (id int)");
        Assert.NotEqual(0, ddlExit);
        Assert.Contains("permission denied for schema public", ddlOut, StringComparison.Ordinal); // 42501
        var (crossExit, crossOut) = await RoleSqlAsync("hr_core_app", env["POSTGRES_HR_CORE_APP_PASSWORD"], "payroll_db", "SELECT 1");
        Assert.NotEqual(0, crossExit);
        Assert.Contains("permission denied for database", crossOut, StringComparison.Ordinal);

        var factory = new ConnectionFactory { HostName = "localhost", Port = 6573, UserName = "mq_payroll", Password = env["RABBITMQ_PAYROLL_PASS"] };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var refused = await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            channel.QueueDeclareAsync("hr-core-web-x", durable: true, exclusive: false, autoDelete: false));
        Assert.Equal(403, refused.ShutdownReason!.ReplyCode);

        r.WriteScenarioDbChecks(new
        {
            publishedBy = "mq_payroll",
            consumedBy = "dev-analytics-web-payroll-processed (mq_dev_analytics)",
            hrCoreAppDdl = "refused (42501)",
            hrCoreAppToPayrollDb = "refused (42501)",
            mqPayrollDeclareHrCoreQueue = "ACCESS_REFUSED (403)",
        });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 38d

    [SkippableFact]
    public async Task Ac38d_employee_created_reaches_auth_directory_and_training_once_through_mq_users()
    {
        var r = Recorder("38d", "IEmployeeCreated with a tenant header, published twice with one MessageId (AC-16 tool) by mq_hr_core");
        await RequireStackAsync(r);
        var env = LabEnv();
        var employeeId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var (bus, provider) = await BusAsync("mq_hr_core", env["RABBITMQ_HR_CORE_PASS"], "hr-core-web");
        try
        {
            for (var i = 0; i < 2; i++)
            {
                await bus.Publish<IEmployeeCreated>(new
                {
                    EmployeeId = employeeId,
                    FullName = "AC-OPS-38 synthetic employee",
                    NationalIdHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(employeeId.ToByteArray())).ToLowerInvariant(),
                    EmploymentType = "Permanent",
                    TenantId = (Guid?)Tenant,
                    PrimaryOrgUnitId = (Guid?)null,
                    CorrelationId = messageId.ToString("D"),
                }, ctx =>
                {
                    ctx.MessageId = messageId;
                    ctx.Headers.Set(TenantIdMiddleware.HeaderName, Tenant.ToString("D"));
                });
            }
        }
        finally
        {
            await bus.StopAsync();
            await provider.DisposeAsync();
        }

        string authUsers = "", receipts = "", training = "";
        var ok = await WaitUntilAsync(TimeSpan.FromSeconds(90), async () =>
        {
            authUsers = await AdminSqlAsync("auth_directory_db", $"SELECT count(*) FROM \"AuthUsers\" WHERE \"EmployeeId\"='{employeeId}'");
            receipts = await AdminSqlAsync("auth_directory_db", $"SELECT count(*) FROM \"IntegrationMessageReceipts\" WHERE \"MessageId\"='{messageId}'");
            training = await AdminSqlAsync("hr_training_db", $"SELECT count(*) FROM \"EmployeeOrgUnitSnapshots\" WHERE \"EmployeeId\"='{employeeId}'");
            return authUsers == "1" && receipts == "1" && training == "1";
        });
        await Task.Delay(TimeSpan.FromSeconds(5)); // the duplicate had its chance to make a second effect
        authUsers = await AdminSqlAsync("auth_directory_db", $"SELECT count(*) FROM \"AuthUsers\" WHERE \"EmployeeId\"='{employeeId}'");
        r.WriteScenarioDbChecks(new { messageId, employeeId, authUsers, inboxReceipts = receipts, trainingSnapshots = training });
        Assert.True(ok, $"effects: AuthUsers={authUsers}, receipts={receipts}, training snapshots={training}");
        Assert.Equal("1", authUsers);
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 38e

    [SkippableFact]
    public async Task Ac38e_delayed_message_plugin_is_enabled_and_saga_timeouts_declare_their_delayed_exchange()
    {
        var r = Recorder("38e", "rabbitmq_delayed_message_exchange enabled; delayed exchanges declared by the sagas without error");
        await RequireStackAsync(r);

        var mq = await ContainerAsync("rabbitmq");
        var (_, plugins) = await RunAsync("docker", null, "exec", mq, "rabbitmq-plugins", "list", "-e", "rabbitmq_delayed_message_exchange");
        Assert.Matches(@"\[E\*?\]\s+rabbitmq_delayed_message_exchange\s+3\.13\.0", plugins);

        // The 10-second scheduled message through the delayed exchange with a least-privilege user is the Testcontainers
        // test Ops038RabbitLeastPrivilegeTests (same image); here: no x-delayed-message error in the running broker.
        var (_, brokerLog) = await RunAsync("docker", null, "logs", mq);
        Assert.DoesNotMatch("(?i)x-delayed-message.*(error|unknown exchange type)|unknown exchange type 'x-delayed-message'", brokerLog);
        var (_, contractsLog) = await RunAsync("docker", null, "logs", await ContainerAsync("hr-contracts-web"));
        Assert.DoesNotContain("x-delayed-message", contractsLog, StringComparison.OrdinalIgnoreCase);
        r.WriteScenarioDbChecks(new
        {
            plugin = "rabbitmq_delayed_message_exchange 3.13.0 enabled",
            delayedErrors = 0,
            scheduledMessageTest = "Ops038RabbitLeastPrivilegeTests.A_message_scheduled_ten_seconds_ahead_through_the_delayed_exchange_arrives_after_the_delay"
        });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 38f

    [SkippableFact]
    public async Task Ac38f_a_durable_message_survives_a_rabbitmq_recreate_and_is_consumed_afterwards()
    {
        var r = Recorder("38f", "Durable message waiting in a quorum queue; docker compose up -d --force-recreate rabbitmq; consumed afterwards");
        await RequireStackAsync(r);
        const string queue = "dev-analytics-web-payroll-processed";
        var (_, rabbitBefore) = await RunAsync("docker", null, "inspect", "-f", "{{.Id}}", await ContainerAsync("rabbitmq"));
        async Task<SortedSet<string>> BrokerUsersAsync() => new(
            (await RabbitCtlAsync("list_connections", "user")).Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("mq_", StringComparison.Ordinal)),
            StringComparer.Ordinal);
        // The users holding consumers: their buses reconnect by themselves. A publish-only bus (File.Storage) connects on
        // its first publish and again on the next one after a broker restart, so it is not expected back on its own.
        async Task<SortedSet<string>> ConsumingUsersAsync() => new(
            (await RabbitCtlAsync("list_channels", "user", "consumer_count")).Split('\n')
                .Select(l => l.Trim().Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 2 && p[0].StartsWith("mq_", StringComparison.Ordinal) && int.TryParse(p[1], out var n) && n > 0)
                .Select(p => p[0]),
            StringComparer.Ordinal);
        var usersBefore = await ConsumingUsersAsync();
        Assert.True(usersBefore.Count >= 10, $"only {usersBefore.Count} consuming mq_* users before the recreate");

        Assert.Equal(0, (await ComposeAsync("stop", "dev-analytics-web")).ExitCode);
        try
        {
            await PublishPayrollProcessedAsync(Guid.NewGuid());
            Assert.True(await WaitUntilAsync(TimeSpan.FromSeconds(30), async () => (await QueueAsync(queue)).Ready >= 1), "the message is not waiting");

            var (recreate, recreateOut) = await ComposeAsync("up", "-d", "--force-recreate", "--no-deps", "--wait", "rabbitmq");
            Assert.True(recreate == 0, recreateOut);
            var (_, rabbitAfter) = await RunAsync("docker", null, "inspect", "-f", "{{.Id}}", await ContainerAsync("rabbitmq"));
            Assert.NotEqual(rabbitBefore.Trim(), rabbitAfter.Trim());
            Assert.True(await WaitUntilAsync(TimeSpan.FromSeconds(60), async () => (await QueueAsync(queue)).Ready >= 1),
                "the durable message was lost with the broker container");
        }
        finally
        {
            Assert.Equal(0, (await ComposeAsync("start", "dev-analytics-web")).ExitCode);
        }

        Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(3), async () => (await QueueAsync(queue)).Ready == 0),
            "the message was not consumed after the consumer came back");
        // Every user that held consumers reconnects to the recreated broker with its mq_* user (publish-only buses
        // reconnect on their next publish).
        var usersAfter = new SortedSet<string>(StringComparer.Ordinal);
        Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(2), async () => (usersAfter = await BrokerUsersAsync()).IsSupersetOf(usersBefore)),
            $"not reconnected: {string.Join(',', usersBefore.Except(usersAfter))}");
        r.WriteScenarioDbChecks(new { queue, survivedRecreate = true, consumedAfterwards = true, reconnectedUsers = usersAfter });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 38g

    [SkippableFact]
    public async Task Ac38g_generated_secrets_render_the_production_chain_and_the_guard_refuses_missing_or_placeholder_values()
    {
        var r = Recorder("38g", "init-secrets on an empty .env renders the single-host chain; guard negatives");
        await RequireStackAsync(r);

        var dir = Path.Combine(Path.GetTempPath(), "ops038-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var env = Path.Combine(dir, "server.env");
        try
        {
            var (initExit, initOut) = await RunAsync("powershell", null, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(Root, "scripts", "vps", "init-secrets.ps1"), "-EnvFile", env,
                "-Set", "APP_DOMAIN=hr.ops038.test,SSO_DOMAIN=sso.ops038.test,HRM_IMAGE_REPO=registry.ops038.test/hrmanagement");
            Assert.True(initExit == 0, initOut);
            // IMAGE_TAG may stay empty in .env (remote-deploy.sh exports the release tag); give it a tag as a deploy does.
            File.WriteAllText(env, Regex.Replace(File.ReadAllText(env), "(?m)^IMAGE_TAG=$", "IMAGE_TAG=0123456789ab"));
            var generated = File.ReadAllLines(env).Select(l => l.Split('=', 2)).Where(p => p.Length == 2 && p[1].Length >= 32).Select(p => p[1]).ToList();
            Assert.True(generated.Count >= 60, $"only {generated.Count} generated secrets");
            Assert.All(generated, v => Assert.DoesNotContain(v, initOut, StringComparison.Ordinal));

            var config = new List<string> { "compose", "--env-file", env };
            foreach (var f in new[] { "docker-compose.yml", "docker-compose.services.yml", "docker-compose.images.yml", "docker-compose.production.yml", "docker-compose.vps.yml", "docker-compose.vps-demo.yml" })
                config.AddRange(["-f", f]);
            config.AddRange(["--profile", "full-stack", "config", "--quiet"]);
            var (configExit, configOut) = await RunAsync("docker", null, [.. config]);
            Assert.True(configExit == 0, configOut);
            Assert.All(generated, v => Assert.DoesNotContain(v, configOut, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        // Negatives: one-off containers of the least-privilege image (the guard is enforced there).
        var (proofExit, proofOut) = await ComposeAsync("run", "--rm", "--no-deps", "-T", "-e", "GatewayCallerProof__Secret=", "hr-health-web");
        Assert.NotEqual(0, proofExit);
        Assert.Contains("GatewayCallerProof:Secret", proofOut, StringComparison.Ordinal);
        var placeholder = "Host=postgresql;Port=5432;Database=hr_health_db;Username=hr_health_app;Password=USE_ENV_OR_USER_SECRETS";
        var (connExit, connOut) = await ComposeAsync("run", "--rm", "--no-deps", "-T", "-e", "ConnectionStrings__HealthDb=" + placeholder, "hr-health-web");
        Assert.NotEqual(0, connExit);
        Assert.Contains("ConnectionStrings:HealthDb", connOut, StringComparison.Ordinal);
        Assert.DoesNotContain("USE_ENV_OR_USER_SECRETS", connOut, StringComparison.Ordinal);
        r.WriteScenarioDbChecks(new { initSecrets = "empty env filled, names printed only, compose config OK", guardMissingProof = "refused", guardPlaceholderConnection = "refused, key named, value not printed" });
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 38h

    [SkippableFact]
    public async Task Ac38h_mandatory_seeds_complete_and_a_failed_one_stops_hr_core_naming_the_seed()
    {
        var r = Recorder("38h", "Mandatory national seed as hr_core_app; INSERT on WorkflowDefinitions revoked -> HR.Core stops naming the seed");
        await RequireStackAsync(r);
        Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(2), () => HealthyAsync("6210")), "HR.Core is not ready");
        Assert.Equal("1", await AdminSqlAsync("hr_core_db", "SELECT count(*) FROM \"SeedPackApplications\""));
        const string code = "RECRUITMENT-RESULTS-APPROVAL";
        Assert.Equal("1", await AdminSqlAsync("hr_core_db", $"SELECT count(*) FROM \"WorkflowDefinitions\" WHERE \"Code\"='{code}'"));
        var contractType = await AdminSqlAsync("hr_contracts_db", "SELECT count(*) FROM \"ContractTypes\" WHERE \"Code\"='DEFAULT'");
        Assert.Equal("1", contractType);

        var hrCore = await ContainerAsync("hr-core-web");
        string failureLog = "";
        try
        {
            await AdminSqlAsync("hr_core_db", $"DELETE FROM \"WorkflowDefinitions\" WHERE \"Code\"='{code}'");
            await AdminSqlAsync("hr_core_db", "REVOKE INSERT ON \"WorkflowDefinitions\" FROM hr_core_app");
            var since = DateTime.UtcNow.AddSeconds(-1).ToString("o", CultureInfo.InvariantCulture);
            Assert.Equal(0, (await ComposeAsync("restart", "hr-core-web")).ExitCode);
            var stopped = await WaitUntilAsync(TimeSpan.FromMinutes(3), async () =>
            {
                var (_, logs) = await RunAsync("docker", null, "logs", "--since", since, hrCore);
                failureLog = logs;
                return logs.Contains("MandatorySeedFailedException", StringComparison.Ordinal) || logs.Contains("Mandatory seed", StringComparison.Ordinal);
            });
            Assert.True(stopped, "HR.Core did not report the failed mandatory seed");
            Assert.Contains("national-mandatory", failureLog, StringComparison.Ordinal);
            Assert.Contains("42501", failureLog, StringComparison.Ordinal);
            Assert.False(await HealthyAsync("6210"), "HR.Core answered ready although its mandatory seed failed");
            // No row value (the definition's name) in the failure.
            Assert.DoesNotContain("اعتماد نتائج مسابقة", failureLog, StringComparison.Ordinal);
        }
        finally
        {
            await AdminSqlAsync("hr_core_db", "GRANT INSERT ON \"WorkflowDefinitions\" TO hr_core_app");
            await ComposeAsync("restart", "hr-core-web");
        }

        Assert.True(await WaitUntilAsync(TimeSpan.FromMinutes(4), () => HealthyAsync("6210")), "HR.Core did not come back after the restore");
        Assert.Equal("1", await AdminSqlAsync("hr_core_db", $"SELECT count(*) FROM \"WorkflowDefinitions\" WHERE \"Code\"='{code}'"));
        r.WriteScenarioDbChecks(new
        {
            seedPackApplied = true,
            defaultContractType = contractType,
            negative = "INSERT on WorkflowDefinitions revoked: HR.Core stopped with national-mandatory / SQLSTATE 42501",
            restored = "grant restored, definition re-seeded, HR.Core ready",
        });
        r.WriteResult("Pass");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcOps38Collection
{
    public const string Name = "AC-OPS-38 least-privilege stack";
}
