using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.Integration;
using Admin.Logistics.Infrastructure.Services;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-45 on PostgreSQL: what needs a real transaction, a real <c>xmin</c>, a real unique index, a real deadlock and real row security.
/// The job's proof runs as a role that is neither superuser nor owner, as <c>admin_app</c> does in production.
/// </summary>
[Collection("PgRabbit")]
public sealed class Wf045AssetApprovalPostgresTests(PgRabbitFixture fixture)
{
    private const string Reason = "لم يعد الطلب مطلوباً";

    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid TenantB = Guid.Parse("45000000-0000-4000-8000-00000000f00d");
    private static readonly Guid Requester = Guid.Parse("45000000-0000-4000-8000-000000000a01");
    private static readonly Guid RequesterB = Guid.Parse("45000000-0000-4000-8000-000000000a02");
    private static readonly SoftwareSignatureService Signer = new(Options.Create(new SignatureOptions()));
    private static readonly ExternalApprovalCaller Owner = new(true, ServiceClientIds.AdminLogistics, Requester.ToString("D"), "a-logistics-maker");

    // HR.Core.

    private static HRCoreDbContext HrDb(string connection)
    {
        var context = new Msg040FixedTenant(Tenant);
        return new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(connection).AddInterceptors(new TenantIdStampInterceptor(context)).Options,
            context);
    }

    private async Task<string> HrDatabaseAsync(string name)
    {
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf045_{name}_{Guid.NewGuid():N}");
        await using var db = HrDb(connection);
        await db.Database.MigrateAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        return connection;
    }

    private static WorkflowInstanceActionService Actions(HRCoreDbContext db, IWorkflowEngine? engine = null, IPublishEndpoint? publish = null) =>
        new(
            db,
            engine ?? new WorkflowEngine(),
            new WorkflowSignatureService(db, Signer, new Msg040FixedTenant(Tenant)),
            publish ?? Mock.Of<IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new EstablishmentChangeWorkflowHandler(db, null!));

    private static WorkflowActionActor Officer() => new(Guid.NewGuid(), "officer", HrManagementRoles.AdminLogistics);

    private static WorkflowActionActor Manager() => new(Guid.NewGuid(), "manager", HrManagementRoles.HrManagementApprover);

    private static WorkflowApprovalStartRequest Custody(Guid assignment) => new(
        "AssetAssignment",
        assignment,
        "ASSET-ASSIGN-APPROVAL",
        $"AssetAssignment:{assignment:D}",
        [Requester],
        null,
        "تسليم عهدة",
        "/ui/admin-logistics/Assets/Details/00000000-0000-0000-0000-000000000001",
        new Dictionary<string, string> { ["assetId"] = Guid.NewGuid().ToString("D"), ["employeeId"] = Guid.NewGuid().ToString("D") });

    private static async Task<Guid> AtLastStepAsync(string connection)
    {
        await using var db = HrDb(connection);
        var started = await new ExternalApprovalService(db, new WorkflowEngine()).StartAsync(Custody(Guid.NewGuid()), Owner);
        Assert.Equal(ExternalApprovalOutcome.Started, started.Outcome);
        var step1 = await Actions(db).ApproveAsync(started.Instance!.Id, Officer(), "ok", 1);
        Assert.True(step1.Success, step1.Error);
        return started.Instance.Id;
    }

    private ServiceProvider BusProvider(string connection)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ITenantContext>(new Msg040FixedTenant(Tenant));
        services.AddDbContext<HRCoreDbContext>(o => o.UseNpgsql(connection).AddInterceptors(new TenantIdStampInterceptor(new Msg040FixedTenant(Tenant))));
        services.AddServiceMassTransit(configuration, configureOutbox: x => x.AddPostgresOutbox<HRCoreDbContext>(), enableInboxFilter: false);
        return services.BuildServiceProvider();
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_last_approval_writes_the_v2_outcome_in_the_outbox_with_the_instance()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await HrDatabaseAsync("outbox");
        await using var provider = BusProvider(connection);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        var instance = await AtLastStepAsync(connection);
        var before = await ScalarAsync<long>(connection, """SELECT count(*) FROM "OutboxMessage" """);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            var result = await Actions(db, publish: scope.ServiceProvider.GetRequiredService<IPublishEndpoint>())
                .ApproveAsync(instance, Manager(), "ok", 2);
            Assert.True(result.Success, result.Error);
        }

        await bus.StopAsync();
        Assert.Equal("Completed", await ScalarAsync<string>(connection, $"""SELECT "Status" FROM "WorkflowInstances" WHERE "Id"='{instance:D}'"""));
        Assert.Equal(before + 1, await ScalarAsync<long>(connection, """SELECT count(*) FROM "OutboxMessage" """));
        var body = await ScalarAsync<string>(connection, """SELECT "Body" FROM "OutboxMessage" ORDER BY "SequenceNumber" DESC LIMIT 1""");
        Assert.Contains(instance.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"schemaVersion""\s*:\s*2", RegexOptions.IgnoreCase), body);
    }

    /// <summary>Holds both contenders after their checks and before their writes, so the race happens on every run.</summary>
    private sealed class RaceEngine(Barrier gate) : IWorkflowEngine
    {
        private readonly WorkflowEngine _engine = new();

        private void Wait() => Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(30)), "The other contender never arrived.");

        public WorkflowEngineResult Start(
            WorkflowDefinitionSnapshot definition, string entityType, Guid entityId, Guid? initiatedBy, string? initiatedByName, string? contextJson = null) =>
            _engine.Start(definition, entityType, entityId, initiatedBy, initiatedByName, contextJson);

        public WorkflowEngineResult ApproveStep(
            WorkflowInstanceState instance, WorkflowDefinitionSnapshot definition, Guid? actorId, string? actorName,
            string? comments = null, Dictionary<string, string>? context = null)
        {
            Wait();
            return _engine.ApproveStep(instance, definition, actorId, actorName, comments, context);
        }

        public WorkflowEngineResult Reject(WorkflowInstanceState instance, Guid? actorId, string? actorName, string? comments = null) =>
            _engine.Reject(instance, actorId, actorName, comments);

        public WorkflowEngineResult Withdraw(WorkflowInstanceState instance, Guid? actorId, string? actorName, string? reason)
        {
            Wait();
            return _engine.Withdraw(instance, actorId, actorName, reason);
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_withdrawal_racing_the_last_approval_ends_with_one_winner_and_the_message_for_the_other()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await HrDatabaseAsync("race");

        for (var round = 0; round < 5; round++)
        {
            var instance = await AtLastStepAsync(connection);
            using var gate = new Barrier(2);
            var engine = new RaceEngine(gate);

            await using var approveDb = HrDb(connection);
            await using var cancelDb = HrDb(connection);
            var approving = Task.Run(() => Actions(approveDb, engine).ApproveAsync(instance, Manager(), "ok", 2));
            var cancelling = Task.Run(() => new ExternalApprovalService(cancelDb, engine).CancelAsync(instance, Owner, Reason));

            var approved = await approving;
            var cancelled = await cancelling;

            Assert.True(approved.Success ^ cancelled.Outcome == ExternalApprovalOutcome.Withdrawn, $"approved {approved.Error}, cancelled {cancelled.Outcome}");
            var status = await ScalarAsync<string>(connection, $"""SELECT "Status" FROM "WorkflowInstances" WHERE "Id"='{instance:D}'""");
            if (approved.Success)
            {
                Assert.Equal("Completed", status);
                Assert.Equal(ExternalApprovalOutcome.Conflict, cancelled.Outcome);
                Assert.Equal(WorkflowConcurrency.ConflictMessage, cancelled.Error);
            }
            else
            {
                Assert.Equal("Cancelled", status);
                Assert.Equal(WorkflowConcurrency.ConflictMessage, approved.Error);
            }
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_cancel_that_a_deadlock_ends_gets_the_conflict_message_and_writes_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await HrDatabaseAsync("deadlock");
        var (first, second) = (await AtLastStepAsync(connection), await AtLastStepAsync(connection));

        // The other side holds the instance and waits for the entity this side holds, with a timer long enough to leave the choice to this side.
        await using var other = new NpgsqlConnection(connection);
        await other.OpenAsync();
        await using var otherTx = await other.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand($"""SET LOCAL deadlock_timeout='20s'; UPDATE "WorkflowInstances" SET "CompletedAt"=NULL WHERE "Id"='{first:D}';""", other, otherTx))
            await hold.ExecuteNonQueryAsync();

        await using var db = HrDb(connection);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL deadlock_timeout='100ms'");
        await db.Database.ExecuteSqlAsync($"""UPDATE "WorkflowInstances" SET "CompletedAt"=NULL WHERE "Id"={second}""");

        await using var wait = new NpgsqlCommand($"""UPDATE "WorkflowInstances" SET "CompletedAt"=NULL WHERE "Id"='{second:D}';""", other, otherTx);
        var waiting = wait.ExecuteNonQueryAsync();
        await WaitUntilBlockedAsync(connection);

        var tracked = await db.WorkflowInstances.SingleAsync(i => i.Id == first);
        tracked.Status = WorkflowStatus.Cancelled;

        var conflict = await WorkflowConcurrency.SaveOrConflictAsync(db, "cancel", default);

        Assert.Equal(WorkflowConcurrency.ConflictMessage, conflict);
        await tx.RollbackAsync();
        await waiting;
        await otherTx.RollbackAsync();
        Assert.Equal("InProgress", await ScalarAsync<string>(connection, $"""SELECT "Status" FROM "WorkflowInstances" WHERE "Id"='{first:D}'"""));
    }

    private static async Task WaitUntilBlockedAsync(string connection)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await ScalarAsync<long>(connection, "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock'") > 0)
                return;
            await Task.Delay(50);
        }

        Assert.Fail("The other side never waited for its lock.");
    }

    // Admin.Logistics.

    private static AdminLogisticsDbContext LogisticsDb(string connection, Guid? tenant = null) =>
        new(new DbContextOptionsBuilder<AdminLogisticsDbContext>().UseNpgsql(connection).Options, new Msg040FixedTenant(tenant ?? Tenant));

    private async Task<string> LogisticsDatabaseAsync(string name)
    {
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf045_{name}_{Guid.NewGuid():N}");
        await using var db = LogisticsDb(connection);
        await db.Database.MigrateAsync();
        return connection;
    }

    private static Asset NewAsset(Guid tenant) => new()
    {
        TenantId = tenant,
        Name = "حاسوب",
        TagNumber = $"T-{Guid.NewGuid():N}",
        Status = AssetStatus.InStorage,
        IsPrivileged = true
    };

    private static AssetAssignment NewAssignment(Asset asset, AssetAssignmentStatus status, Guid? instance = null, Guid? requester = null) => new()
    {
        TenantId = asset.TenantId,
        AssetId = asset.Id,
        EmployeeId = Guid.NewGuid(),
        AssignedAt = DateTime.UtcNow,
        Status = status,
        WorkflowInstanceId = instance,
        CreatedBy = requester ?? Requester,
        CreatedAt = DateTime.UtcNow.AddHours(-1)
    };

    private static AssetLossReport NewReport(Asset asset, AssetLossReportStatus status, Guid? instance = null) => new()
    {
        TenantId = asset.TenantId,
        AssetId = asset.Id,
        Status = status,
        WorkflowInstanceId = instance,
        CreatedBy = Requester
    };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_unique_indexes_hold_an_asset_for_a_request_that_is_still_starting()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await LogisticsDatabaseAsync("index");
        await using var db = LogisticsDb(connection);
        var asset = NewAsset(Tenant);
        db.Assets.Add(asset);
        db.AssetAssignments.Add(NewAssignment(asset, AssetAssignmentStatus.ApprovalStarting));
        db.AssetLossReports.Add(NewReport(asset, AssetLossReportStatus.ApprovalStarting));
        await db.SaveChangesAsync();

        db.AssetAssignments.Add(NewAssignment(asset, AssetAssignmentStatus.ApprovalStarting));
        var assignment = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(ConcurrencyExtensions.IsUniqueViolation(assignment));
        db.ChangeTracker.Clear();

        db.AssetLossReports.Add(NewReport(asset, AssetLossReportStatus.PendingApproval, Guid.NewGuid()));
        var report = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(ConcurrencyExtensions.IsUniqueViolation(report));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_withdrawn_assignment_frees_the_index_for_the_next_one()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await LogisticsDatabaseAsync("free");
        await using var db = LogisticsDb(connection);
        var asset = NewAsset(Tenant);
        var first = NewAssignment(asset, AssetAssignmentStatus.ApprovalStarting);
        db.Assets.Add(asset);
        db.AssetAssignments.Add(first);
        await db.SaveChangesAsync();

        AssetApprovalRules.Withdraw(first, Requester, Reason, DateTime.UtcNow);
        db.AssetAssignments.Add(NewAssignment(asset, AssetAssignmentStatus.ApprovalStarting));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.AssetAssignments.CountAsync());
    }

    /// <summary>Between HR.Core's answer and the save, a consumer applies the outcome: the withdrawal must lose.</summary>
    private sealed class ConsumerBetween(string connection) : IAssetApprovalStarter
    {
        public Task<AssetApprovalStartResult> StartAsync(AssetAssignment assignment, Asset asset, CancellationToken ct = default) =>
            throw new InvalidOperationException();

        public Task<AssetApprovalStartResult> StartAsync(AssetLossReport report, Asset asset, Guid? holderEmployeeId, CancellationToken ct = default) =>
            throw new InvalidOperationException();

        public async Task<AssetApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct = default)
        {
            await using var consumer = LogisticsDb(connection);
            var report = await consumer.AssetLossReports.SingleAsync(ct);
            AssetApprovalRules.ApplyRejected(report, "رفض سبق السحب");
            await consumer.SaveChangesAsync(ct);
            return new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Cancelled);
        }
    }

    private static AssetApprovalLoop Loop(AdminLogisticsDbContext db, IAssetApprovalStarter starter) =>
        new(db, starter, new NoEmployees(), new StaticOptions(new AssetApprovalOptions()), NullLogger<AssetApprovalLoop>.Instance);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_withdrawal_that_a_consumer_overtakes_on_xmin_changes_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await LogisticsDatabaseAsync("xmin");
        Guid reportId;
        await using (var seed = LogisticsDb(connection))
        {
            var asset = NewAsset(Tenant);
            var report = NewReport(asset, AssetLossReportStatus.PendingApproval, Guid.NewGuid());
            seed.Assets.Add(asset);
            seed.AssetLossReports.Add(report);
            await seed.SaveChangesAsync();
            reportId = report.Id;
        }

        await using var db = LogisticsDb(connection);
        var outcome = await Loop(db, new ConsumerBetween(connection)).WithdrawLossAsync(reportId, Requester, Reason);

        Assert.Equal(AssetWithdrawOutcome.NotWaiting, outcome);
        await using var read = LogisticsDb(connection);
        var stored = await read.AssetLossReports.AsNoTracking().SingleAsync();
        Assert.Equal(AssetLossReportStatus.Rejected, stored.Status);
        Assert.Null(stored.WithdrawnAt);
    }

    /// <summary>A fake HR.Core: one instance per key, like the real one, after <paramref name="failures"/> refused calls.</summary>
    private sealed class FakeHrCore(int failures) : HttpMessageHandler
    {
        private readonly Dictionary<string, Guid> _instances = [];
        private int _calls;

        public List<(string? Tenant, string? Acting, string Key)> Starts { get; } = [];

        public int Calls => _calls;

        public Guid InstanceFor(string key) => _instances[key];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) <= failures)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var key = body.RootElement.GetProperty("idempotencyKey").GetString()!;
            lock (Starts)
            {
                Starts.Add((Header(request, TenantIdMiddleware.HeaderName), Header(request, ForwardActingUserHandler.HeaderName), key));
                if (!_instances.ContainsKey(key))
                    _instances[key] = Guid.NewGuid();
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new WorkflowApprovalStartResponse(_instances[key], "InProgress", true)), Encoding.UTF8, "application/json")
            };
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }

    private sealed class HandlerClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://hr-core-web:8080/") };
    }

    /// <summary>As production wires it: the tenant session, the audit and the stamp, and the handler that forwards the tenant.</summary>
    private static ServiceProvider JobProvider(string connection, FakeHrCore hrCore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddTenantContext();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AdminLogisticsDbContext>((sp, o) => o.UseNpgsql(connection).AddInterceptors(
            sp.GetRequiredService<AuditSaveChangesInterceptor>(),
            sp.GetRequiredService<TenantIdStampInterceptor>(),
            sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
        services.AddSingleton<IHttpClientFactory>(sp => new HandlerClient(
            new ForwardTenantHandler(sp.GetRequiredService<IHttpContextAccessor>()) { InnerHandler = hrCore }));
        services.AddScoped<IAssetApprovalStarter, AssetApprovalStarter>();
        services.AddSingleton<IEmployeeLookup, NoEmployees>();
        services.AddSingleton<IOptionsMonitor<AssetApprovalOptions>>(new StaticOptions(new AssetApprovalOptions { Retry = { IntervalSeconds = 1 } }));
        services.AddScoped<AssetApprovalLoop>();
        services.AddSingleton<AssetApprovalStartRetryJob>();
        return services.BuildServiceProvider();
    }

    private static async Task<AssetAssignment> SeedStartingAsync(string connection, Guid tenant, Guid requester)
    {
        await using var db = LogisticsDb(connection, tenant);
        var asset = NewAsset(tenant);
        var assignment = NewAssignment(asset, AssetAssignmentStatus.ApprovalStarting, requester: requester);
        db.Assets.Add(asset);
        db.AssetAssignments.Add(assignment);
        db.AuditLogs.Add(new EntityAuditLog { TenantId = tenant, EntityType = nameof(AssetAssignment), EntityId = assignment.Id, Action = "Created" });
        await db.SaveChangesAsync();
        return assignment;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_failure_then_a_success_gives_one_instance_by_the_same_key()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await LogisticsDatabaseAsync("retry");
        var assignment = await SeedStartingAsync(connection, Tenant, Requester);
        var hrCore = new FakeHrCore(failures: 1);
        await using var provider = JobProvider(connection, hrCore);
        var job = provider.GetRequiredService<AssetApprovalStartRetryJob>();

        await job.RunOnceAsync();

        await using (var read = LogisticsDb(connection))
        {
            var waiting = await read.AssetAssignments.AsNoTracking().SingleAsync();
            Assert.Equal(AssetAssignmentStatus.ApprovalStarting, waiting.Status);
            Assert.Equal(1, waiting.ApprovalStartAttempts);
            Assert.NotNull(waiting.ApprovalStartLastError);
        }

        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await job.RunOnceAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await job.RunOnceAsync();

        await using var after = LogisticsDb(connection);
        var linked = await after.AssetAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(AssetAssignmentStatus.PendingApproval, linked.Status);
        var key = $"AssetAssignment:{assignment.Id:D}";
        Assert.Equal(hrCore.InstanceFor(key), linked.WorkflowInstanceId);
        Assert.Equal(key, Assert.Single(hrCore.Starts).Key);
    }

    /// <summary>As the controller saves it: as the app role, inside the tenant, with the real interceptors, so the audit row is the real one.</summary>
    private static async Task<AssetAssignment> SeedThroughTheAppRoleAsync(ServiceProvider provider, Guid tenant, Guid requester)
    {
        TenantIdFlow.Set(tenant);
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();
            var asset = NewAsset(tenant);
            var assignment = NewAssignment(asset, AssetAssignmentStatus.ApprovalStarting, requester: requester);
            db.Assets.Add(asset);
            db.AssetAssignments.Add(assignment);
            await db.SaveChangesAsync();
            return assignment;
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private async Task<(string Admin, string App)> ForcedRowSecurityAsync()
    {
        var admin = await LogisticsDatabaseAsync("rls");
        var script = await System.IO.File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "sql", "ten002c-harden-tenant-rls.sql"));
        await ExecuteAsync(admin, script.Replace(@"\set ON_ERROR_STOP on", string.Empty, StringComparison.Ordinal));
        var database = new NpgsqlConnectionStringBuilder(admin).Database!;
        return (admin, await fixture.Postgres.CreateAppConnectionAsync(database));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Under_forced_row_security_the_job_finds_both_tenants_and_starts_each_in_its_own()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, app) = await ForcedRowSecurityAsync();

        Assert.False(await ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.True(await ScalarAsync<bool>(admin, """SELECT relforcerowsecurity FROM pg_class WHERE oid = '"AssetAssignments"'::regclass"""));

        var hrCore = new FakeHrCore(failures: 0);
        await using var provider = JobProvider(app, hrCore);
        var inA = await SeedThroughTheAppRoleAsync(provider, Tenant, Requester);
        var inB = await SeedThroughTheAppRoleAsync(provider, TenantB, RequesterB);

        // A session with no tenant finds no request at all, and the tenants still: the audit interceptor wrote their rows.
        Assert.Equal(0L, await ScalarAsync<long>(app, """SELECT count(*) FROM "AssetAssignments" """));
        await using (var scope = provider.CreateAsyncScope())
        {
            var tenants = await AssetApprovalStartRetryJob.FindTenantsAsync(scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>());
            Assert.Equal(2, tenants.Count);
        }

        await provider.GetRequiredService<AssetApprovalStartRetryJob>().RunOnceAsync();

        Assert.Equal(2, hrCore.Starts.Count);
        Assert.Contains((Tenant.ToString("D"), Requester.ToString("D"), $"AssetAssignment:{inA.Id:D}"), hrCore.Starts);
        Assert.Contains((TenantB.ToString("D"), RequesterB.ToString("D"), $"AssetAssignment:{inB.Id:D}"), hrCore.Starts);
        Assert.Equal(2L, await ScalarAsync<long>(admin, """SELECT count(*) FROM "AssetAssignments" WHERE "Status" = 0 AND "WorkflowInstanceId" IS NOT NULL"""));
        Assert.Equal(0L, await ScalarAsync<long>(admin, """SELECT count(*) FROM "AssetAssignments" WHERE "Status" = 0 AND "WorkflowInstanceId" IS NULL"""));
    }

    private sealed class NoEmployees : IEmployeeLookup
    {
        public Task<string?> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> GetEmployeeDisplayNamesAsync(IEnumerable<Guid> employeeIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class StaticOptions(AssetApprovalOptions value) : IOptionsMonitor<AssetApprovalOptions>
    {
        public AssetApprovalOptions CurrentValue => value;

        public AssetApprovalOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AssetApprovalOptions, string?> listener) => null;
    }
}
