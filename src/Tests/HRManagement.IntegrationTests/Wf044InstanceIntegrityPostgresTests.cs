using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-44 on PostgreSQL: what needs a real <c>xmin</c>, a real unique index, a real transaction and real row security. A barrier in the engine
/// holds both contenders until each has passed its checks, so every race here happens on every run.
/// </summary>
[Collection("PgRabbit")]
public sealed class Wf044InstanceIntegrityPostgresTests(PgRabbitFixture fixture)
{
    private const string RequisitionCode = "RECRUITMENT-REQUISITION-APPROVAL";
    private const string PreviousMigration = "20261106111135_WF043_ExternalStart";
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid OtherTenant = Guid.Parse("44000000-0000-4000-8000-00000000f00d");
    private static readonly Guid Creator = Guid.Parse("44000000-0000-4000-8000-000000000a01");
    private static readonly Guid Submitter = Guid.Parse("44000000-0000-4000-8000-000000000a02");
    private static readonly Guid Requester = Guid.Parse("44000000-0000-4000-8000-000000000a03");
    private static readonly SoftwareSignatureService Signer = new(Options.Create(new SignatureOptions()));
    private static readonly ExternalApprovalCaller Owner = new(true, ServiceClientIds.AdminLogistics, Requester.ToString("D"), "a-logistics-maker");

    private const string Decided = """[{"Level":1,"StepName":"s","Status":1,"ActionAt":"2026-11-01T10:00:00Z"}]""";

    /// <summary>Holds every contender at the engine, after the reads and the checks and before the writes.</summary>
    private sealed class BarrierEngine(Barrier gate) : IWorkflowEngine
    {
        private readonly WorkflowEngine _engine = new();

        private void Wait()
        {
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("A contender never reached the barrier.");
        }

        public WorkflowEngineResult Start(
            WorkflowDefinitionSnapshot definition, string entityType, Guid entityId, Guid? initiatedBy, string? initiatedByName, string? contextJson = null)
        {
            Wait();
            return _engine.Start(definition, entityType, entityId, initiatedBy, initiatedByName, contextJson);
        }

        public WorkflowEngineResult ApproveStep(
            WorkflowInstanceState instance, WorkflowDefinitionSnapshot definition, Guid? actorId, string? actorName,
            string? comments = null, Dictionary<string, string>? context = null)
        {
            Wait();
            return _engine.ApproveStep(instance, definition, actorId, actorName, comments, context);
        }

        public WorkflowEngineResult Reject(WorkflowInstanceState instance, Guid? actorId, string? actorName, string? comments = null)
        {
            Wait();
            return _engine.Reject(instance, actorId, actorName, comments);
        }

        public WorkflowEngineResult Withdraw(WorkflowInstanceState instance, Guid? actorId, string? actorName, string? reason) =>
            _engine.Withdraw(instance, actorId, actorName, reason);
    }

