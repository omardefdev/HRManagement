using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-46 on PostgreSQL, HR.Core's side: the key, the owner, the conflict, and the outcome event written to the outbox in the transaction of
/// the decision or the withdrawal, with every decision.
/// </summary>
[Collection("PgRabbit")]
public sealed class Wf046ExternalOutcomePostgresTests(PgRabbitFixture fixture)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid Requester = Guid.Parse("46000000-0000-4000-8000-000000000b01");
    private static readonly Guid Checker = Guid.Parse("46000000-0000-4000-8000-000000000b02");
    private static readonly Guid Authority = Guid.Parse("46000000-0000-4000-8000-000000000b03");
    private static readonly SoftwareSignatureService Signer = new(Options.Create(new SignatureOptions()));
    private static readonly ExternalApprovalCaller Compliance = new(true, ServiceClientIds.Compliance, Requester.ToString("D"), "a-compliance");

    private static HRCoreDbContext HrDb(string connection)
    {
        var context = new Msg040FixedTenant(Tenant);
        return new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql(connection).AddInterceptors(new TenantIdStampInterceptor(context)).Options,
            context);
    }

    private async Task<string> HrDatabaseAsync(string name)
    {
        var connection = await fixture.Postgres.CreateDatabaseAsync($"wf046_{name}_{Guid.NewGuid():N}");
        await using var db = HrDb(connection);
        await db.Database.MigrateAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);
        return connection;
    }

    private static WorkflowInstanceActionService Actions(HRCoreDbContext db, IPublishEndpoint publish) =>
        new(
            db,
            new WorkflowEngine(),
            new WorkflowSignatureService(db, Signer, new Msg040FixedTenant(Tenant)),
            publish,
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new EstablishmentChangeWorkflowHandler(db, null!));

    private static WorkflowApprovalStartRequest Discipline(Guid id, string? key = null) => new(
        "DisciplinaryAction",
        id,
        "DISCIPLINARY-APPROVAL",
        key ?? $"DisciplinaryAction:{id:D}:1",
        [Requester],
        null,
        "إجراء تأديبي — إنذار",
        $"/ui/compliance/DisciplinaryActions/Details/{id:D}",
        new Dictionary<string, string> { ["actionType"] = "إنذار" },
        Guid.NewGuid());

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

    /// <summary>A JSON field of the outbox body, which MassTransit writes indented.</summary>
    private static System.Text.RegularExpressions.Regex Field(string name, string value) =>
        new($"\"{name}\"\\s*:\\s*\"{System.Text.RegularExpressions.Regex.Escape(value)}\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static long OutcomeRows(string connection) =>
        ScalarAsync<long>(connection, """SELECT count(*) FROM "OutboxMessage" WHERE "MessageType" LIKE '%IWorkflowOutcomeDecided%'""").GetAwaiter().GetResult();

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_same_key_answers_the_same_instance_another_owner_is_refused_and_another_key_conflicts()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await HrDatabaseAsync("key");
        var id = Guid.NewGuid();
        await using var db = HrDb(connection);
        var service = new ExternalApprovalService(db, new WorkflowEngine());

        var first = await service.StartAsync(Discipline(id), Compliance);
        var again = await service.StartAsync(Discipline(id), Compliance);
        var stranger = await service.StartAsync(Discipline(id), Compliance with { ClientId = ServiceClientIds.IdCard });
        var otherKey = await service.StartAsync(Discipline(id, $"DisciplinaryAction:{id:D}:9"), Compliance);

        Assert.Equal(ExternalApprovalOutcome.Started, first.Outcome);
        Assert.Equal(ExternalApprovalOutcome.Existing, again.Outcome);
        Assert.Equal(first.Instance!.Id, again.Instance!.Id);
        Assert.Equal(ExternalApprovalOutcome.Forbidden, stranger.Outcome);
        Assert.Equal(ExternalApprovalOutcome.Conflict, otherKey.Outcome);
        Assert.Equal(1L, await ScalarAsync<long>(connection, $"""SELECT count(*) FROM "WorkflowInstances" WHERE "EntityId" = '{id:D}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_last_approval_writes_the_outcome_with_every_decision_in_the_same_transaction()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await HrDatabaseAsync("approve");
        await using var provider = BusProvider(connection);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        Guid instanceId;
        await using (var db = HrDb(connection))
        {
            instanceId = (await new ExternalApprovalService(db, new WorkflowEngine()).StartAsync(Discipline(Guid.NewGuid()), Compliance)).Instance!.Id;
            var step1 = await Actions(db, Mock.Of<IPublishEndpoint>()).ApproveAsync(instanceId, new WorkflowActionActor(Checker, "a-compliance-2", HrManagementRoles.Compliance), "ok", 1);
            Assert.True(step1.Success, step1.Error);
        }

        Assert.Equal(0L, OutcomeRows(connection));
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            var result = await Actions(db, scope.ServiceProvider.GetRequiredService<IPublishEndpoint>())
                .ApproveAsync(instanceId, new WorkflowActionActor(Authority, "a-management-approver", HrManagementRoles.HrManagementApprover), "ok", 2);
            Assert.True(result.Success, result.Error);
        }

        await bus.StopAsync();
        Assert.Equal("Completed", await ScalarAsync<string>(connection, $"""SELECT "Status" FROM "WorkflowInstances" WHERE "Id"='{instanceId:D}'"""));
        Assert.Equal(1L, OutcomeRows(connection));
        var body = await ScalarAsync<string>(connection, """SELECT "Body" FROM "OutboxMessage" WHERE "MessageType" LIKE '%IWorkflowOutcomeDecided%'""");
        Assert.Matches(Field("outcome", "Approved"), body);
        Assert.Contains(Checker.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Authority.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(Field("roleRequired", "compliance"), body);
        Assert.Matches(Field("roleRequired", "hr-management-approver"), body);
        Assert.Matches(Field("workflowCode", "DISCIPLINARY-APPROVAL"), body);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_withdrawal_writes_cancelled_and_a_withdrawal_after_the_end_answers_the_final_status()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await HrDatabaseAsync("cancel");
        await using var provider = BusProvider(connection);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        Guid withdrawn;
        Guid finished;
        await using (var db = HrDb(connection))
        {
            var service = new ExternalApprovalService(db, new WorkflowEngine());
            withdrawn = (await service.StartAsync(Discipline(Guid.NewGuid()), Compliance)).Instance!.Id;
            finished = (await service.StartAsync(Discipline(Guid.NewGuid()), Compliance)).Instance!.Id;
            var reject = await Actions(db, Mock.Of<IPublishEndpoint>()).RejectAsync(finished, new WorkflowActionActor(Checker, "a-compliance-2", HrManagementRoles.Compliance), "ناقص", targetStepLevel: 1);
            Assert.True(reject.Success, reject.Error);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            var service = new ExternalApprovalService(db, new WorkflowEngine(), scope.ServiceProvider.GetRequiredService<IPublishEndpoint>());
            Assert.Equal(ExternalApprovalOutcome.Withdrawn, (await service.CancelAsync(withdrawn, Compliance, "سحب")).Outcome);

            var late = await service.CancelAsync(finished, Compliance, "سحب");
            Assert.Equal(ExternalApprovalOutcome.Conflict, late.Outcome);
            Assert.Equal(WorkflowStatus.Rejected, late.Instance!.Status);
            // The client turns this 409 into AlreadyTerminal with the final outcome.
            Assert.Equal(WorkflowApprovalOutcomes.Rejected, WorkflowApprovalOutcomes.FromEngineStatus(late.Instance.Status.ToString()));
        }

        await bus.StopAsync();
        Assert.Equal(1L, OutcomeRows(connection));
        var body = await ScalarAsync<string>(connection, """SELECT "Body" FROM "OutboxMessage" WHERE "MessageType" LIKE '%IWorkflowOutcomeDecided%'""");
        Assert.Matches(Field("outcome", "Cancelled"), body);
        Assert.Contains("سحب", body, StringComparison.Ordinal);
        Assert.Equal("Cancelled", await ScalarAsync<string>(connection, $"""SELECT "Status" FROM "WorkflowInstances" WHERE "Id"='{withdrawn:D}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_status_is_read_by_its_owner_only()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var connection = await HrDatabaseAsync("status");
        await using var db = HrDb(connection);
        var service = new ExternalApprovalService(db, new WorkflowEngine());
        var instance = (await service.StartAsync(Discipline(Guid.NewGuid()), Compliance)).Instance!;

        var status = await service.GetStatusAsync(instance.Id, Compliance);

        Assert.Equal(WorkflowApprovalOutcomes.InProgress, status!.Status);
        Assert.Equal(HrManagementRoles.Compliance, status.CurrentStepRole);
        Assert.Null(await service.GetStatusAsync(instance.Id, Compliance with { ClientId = ServiceClientIds.AdminLogistics }));
    }
}
