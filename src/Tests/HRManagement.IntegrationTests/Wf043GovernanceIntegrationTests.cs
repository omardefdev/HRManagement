using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-43 on PostgreSQL with the real migrations: what needs a real database, a real outbox or a real xmin.</summary>
[Collection("PgRabbit")]
public sealed class Wf043GovernanceIntegrationTests(PgRabbitFixture fixture)
{
    private const string Code = "ESTABLISHMENT-CHANGE-APPROVAL";
    private const string AssignmentCode = "ASSET-ASSIGN-APPROVAL";
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid Requester = Guid.Parse("43000000-0000-4000-8000-0000000000a1");

    private static readonly WorkflowDefinitionActor Author = new(Guid.Parse("43000000-0000-4000-8000-00000000000a"), Tenant);
    private static readonly WorkflowDefinitionActor Second = new(Guid.Parse("43000000-0000-4000-8000-00000000000b"), Tenant);
    private static readonly ExternalApprovalCaller Owner = new(true, ServiceClientIds.AdminLogistics, Requester.ToString("D"), "a-logistics-maker");

    private async Task<string> DatabaseAsync(string name)
    {
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf043_{name}_{Guid.NewGuid():N}");
        await using var db = Db(connection);
        await db.Database.MigrateAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        return connection;
    }