    /// <summary>With the stamp production registers: the starter leaves the tenant of a new instance to it.</summary>
    private static HRCoreDbContext Db(string connection, Guid? tenant = null)
    {
        var context = new Msg040FixedTenant(tenant ?? Tenant);
        return new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseNpgsql(connection)
                .AddInterceptors(new TenantIdStampInterceptor(context))
                .Options,
            context);
    }

    private async Task<string> DatabaseAsync(string name)
    {
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf044_{name}_{Guid.NewGuid():N}");
        await using var db = Db(connection);
        await db.Database.MigrateAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        return connection;
    }

    private static WorkflowInstanceActionService Actions(
        HRCoreDbContext db, IWorkflowEngine? engine = null, IPublishEndpoint? publish = null, EstablishmentChangeWorkflowHandler? establishment = null) =>
        new(
            db,
            engine ?? new WorkflowEngine(),
            new WorkflowSignatureService(db, Signer, new Msg040FixedTenant(Tenant)),
            publish ?? Mock.Of<IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            establishment ?? new EstablishmentChangeWorkflowHandler(db, null!));

    private static WorkflowActionActor HrCore() => new(Guid.NewGuid(), "hr", HrManagementRoles.HrCore);

    private static WorkflowActionActor Approver() => new(Guid.NewGuid(), "approver", HrManagementRoles.HrManagementApprover);

    private static async Task<(Guid RequisitionId, Guid InstanceId)> SubmittedRequisitionAsync(string connection)
    {
        await using var db = Db(connection);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, Tenant);
        var requisition = Hcm026PostgresFixture.NewRequisition(Tenant, position.Id, RequisitionStatus.PendingApproval);
        requisition.CreatedBy = Creator;
        db.RecruitmentRequisitions.Add(requisition);
        await db.SaveChangesAsync();
        var id = await new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance)
            .StartByWorkflowCodeAsync(RequisitionCode, "RecruitmentRequisition", requisition.Id, Tenant, Creator, "creator");
        requisition.WorkflowInstanceId = id;
        await db.SaveChangesAsync();
        return (requisition.Id, id!.Value);
    }

    private static async Task<int> SignaturesAsync(string connection)
    {
        await using var db = Db(connection);
        return await db.SignatureRecords.IgnoreQueryFilters().CountAsync();
    }

    private static async Task<WorkflowInstance> StoredAsync(string connection, Guid id)
    {
        await using var db = Db(connection);
        return await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    private static List<WorkflowStepHistoryEntry> History(WorkflowInstance instance) =>
        JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson)!;

    // ---------------------------------------------------------------- xmin

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_approvals_at_once_end_with_one_decision_one_signature_and_the_conflict_message()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("xmin");
        var (requisitionId, instanceId) = await SubmittedRequisitionAsync(connection);
        using var gate = new Barrier(2);
        await using var first = Db(connection);
        await using var second = Db(connection);

        var results = await Task.WhenAll(
            Task.Run(() => Actions(first, new BarrierEngine(gate)).ApproveAsync(instanceId, HrCore(), "one", 1)),
            Task.Run(() => Actions(second, new BarrierEngine(gate)).ApproveAsync(instanceId, HrCore(), "two", 1)));

        Assert.Single(results, r => r.Success);
        Assert.Equal(WorkflowConcurrency.ConflictMessage, Assert.Single(results, r => !r.Success).Error);
        var stored = await StoredAsync(connection, instanceId);
        Assert.Equal(2, stored.CurrentStepLevel);
        Assert.Single(History(stored));
        Assert.Equal(1, await SignaturesAsync(connection));
        Assert.NotEqual(Guid.Empty, requisitionId);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task An_approval_and_a_rejection_at_once_decide_once()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("xmin2");
        var (requisitionId, instanceId) = await SubmittedRequisitionAsync(connection);
        using var gate = new Barrier(2);
        await using var first = Db(connection);
        await using var second = Db(connection);

        var results = await Task.WhenAll(
            Task.Run(() => Actions(first, new BarrierEngine(gate)).ApproveAsync(instanceId, HrCore(), "yes", 1)),
            Task.Run(() => Actions(second, new BarrierEngine(gate)).RejectAsync(instanceId, HrCore(), "no")));

        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => r.IsConflict);
        var stored = await StoredAsync(connection, instanceId);
        Assert.Single(History(stored));
        Assert.Equal(1, await SignaturesAsync(connection));
        await using var read = Db(connection);
        var requisition = await read.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == requisitionId);
        Assert.Equal(stored.Status == WorkflowStatus.Rejected ? RequisitionStatus.Draft : RequisitionStatus.PendingApproval, requisition.Status);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_withdrawal_on_a_row_a_decision_changed_is_a_conflict_and_writes_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("cancel");
        await using var stale = Db(connection);
        var started = await new ExternalApprovalService(stale, new WorkflowEngine()).StartAsync(Start(Guid.NewGuid(), "k-cancel"), Owner);
        Assert.True(started.Outcome == ExternalApprovalOutcome.Started, started.Error);
        var instanceId = started.Instance!.Id;

        await using (var other = Db(connection))
        {
            var approved = await Actions(other).ApproveAsync(instanceId, HrCore(), "ok", 1);
            Assert.True(approved.Success, approved.Error);
        }

        var withdrawn = await new ExternalApprovalService(stale, new WorkflowEngine()).CancelAsync(instanceId, Owner, "سبب");

        Assert.Equal(ExternalApprovalOutcome.Conflict, withdrawn.Outcome);
        Assert.Equal(WorkflowConcurrency.ConflictMessage, withdrawn.Error);
        Assert.Equal(WorkflowStatus.InProgress, (await StoredAsync(connection, instanceId)).Status);
    }

    // ---------------------------------------------------------------- one running instance

    private static WorkflowInstance Running(Guid definitionId, Guid entityId, Guid tenant) => new()
    {
        TenantId = tenant,
        DefinitionId = definitionId,
        DefinitionVersion = 1,
        EntityType = "RecruitmentRequisition",
        EntityId = entityId,
        Status = WorkflowStatus.InProgress
    };

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_index_refuses_a_second_running_instance_and_accepts_one_after_the_first_ends()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("index");
        var entityId = Guid.NewGuid();
        await using var db = Db(connection);
        var definition = await db.WorkflowDefinitions.IgnoreQueryFilters().FirstAsync(d => d.Code == RequisitionCode);
        var first = Running(definition.Id, entityId, Tenant);
        Assert.Null(await WorkflowInstanceInsert.TrySaveAsync(db, first, default));

        var second = Running(definition.Id, entityId, Tenant);
        Assert.Equal(WorkflowInstanceInsert.ActiveEntityIndex, await WorkflowInstanceInsert.TrySaveAsync(db, second, default));
        Assert.Equal(EntityState.Detached, db.Entry(second).State);

        // Another tenant's entity with the same id is another entity.
        await using (var other = Db(connection, OtherTenant))
            Assert.Null(await WorkflowInstanceInsert.TrySaveAsync(other, Running(definition.Id, entityId, OtherTenant), default));

        first.Status = WorkflowStatus.Cancelled;
        await db.SaveChangesAsync();
        Assert.Null(await WorkflowInstanceInsert.TrySaveAsync(db, Running(definition.Id, entityId, Tenant), default));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_refused_insert_leaves_the_surrounding_transaction_usable()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("savepoint");
        var entityId = Guid.NewGuid();
        await using var db = Db(connection);
        var definition = await db.WorkflowDefinitions.IgnoreQueryFilters().FirstAsync(d => d.Code == RequisitionCode);
        Assert.Null(await WorkflowInstanceInsert.TrySaveAsync(db, Running(definition.Id, entityId, Tenant), default));

        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.NotNull(await WorkflowInstanceInsert.TrySaveAsync(db, Running(definition.Id, entityId, Tenant), default));

        Assert.Equal(1, await db.WorkflowInstances.CountAsync(i => i.EntityId == entityId));
        await transaction.CommitAsync();
    }

    private static ApprovalStartCoordinator Coordinator(HRCoreDbContext db, IWorkflowEngine engine) => new(
        db,
        Mock.Of<IPolicyOrchestrator>(),
        new WorkflowStarter(db, engine, NullLogger<WorkflowStarter>.Instance),
        new ConfigurationBuilder().Build());

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_submissions_at_once_through_the_coordinator_give_one_instance_and_both_point_at_it()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("submit");
        Guid requisitionId;
        await using (var seed = Db(connection))
        {
            var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(seed, Tenant);
            var requisition = Hcm026PostgresFixture.NewRequisition(Tenant, position.Id, RequisitionStatus.Draft);
            seed.RecruitmentRequisitions.Add(requisition);
            await seed.SaveChangesAsync();
            requisitionId = requisition.Id;
        }

        using var gate = new Barrier(2);

        async Task<ApprovalStartResult> SubmitAsync()
        {
            await using var db = Db(connection);
            var requisition = await db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId);
            return await Coordinator(db, new BarrierEngine(gate)).StartAndApplyAsync(
                new ApprovalStartRequest("RecruitmentRequisition", requisitionId, Tenant, RequisitionCode, "core.recruitment", Submitter, "s", null),
                async instanceId =>
                {
                    requisition.Status = RequisitionStatus.PendingApproval;
                    requisition.WorkflowInstanceId = instanceId;
                    await db.SaveChangesAsync();
                });
        }

        var results = await Task.WhenAll(Task.Run(SubmitAsync), Task.Run(SubmitAsync));

        Assert.All(results, r => Assert.True(r.Started, r.Error));
        Assert.Equal(results[0].InstanceId, results[1].InstanceId);
        await using var read = Db(connection);
        var running = await read.WorkflowInstances.AsNoTracking().Where(i => i.EntityId == requisitionId).ToListAsync();
        Assert.Equal(results[0].InstanceId, Assert.Single(running).Id);
        Assert.Equal(results[0].InstanceId, (await read.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == requisitionId)).WorkflowInstanceId);
    }

    private static WorkflowApprovalStartRequest Start(Guid entityId, string key) => new(
        "AssetAssignment", entityId, "ASSET-ASSIGN-APPROVAL", key, [], null, "تسليم عهدة", "/ui/admin-logistics/Assets/Details/1", null);

    private async Task<ExternalApprovalResult[]> StartBothAsync(string connection, WorkflowApprovalStartRequest one, WorkflowApprovalStartRequest two)
    {
        using var gate = new Barrier(2);

        async Task<ExternalApprovalResult> RunAsync(WorkflowApprovalStartRequest request)
        {
            await using var db = Db(connection);
            return await new ExternalApprovalService(db, new BarrierEngine(gate)).StartAsync(request, Owner);
        }

        return await Task.WhenAll(Task.Run(() => RunAsync(one)), Task.Run(() => RunAsync(two)));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_external_starts_with_one_key_give_one_instance()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("key");
        var entityId = Guid.NewGuid();

        var results = await StartBothAsync(connection, Start(entityId, "k-1"), Start(entityId, "k-1"));

        Assert.Single(results, r => r.Outcome == ExternalApprovalOutcome.Started);
        Assert.Single(results, r => r.Outcome == ExternalApprovalOutcome.Existing);
        Assert.Equal(results[0].Instance!.Id, results[1].Instance!.Id);
        await using var read = Db(connection);
        Assert.Equal(1, await read.WorkflowInstances.CountAsync(i => i.EntityId == entityId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Another_key_on_a_running_entity_is_a_conflict()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("key2");
        var entityId = Guid.NewGuid();

        var results = await StartBothAsync(connection, Start(entityId, "k-a"), Start(entityId, "k-b"));

        Assert.Single(results, r => r.Outcome == ExternalApprovalOutcome.Started);
        var loser = Assert.Single(results, r => r.Outcome == ExternalApprovalOutcome.Conflict);
        Assert.Equal(ExternalApprovalService.ActiveInstanceMessage, loser.Error);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task One_key_on_two_entities_at_once_is_a_conflict()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("key3");

        var results = await StartBothAsync(connection, Start(Guid.NewGuid(), "k-x"), Start(Guid.NewGuid(), "k-x"));

        Assert.Single(results, r => r.Outcome == ExternalApprovalOutcome.Started);
        var loser = Assert.Single(results, r => r.Outcome == ExternalApprovalOutcome.Conflict);
        Assert.Equal(ExternalApprovalService.KeyReusedMessage, loser.Error);
    }

    // ---------------------------------------------------------------- one transaction

    public sealed record Wf044Probe(Guid Id);

    private static Task<int> OutboxCountAsync(HRCoreDbContext db) =>
        db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "OutboxMessage" """).SingleAsync();

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

    private static EstablishmentChangeWorkflowHandler Establishment(HRCoreDbContext db, IEstablishmentChangeApplier applier)
    {
        var config = new ConfigurationBuilder().Build();
        var legal = new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db));
        return new EstablishmentChangeWorkflowHandler(db, new EstablishmentChangeLifecycleService(
            db, Mock.Of<ICodeGenerator>(), Mock.Of<IPolicyOrchestrator>(), new EstablishmentLegalSourceValidator(legal), applier,
            new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance), config));
    }

    private static async Task<(Guid RequestId, Guid InstanceId)> EstablishmentChangeAtLastStepAsync(HRCoreDbContext db)
    {
        var request = new EstablishmentChangeRequest
        {
            TenantId = Tenant,
            RequestNumber = $"EST-44-{Guid.NewGuid():N}"[..16],
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = "{}",
            RequestedAt = DateTime.UtcNow,
            CreatedBy = Creator
        };
        db.EstablishmentChangeRequests.Add(request);
        await db.SaveChangesAsync();
        var id = await new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance)
            .StartByWorkflowCodeAsync("ESTABLISHMENT-CHANGE-APPROVAL", "EstablishmentChangeRequest", request.Id, Tenant, Creator, "creator");
        request.WorkflowInstanceId = id;
        await db.SaveChangesAsync();
        var step1 = await Actions(db).ApproveAsync(id!.Value, HrCore(), "ok", 1);
        Assert.True(step1.Success, step1.Error);
        db.ChangeTracker.Clear();
        return (request.Id, id.Value);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_failing_handler_leaves_no_completed_instance_no_signature_and_no_outbox_row()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("fault");
        await using var provider = BusProvider(connection);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var (requestId, instanceId) = await EstablishmentChangeAtLastStepAsync(db);
        var signaturesBefore = await SignaturesAsync(connection);
        var rowsInsideTheTransaction = -1;

        async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)> PublishSaveAndFailAsync()
        {
            await publish.Publish(new Wf044Probe(Guid.NewGuid()));
            await db.SaveChangesAsync();
            rowsInsideTheTransaction = await OutboxCountAsync(db);
            throw new InvalidOperationException("injected failure in the handler");
        }

        var applier = new Mock<IEstablishmentChangeApplier>();
        applier.Setup(a => a.ApplyAsync(It.IsAny<EstablishmentChangeRequest>(), It.IsAny<CancellationToken>())).Returns(PublishSaveAndFailAsync);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Actions(db, publish: publish, establishment: Establishment(db, applier.Object)).ApproveAsync(instanceId, Approver(), "ok", 2));

        // Without this the test could pass with nothing ever written.
        Assert.Equal(1, rowsInsideTheTransaction);
        var stored = await StoredAsync(connection, instanceId);
        Assert.Equal(WorkflowStatus.InProgress, stored.Status);
        Assert.Equal(2, stored.CurrentStepLevel);
        Assert.Null(stored.CompletedAt);
        Assert.Single(History(stored));
        Assert.Equal(signaturesBefore, await SignaturesAsync(connection));
        await using var read = Db(connection);
        Assert.Equal(0, await OutboxCountAsync(read));
        Assert.Equal(EstablishmentChangeStatus.PendingApproval,
            (await read.EstablishmentChangeRequests.AsNoTracking().SingleAsync(r => r.Id == requestId)).Status);
        await bus.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_decision_and_its_effect_commit_together()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("commit");
        await using var db = Db(connection);
        var (requestId, instanceId) = await EstablishmentChangeAtLastStepAsync(db);
        var applier = new Mock<IEstablishmentChangeApplier>();
        applier.Setup(a => a.ApplyAsync(It.IsAny<EstablishmentChangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, Guid.NewGuid(), nameof(JobCatalogEntry), null, null));

        var result = await Actions(db, establishment: Establishment(db, applier.Object)).ApproveAsync(instanceId, Approver(), "ok", 2);

        Assert.True(result.Success, result.Error);
        Assert.Equal(WorkflowStatus.Completed, (await StoredAsync(connection, instanceId)).Status);
        await using var read = Db(connection);
        Assert.Equal(EstablishmentChangeStatus.Applied, (await read.EstablishmentChangeRequests.AsNoTracking().SingleAsync(r => r.Id == requestId)).Status);
        Assert.Equal(2, await SignaturesAsync(connection));
    }

    // ---------------------------------------------------------------- the migration, as the server runs it

    private static readonly string[] TablesRead =
    [
        "WorkflowInstances", "RecruitmentRequisitions", "OfferLetters", "EstablishmentChangeRequests",
        "SelectionCommitteeSessions", "RecruitmentAppeals", "RecruitmentCompetitions", "CompetitionResultVersions"
    ];

    private static async Task<T> AdminScalarAsync<T>(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    /// <summary>
    /// The schema of the previous migration, owned by a role with no superuser and no BYPASSRLS, with row security forced: the server's
    /// <c>hr_core_migrator</c>. Testcontainers connects as a superuser, which row security never touches, so nothing else proves this.
    /// </summary>
    private async Task<(string Admin, string Owner)> PreviousSchemaOwnedByAMigratorAsync(string name)
    {
        var admin = await fixture.Postgres.CreateDatabaseAsync($"wf044_{name}_{Guid.NewGuid():N}");
        await using (var db = Db(admin))
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await WorkflowPackSeeder.SeedAsync(db);
        }

        var role = $"wf044_owner_{Guid.NewGuid():N}"[..24];
        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            var builder = new NpgsqlConnectionStringBuilder(admin);
            await using var cmd = new NpgsqlCommand(
                $"""
                CREATE ROLE {role} LOGIN PASSWORD 'wf044_pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                GRANT CONNECT ON DATABASE "{builder.Database}" TO {role};
                GRANT USAGE, CREATE ON SCHEMA public TO {role};
                DO $do$
                DECLARE t record;
                BEGIN
                    FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                        EXECUTE format('ALTER TABLE public.%I OWNER TO {role}', t.tablename);
                    END LOOP;
                END
                $do$;
                """, conn);
            await cmd.ExecuteNonQueryAsync();
            foreach (var table in TablesRead)
            {
                await using var force = new NpgsqlCommand($"""ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY; ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;""", conn);
                await force.ExecuteNonQueryAsync();
            }
        }

        var owner = new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "wf044_pwd" }.ConnectionString;
        return (admin, owner);
    }

    private static WorkflowInstance Copy(Guid tenant, Guid definitionId, int version, string type, Guid entityId, int daysOld, string history = "[]") => new()
    {
        TenantId = tenant,
        DefinitionId = definitionId,
        DefinitionVersion = version,
        EntityType = type,
        EntityId = entityId,
        Status = WorkflowStatus.InProgress,
        StepHistoryJson = history,
        StartedAt = DateTime.UtcNow.AddDays(-daysOld)
    };

    private sealed record Group(Guid EntityId, Guid Kept, Guid Cancelled);

    /// <summary>A group whose linked instance is the older one, and carries <paramref name="linkedHistory"/>.</summary>
    private static async Task<Group> LinkedGroupAsync(string admin, Guid tenant, string linkedHistory, string unlinkedHistory)
    {
        await using var db = Db(admin, tenant);
        var definition = await db.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking().FirstAsync(d => d.Code == RequisitionCode);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenant);
        var requisition = Hcm026PostgresFixture.NewRequisition(tenant, position.Id, RequisitionStatus.PendingApproval);
        var linked = Copy(tenant, definition.Id, definition.Version, "RecruitmentRequisition", requisition.Id, 3, linkedHistory);
        var unlinked = Copy(tenant, definition.Id, definition.Version, "RecruitmentRequisition", requisition.Id, 1, unlinkedHistory);
        requisition.WorkflowInstanceId = linked.Id;
        db.RecruitmentRequisitions.Add(requisition);
        db.WorkflowInstances.AddRange(linked, unlinked);
        await db.SaveChangesAsync();
        return new Group(requisition.Id, linked.Id, unlinked.Id);
    }

    /// <summary>A group nothing links: a custody type, whose link lives in another service's database.</summary>
    private static async Task<Group> UnlinkedGroupAsync(string admin, Guid tenant, string newestHistory = "[]")
    {
        await using var db = Db(admin, tenant);
        var definition = await db.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking().FirstAsync(d => d.Code == "ASSET-ASSIGN-APPROVAL");
        var entityId = Guid.NewGuid();
        var older = Copy(tenant, definition.Id, definition.Version, "AssetAssignment", entityId, 3);
        var newest = Copy(tenant, definition.Id, definition.Version, "AssetAssignment", entityId, 1, newestHistory);
        db.WorkflowInstances.AddRange(older, newest);
        await db.SaveChangesAsync();
        return new Group(entityId, newest.Id, older.Id);
    }

    private static async Task AssertResolvedAsync(string admin, Guid tenant, Group group)
    {
        await using var db = Db(admin, tenant);
        var kept = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == group.Kept);
        var cancelled = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == group.Cancelled);
        Assert.Equal(WorkflowStatus.InProgress, kept.Status);
        Assert.Equal(WorkflowStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);
        var entry = History(cancelled)[^1];
        Assert.Equal("PLAN-44: نسخة مكررة", entry.Comments);
        Assert.Equal(WorkflowStepStatus.Withdrawn, entry.Status);
    }

    private static async Task AssertEveryTableIsForcedAsync(string admin)
    {
        foreach (var table in TablesRead)
            Assert.True(
                await AdminScalarAsync<bool>(admin, $"""SELECT relforcerowsecurity FROM pg_class WHERE oid = '"{table}"'::regclass"""),
                $"{table} lost FORCE ROW LEVEL SECURITY");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_resolves_duplicates_in_two_tenants_as_a_non_superuser_owner_and_leaves_row_security_forced()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync("dedup");
        var linkedDecided = await LinkedGroupAsync(admin, Tenant, Decided, "[]");
        var unlinkedDecided = await LinkedGroupAsync(admin, Tenant, "[]", Decided);
        var custody = await UnlinkedGroupAsync(admin, Tenant);
        var otherLinked = await LinkedGroupAsync(admin, OtherTenant, "[]", "[]");
        var otherCustody = await UnlinkedGroupAsync(admin, OtherTenant);

        // The control: this role sees no row, which is why PLAN-43's backfill changed nothing and reported success.
        Assert.True(await AdminScalarAsync<long>(admin, """SELECT count(*) FROM "WorkflowInstances" """) >= 10);
        Assert.Equal(0, await AdminScalarAsync<long>(owner, """SELECT count(*) FROM "WorkflowInstances" """));

        await using (var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options))
            await migrator.Database.MigrateAsync();

        await AssertResolvedAsync(admin, Tenant, linkedDecided);
        await AssertResolvedAsync(admin, Tenant, unlinkedDecided);
        await AssertResolvedAsync(admin, Tenant, custody);
        await AssertResolvedAsync(admin, OtherTenant, otherLinked);
        await AssertResolvedAsync(admin, OtherTenant, otherCustody);
        await AssertEveryTableIsForcedAsync(admin);
        Assert.Equal(2, await AdminScalarAsync<long>(admin,
            """SELECT count(*) FROM pg_indexes WHERE tablename = 'WorkflowInstances' AND indexname IN ('IX_WorkflowInstances_Tenant_Entity_Active', 'IX_WorkflowInstances_Tenant_Source_IdempotencyKey')"""));
        Assert.Equal(0, await AdminScalarAsync<long>(admin,
            """SELECT count(*) FROM (SELECT 1 FROM "WorkflowInstances" WHERE "Status" = 'InProgress' GROUP BY "TenantId", "EntityType", "EntityId" HAVING count(*) > 1) d"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_refuses_a_decision_nothing_links_and_changes_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync("refuse");
        var undecidable = await UnlinkedGroupAsync(admin, OtherTenant, Decided);

        await using var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.Database.MigrateAsync());

        Assert.Contains("the operator must choose", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains(undecidable.EntityId.ToString("D"), failure.MessageText, StringComparison.Ordinal);
        await AssertEveryTableIsForcedAsync(admin);
        Assert.Equal(0, await AdminScalarAsync<long>(admin, """SELECT count(*) FROM "WorkflowInstances" WHERE "Status" = 'Cancelled' """));
        Assert.Equal(0, await AdminScalarAsync<long>(admin, """SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%WF044%' """));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_role_that_does_not_own_the_tables_fails_the_migration_instead_of_finding_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var (admin, owner) = await PreviousSchemaOwnedByAMigratorAsync("notowner");
        var group = await LinkedGroupAsync(admin, Tenant, "[]", "[]");
        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""ALTER TABLE "RecruitmentRequisitions" OWNER TO postgres""", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var migrator = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(owner).Options);
        await Assert.ThrowsAsync<PostgresException>(() => migrator.Database.MigrateAsync());

        await AssertEveryTableIsForcedAsync(admin);
        Assert.Equal(WorkflowStatus.InProgress, (await StoredAsync(admin, group.Cancelled)).Status);
    }
}