    private static HRCoreDbContext Db(string connection) =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(connection).Options, new Msg040FixedTenant(Tenant));

    private static WorkflowDefinitionChange NewVersion() => new()
    {
        Code = Code,
        Name = "اعتماد تغيير ملاك",
        DomainEntityType = "EstablishmentChangeRequest",
        Steps =
        [
            new WorkflowStepSnapshot { Level = 1, Name = "مراجعة", RoleRequired = HrManagementRoles.HrCore },
            new WorkflowStepSnapshot { Level = 2, Name = "اعتماد", RoleRequired = HrManagementRoles.HrManagementApprover }
        ]
    };

    private static WorkflowApprovalStartRequest Start(Guid? entity = null, string key = "k") => new(
        "AssetAssignment", entity ?? Guid.NewGuid(), AssignmentCode, key, [], null, "تسليم عهدة", "/ui/admin-logistics/Assets/Details/1",
        new Dictionary<string, string> { ["assetId"] = Guid.NewGuid().ToString("D"), ["employeeId"] = Guid.NewGuid().ToString("D") });

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_backfills_status_and_a_second_person_publishes_a_new_version()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("publish");
        await using var db = Db(connection);
        var service = new WorkflowDefinitionLifecycleService(db);

        var draft = await service.CreateDraftAsync(NewVersion(), Author);
        Assert.Equal(2, draft.Definition!.Version);
        Assert.True((await service.SubmitForPublishAsync(draft.Definition.Id, Author)).Success);
        Assert.Equal(WorkflowDefinitionFailure.Forbidden, (await service.PublishAsync(draft.Definition.Id, Author)).Failure);
        Assert.True((await service.PublishAsync(draft.Definition.Id, Second)).Success);

        await using var read = Db(connection);
        var stored = await read.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == draft.Definition.Id);
        Assert.Equal(WorkflowDefinitionStatus.Published, stored.Status);
        Assert.Equal(Second.UserId, stored.PublishedBy);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_start_up_patch_marks_rows_of_an_older_database()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("backfill");
        await using var db = Db(connection);

        // The rows as the migration leaves them on an existing database: row security hides them from its UPDATE.
        await db.Database.ExecuteSqlRawAsync("""UPDATE "WorkflowDefinitions" SET "Status" = 'Draft', "RequireDistinctApprovers" = false""");
        await WorkflowPackSeeder.BackfillGovernanceAsync(db);

        var rows = await db.WorkflowDefinitions.AsNoTracking().ToListAsync();
        Assert.All(rows.Where(d => d.IsPublished), d => Assert.Equal(WorkflowDefinitionStatus.Published, d.Status));
        Assert.Equal(WorkflowDefinitionStatus.Retired, rows.Single(d => d.Code == "RECRUITMENT-HIRE").Status);
        // PLAN-46: discipline needs two different people too.
        Assert.Equal([AssignmentCode, "ASSET-LOSS-APPROVAL", "DISCIPLINARY-APPROVAL"], rows.Where(d => d.RequireDistinctApprovers).Select(d => d.Code).Order(StringComparer.Ordinal));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Publish_and_return_at_once_on_one_definition_one_wins()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("xmin");
        Guid id;
        await using (var db = Db(connection))
        {
            var service = new WorkflowDefinitionLifecycleService(db);
            var draft = await service.CreateDraftAsync(NewVersion(), Author);
            await service.SubmitForPublishAsync(draft.Definition!.Id, Author);
            id = draft.Definition.Id;
        }

        // Both read the same row version first: this is the race, held open.
        await using var first = Db(connection);
        await using var second = Db(connection);
        var loaded = await first.WorkflowDefinitions.SingleAsync(d => d.Id == id);
        var racing = await second.WorkflowDefinitions.SingleAsync(d => d.Id == id);

        var published = await new WorkflowDefinitionLifecycleService(first).PublishAsync(id, Second);
        Assert.True(published.Success);
        Assert.Same(loaded, published.Definition);

        racing.Status = WorkflowDefinitionStatus.Draft;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var read = Db(connection);
        Assert.Equal(WorkflowDefinitionStatus.Published, (await read.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == id)).Status);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_version_outside_its_window_starts_nothing_on_either_path()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("validity");
        await using var db = Db(connection);
        var definition = await db.WorkflowDefinitions.SingleAsync(d => d.Code == AssignmentCode);
        var establishment = await db.WorkflowDefinitions.SingleAsync(d => d.Code == Code);
        definition.ValidTo = DateTime.UtcNow.AddDays(-1);
        establishment.ValidFrom = DateTime.UtcNow.AddDays(1);
        await db.SaveChangesAsync();

        var external = await new ExternalApprovalService(db, new WorkflowEngine()).StartAsync(Start(), Owner);
        var starter = await new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance)
            .StartByWorkflowCodeAsync(Code, "EstablishmentChangeRequest", Guid.NewGuid(), Tenant, null, null);

        Assert.Equal(ExternalApprovalOutcome.DefinitionMissing, external.Outcome);
        Assert.Null(starter);
        Assert.Empty(await db.WorkflowInstances.ToListAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_external_start_refuses_a_non_owner_another_key_and_returns_a_finished_instance_for_its_key()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("external");
        await using var db = Db(connection);
        var service = new ExternalApprovalService(db, new WorkflowEngine());
        var entity = Guid.NewGuid();

        Assert.Equal(ExternalApprovalOutcome.Forbidden, (await service.StartAsync(Start(entity), Owner with { ClientId = ServiceClientIds.Payroll })).Outcome);

        var started = await service.StartAsync(Start(entity, "k1"), Owner);
        Assert.Equal(ExternalApprovalOutcome.Started, started.Outcome);
        Assert.Equal(Requester, (await db.WorkflowInstances.AsNoTracking().SingleAsync()).RequestedByUserId);

        var conflict = await service.StartAsync(Start(entity, "k2"), Owner);
        Assert.Equal(ExternalApprovalOutcome.Conflict, conflict.Outcome);
        Assert.Equal(started.Instance!.Id, conflict.Instance!.Id);

        await service.CancelAsync(started.Instance.Id, Owner, "ألغى الطالب الإجراء");
        var again = await service.StartAsync(Start(entity, "k1"), Owner);
        Assert.Equal(ExternalApprovalOutcome.Existing, again.Outcome);
        Assert.Equal(WorkflowStatus.Cancelled, again.Instance!.Status);
        Assert.Equal(1, await db.WorkflowInstances.CountAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_coordinator_leaves_no_instance_when_the_save_of_the_entity_fails()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await DatabaseAsync("coordinator");
        await using var db = Db(connection);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Recruitment:Approvals:AlwaysStartWorkflow"] = "true"
        }).Build();
        var coordinator = new ApprovalStartCoordinator(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance),
            configuration);
        var request = new ApprovalStartRequest(
            "EstablishmentChangeRequest", Guid.NewGuid(), Tenant, Code, "Core.Establishment", Guid.NewGuid(), "maker", "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAndApplyAsync(request, _ => throw new InvalidOperationException("the save failed")));

        await using var read = Db(connection);
        Assert.Empty(await read.WorkflowInstances.ToListAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Approving_the_last_custody_step_writes_an_outbox_row_whose_event_carries_the_instance()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf043_outbox_{Guid.NewGuid():N}");
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
        services.AddDbContext<HRCoreDbContext>(o => o.UseNpgsql(connection));
        services.AddServiceMassTransit(configuration, configureOutbox: x => x.AddPostgresOutbox<HRCoreDbContext>(), enableInboxFilter: false);
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await db.Database.MigrateAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        var started = await new ExternalApprovalService(db, new WorkflowEngine()).StartAsync(Start(), Owner);
        var instanceId = started.Instance!.Id;
        db.ChangeTracker.Clear();

        var actions = new WorkflowInstanceActionService(
            db,
            new WorkflowEngine(),
            Mock.Of<IWorkflowSignatureService>(),
            scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(),
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentRequisitionWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.OfferLetterWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeWorkflowHandler(db, null!));

        var first = await actions.ApproveAsync(instanceId, new WorkflowActionActor(Guid.NewGuid(), "central", HrManagementRoles.HrCore), "ok", 1);
        Assert.True(first.Success, first.Error);
        Assert.Equal(0, await OutboxCountAsync(db));

        var last = await actions.ApproveAsync(instanceId, new WorkflowActionActor(Guid.NewGuid(), "approver", HrManagementRoles.HrManagementApprover), "ok", 2);
        Assert.True(last.Success, last.Error);

        // No hosted delivery service runs here, so the committed row is still in the table.
        var body = await db.Database.SqlQueryRaw<string>("""SELECT "Body" AS "Value" FROM "OutboxMessage" """).SingleAsync();
        using var envelope = JsonDocument.Parse(body);
        var message = envelope.RootElement.TryGetProperty("message", out var wrapped) ? wrapped : envelope.RootElement;
        Assert.Equal(instanceId, message.GetProperty("workflowInstanceId").GetGuid());
        Assert.Equal(2, message.GetProperty("schemaVersion").GetInt32());
        await bus.StopAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_custody_consumer_ignores_an_instance_that_is_not_linked()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf043_admin_{Guid.NewGuid():N}");
        await using var db = new AdminLogisticsDbContext(
            new DbContextOptionsBuilder<AdminLogisticsDbContext>().UseNpgsql(connection).Options, new Msg040FixedTenant(Tenant));
        await db.Database.MigrateAsync();
        var linked = Guid.NewGuid();
        var asset = new Asset { TenantId = Tenant, TagNumber = "WF43-1", Name = "حاسوب", Status = AssetStatus.InStorage };
        var assignment = new AssetAssignment
        {
            TenantId = Tenant,
            AssetId = asset.Id,
            EmployeeId = Guid.NewGuid(),
            AssignedAt = DateTime.UtcNow,
            Status = AssetAssignmentStatus.PendingApproval,
            WorkflowInstanceId = linked
        };
        db.Assets.Add(asset);
        db.AssetAssignments.Add(assignment);
        await db.SaveChangesAsync();
        var consumer = new AssetAssignmentApprovedConsumer(db, NullLogger<AssetAssignmentApprovedConsumer>.Instance);

        await consumer.Consume(Context(new Approved(assignment.Id, Guid.NewGuid())));
        await db.Entry(assignment).ReloadAsync();
        Assert.Equal(AssetAssignmentStatus.PendingApproval, assignment.Status);

        await consumer.Consume(Context(new Approved(assignment.Id, linked)));
        await db.Entry(assignment).ReloadAsync();
        Assert.Equal(AssetAssignmentStatus.Active, assignment.Status);
    }

    private static Task<int> OutboxCountAsync(HRCoreDbContext db) =>
        db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM "OutboxMessage" """).SingleAsync();

    private static ConsumeContext<IAssetAssignmentApproved> Context(IAssetAssignmentApproved message) =>
        Mock.Of<ConsumeContext<IAssetAssignmentApproved>>(c => c.Message == message && c.CancellationToken == CancellationToken.None);

    private sealed record Approved(Guid AssignmentId, Guid? WorkflowInstanceId) : IAssetAssignmentApproved
    {
        public Guid AssetId => Guid.NewGuid();
        public Guid EmployeeId => Guid.Empty;
        public DateTime OccurredAt => DateTime.UtcNow;
        public string SourceSystem => "test";
        public int SchemaVersion => 2;
    }
}

/// <summary>One HR.Core host for the whole class: a host per test would pass EF's limit of service providers in this process.</summary>
public sealed class Wf043ApiHost : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hr-wf043-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<HRCoreWebProgram> _root = new();

    public Wf043ApiHost()
    {
        Factory = _root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false");
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", $"Data Source={_dbPath}");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:RunMigrationsAtStartup"] = "false",
                ["ConnectionStrings:HRCoreDb"] = $"Data Source={_dbPath}",
                ["HrAuth:BehindGateway"] = "true",
                ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                ["HrGateway:InternalBaseUrl"] = "http://localhost:5200"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });
    }

    public WebApplicationFactory<HRCoreWebProgram> Factory { get; }

    public void Dispose()
    {
        Factory.Dispose();
        _root.Dispose();
        try { global::System.IO.File.Delete(_dbPath); }
        catch (IOException) { /* the host may still hold the file */ }
    }
}

/// <summary>PLAN-43 through the real HR.Core pipeline: routes, policies and the acting-user headers.</summary>
public sealed class Wf043ApiBoundaryIntegrationTests(Wf043ApiHost host) : IClassFixture<Wf043ApiHost>
{
    private readonly WebApplicationFactory<HRCoreWebProgram> _factory = host.Factory;

    private HttpClient User(string roles)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

    private HttpClient Service(string clientId, string scope = ServiceScopes.HrCoreWorkflow)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.ServiceClientHeader, clientId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ServiceScopeHeader, scope);
        return client;
    }

    private static WorkflowApprovalStartRequest Body() => new(
        "AssetAssignment", Guid.NewGuid(), "ASSET-ASSIGN-APPROVAL", "AssetAssignment:api", [], null, "تسليم عهدة",
        "/ui/admin-logistics/Assets/Details/1", null);

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_service_account_gets_403_on_a_step(string action)
    {
        using var service = Service(ServiceClientIds.AdminLogistics);

        var response = await service.PostAsJsonAsync($"/api/workflow-instances/{Guid.NewGuid():D}/{action}", new { Comments = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_user_gets_403_on_the_start_and_on_the_cancel_even_as_admin()
    {
        using var user = User($"{HrManagementRoles.Admin},{HrManagementRoles.HrCore}");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/workflow-instances", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync($"/api/workflow-instances/{Guid.NewGuid():D}/cancel", new { Reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_service_without_the_scope_or_another_owner_gets_403()
    {
        using var noScope = Service(ServiceClientIds.AdminLogistics, ServiceScopes.HrCoreLookup);
        using var other = Service(ServiceClientIds.Payroll);

        Assert.Equal(HttpStatusCode.Forbidden, (await noScope.PostAsJsonAsync("/api/workflow-instances", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync("/api/workflow-instances", Body())).StatusCode);
    }

    [Fact]
    public async Task The_owner_without_an_acting_user_gets_400()
    {
        using var owner = Service(ServiceClientIds.AdminLogistics);

        var response = await owner.PostAsJsonAsync("/api/workflow-instances", Body());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ExternalApprovalService.RequesterMessage, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(HrManagementRoles.HrCore)]
    [InlineData(HrManagementRoles.HrManagementApprover)]
    public async Task Hr_core_does_not_write_definitions_or_publish(string role)
    {
        using var user = User(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/workflow-definitions", new { Code = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/workflow-definitions/{Guid.NewGuid():D}/publish", null)).StatusCode);
    }

    [Fact]
    public async Task A_service_does_not_write_definitions()
    {
        using var service = Service(ServiceClientIds.AdminLogistics);

        Assert.Equal(HttpStatusCode.Forbidden, (await service.PostAsJsonAsync("/api/workflow-definitions", new { Code = "X" })).StatusCode);
    }

    [Fact]
    public async Task A_definition_write_without_an_antiforgery_token_is_refused()
    {
        using var user = User(HrManagementRoles.WorkflowAdmin);

        var response = await user.PostAsJsonAsync("/api/workflow-definitions", new { Code = "X" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Workflow_admin_reads_definitions_and_nothing_else_of_HR_Core()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HRCoreDbContext>().Database.EnsureCreatedAsync();
        using var user = User(HrManagementRoles.WorkflowAdmin);

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/workflow-definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/workflow-instances")).StatusCode);
    }
}
