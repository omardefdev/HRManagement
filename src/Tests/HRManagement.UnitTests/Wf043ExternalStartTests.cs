using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.Integration;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HR.Core.Web.Controllers;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-43 (G-WF-05): the owner starts, the requester is the server's, no service acts on a step, unlinked outcomes do nothing.</summary>
public sealed class Wf043ExternalStartTests
{
    private const string AssignmentCode = "ASSET-ASSIGN-APPROVAL";
    private static readonly Guid Requester = Guid.Parse("43000000-0000-4000-8000-0000000000aa");

    private static readonly ExternalApprovalCaller Owner =
        new(true, ServiceClientIds.AdminLogistics, Requester.ToString("D"), "a-logistics-maker");

    private static async Task<(HRCoreDbContext Db, ExternalApprovalService Service)> ArrangeAsync()
    {
        var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        await WorkflowPackSeeder.SeedAsync(db);
        return (db, new ExternalApprovalService(db, new WorkflowEngine()));
    }

    private static WorkflowApprovalStartRequest Request(Guid? entityId = null, string? key = null) => new(
        "AssetAssignment",
        entityId ?? Guid.NewGuid(),
        AssignmentCode,
        key ?? "AssetAssignment:1",
        [Guid.Parse("43000000-0000-4000-8000-0000000000bb")],
        Guid.Parse("43000000-0000-4000-8000-0000000000cc"),
        "تسليم عهدة TAG-1",
        "/ui/admin-logistics/Assets/Details/1",
        new Dictionary<string, string> { ["assetId"] = Guid.NewGuid().ToString("D") },
        Guid.Parse("43000000-0000-4000-8000-0000000000dd"));

    [Fact]
    public async Task The_owner_starts_and_the_requester_comes_from_the_header()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var request = Request();

        var result = await service.StartAsync(request, Owner);

        Assert.Equal(ExternalApprovalOutcome.Started, result.Outcome);
        var stored = await db.WorkflowInstances.SingleAsync();
        Assert.Equal(Requester, stored.RequestedByUserId);
        Assert.Equal(Requester, stored.InitiatedBy);
        Assert.Equal("a-logistics-maker", stored.RequestedByName);
        Assert.Equal(ServiceClientIds.AdminLogistics, stored.SourceClientId);
        Assert.Equal(request.IdempotencyKey, stored.IdempotencyKey);
        Assert.Equal(request.OrgUnitId, stored.ScopeOrgUnitId);
        Assert.Equal(request.SubjectEmployeeId, stored.SubjectEmployeeId);
        Assert.Equal(request.DisplayTitle, stored.DisplayTitle);
        Assert.Equal(request.DetailsUrl, stored.DetailsUrl);
        Assert.Equal(request.ExcludedApproverUserIds, JsonSerializer.Deserialize<List<Guid>>(stored.ExcludedApproverUserIdsJson!));
        Assert.Equal(request.Facts!["assetId"], WorkflowCondition.ParseContext(stored.ContextJson)!["assetId"]);
    }

    [Fact]
    public void The_wire_body_has_no_requester_and_keeps_PLAN_60s_order()
    {
        var names = typeof(WorkflowApprovalStartRequest).GetConstructors().Single().GetParameters().Select(p => p.Name);

        Assert.Equal(
            ["EntityType", "EntityId", "WorkflowCode", "IdempotencyKey", "ExcludedApproverUserIds", "OrgUnitId", "DisplayTitle", "DetailsUrl", "Facts", "SubjectEmployeeId"],
            names);
    }

    [Theory]
    [InlineData(true, ServiceClientIds.Payroll, "AssetAssignment")]
    [InlineData(false, ServiceClientIds.AdminLogistics, "AssetAssignment")]
    [InlineData(true, ServiceClientIds.AdminLogistics, "RecruitmentRequisition")]
    [InlineData(true, ServiceClientIds.AdminLogistics, "Ticket")]
    public async Task Only_the_owner_of_a_registered_type_starts(bool isService, string client, string entityType)
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        var result = await service.StartAsync(
            Request() with { EntityType = entityType }, new ExternalApprovalCaller(isService, client, Requester.ToString("D"), null));

        Assert.Equal(ExternalApprovalOutcome.Forbidden, result.Outcome);
        Assert.Equal(ExternalApprovalService.NotOwnerMessage, result.Error);
        Assert.Empty(await db.WorkflowInstances.ToListAsync());
    }

    [Fact]
    public async Task A_workflow_code_outside_the_type_is_refused_and_an_empty_one_takes_the_default()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        var refused = await service.StartAsync(Request() with { WorkflowCode = "ASSET-LOSS-APPROVAL" }, Owner);
        var started = await service.StartAsync(Request() with { WorkflowCode = "" }, Owner);

        Assert.Equal(ExternalApprovalService.WorkflowCodeMessage, refused.Error);
        Assert.Equal(ExternalApprovalOutcome.Started, started.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Without_a_requester_nothing_starts(string? actingUser)
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        var result = await service.StartAsync(Request(), Owner with { ActingUser = actingUser });

        Assert.Equal(ExternalApprovalOutcome.Invalid, result.Outcome);
        Assert.Equal(ExternalApprovalService.RequesterMessage, result.Error);
    }

    [Fact]
    public async Task The_same_key_returns_the_same_instance_even_when_finished()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var request = Request();
        var first = await service.StartAsync(request, Owner);

        var again = await service.StartAsync(request, Owner);
        Assert.Equal(ExternalApprovalOutcome.Existing, again.Outcome);
        Assert.Equal(first.Instance!.Id, again.Instance!.Id);

        var stored = await db.WorkflowInstances.SingleAsync();
        stored.Status = WorkflowStatus.Rejected;
        await db.SaveChangesAsync();

        var finished = await service.StartAsync(request, Owner);
        Assert.Equal(ExternalApprovalOutcome.Existing, finished.Outcome);
        Assert.Equal(WorkflowStatus.Rejected, finished.Instance!.Status);
        Assert.Equal(1, await db.WorkflowInstances.CountAsync());
    }

    [Fact]
    public async Task Another_key_for_a_running_entity_is_a_conflict_that_names_the_instance()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var entity = Guid.NewGuid();
        var first = await service.StartAsync(Request(entity, "k1"), Owner);

        var result = await service.StartAsync(Request(entity, "k2"), Owner);

        Assert.Equal(ExternalApprovalOutcome.Conflict, result.Outcome);
        Assert.Equal(ExternalApprovalService.ActiveInstanceMessage, result.Error);
        Assert.Equal(first.Instance!.Id, result.Instance!.Id);
    }

    [Fact]
    public async Task A_key_used_for_another_entity_is_a_conflict()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        await service.StartAsync(Request(key: "k"), Owner);

        var result = await service.StartAsync(Request(key: "k"), Owner);

        Assert.Equal(ExternalApprovalService.KeyReusedMessage, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_key_is_required(string key)
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        Assert.Equal(ExternalApprovalService.IdempotencyKeyMessage, (await service.StartAsync(Request(key: key), Owner)).Error);
    }

    [Fact]
    public async Task The_key_has_a_limit()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        Assert.Equal(ExternalApprovalService.IdempotencyKeyMessage, (await service.StartAsync(Request(key: new string('k', 201)), Owner)).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("عهدة 12345678")]
    [InlineData("عهدة ١٢٣٤٥٦٧٨")]
    public async Task A_title_is_required_and_holds_no_long_number(string title)
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        Assert.Equal(ExternalApprovalService.DisplayTitleMessage, (await service.StartAsync(Request() with { DisplayTitle = title }, Owner)).Error);
    }

    [Fact]
    public async Task A_title_has_a_limit_and_seven_digits_pass()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        Assert.Equal(ExternalApprovalService.DisplayTitleMessage, (await service.StartAsync(Request() with { DisplayTitle = new string('ع', 301) }, Owner)).Error);
        Assert.Equal(ExternalApprovalOutcome.Started, (await service.StartAsync(Request() with { DisplayTitle = "عهدة 1234567" }, Owner)).Outcome);
    }

    [Theory]
    [InlineData("https://evil.example/ui/admin-logistics/x")]
    [InlineData("//evil.example/ui/admin-logistics/x")]
    [InlineData("/ui/hr-core/x")]
    [InlineData("/ui/admin-logistics/../hr-core/x")]
    [InlineData("/ui/admin-logistics//evil.example")]
    [InlineData("/ui/admin-logistics\\x")]
    [InlineData("")]
    public async Task The_details_url_stays_inside_the_owner(string url)
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;

        Assert.Equal(ExternalApprovalService.DetailsUrlMessage, (await service.StartAsync(Request() with { DetailsUrl = url }, Owner)).Error);
    }

    [Fact]
    public async Task Facts_have_limits()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var tooMany = Enumerable.Range(0, 21).ToDictionary(i => $"k{i}", i => "v");

        foreach (var facts in new[]
                 {
                     tooMany,
                     new Dictionary<string, string> { [new string('k', 51)] = "v" },
                     new Dictionary<string, string> { ["k"] = new string('v', 201) }
                 })
        {
            Assert.Equal(ExternalApprovalService.FactsMessage, (await service.StartAsync(Request() with { Facts = facts }, Owner)).Error);
        }
    }

    [Fact]
    public async Task No_valid_published_definition_is_the_coordinators_message()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var definition = await db.WorkflowDefinitions.SingleAsync(d => d.Code == AssignmentCode);
        definition.ValidTo = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var result = await service.StartAsync(Request(), Owner);

        Assert.Equal(ExternalApprovalOutcome.DefinitionMissing, result.Outcome);
        Assert.Equal(ApprovalStartCoordinator.MissingDefinitionMessage, result.Error);
    }

    [Fact]
    public async Task The_latest_valid_version_starts_and_a_binding_has_no_say()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var v1 = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Code == AssignmentCode);
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Code = AssignmentCode,
            Name = v1.Name,
            Version = 2,
            DomainEntityType = v1.DomainEntityType,
            IsPublished = true,
            Status = WorkflowDefinitionStatus.Published,
            StepsJson = v1.StepsJson
        });
        db.PolicyWorkflowBindings.Add(new HR.Core.Domain.Entities.Policy.PolicyWorkflowBinding
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Domain = PolicyDomains.AssetsCustody,
            WorkflowCode = AssignmentCode,
            WorkflowDefinitionId = v1.Id
        });
        await db.SaveChangesAsync();

        var result = await service.StartAsync(Request(), Owner);

        Assert.Equal(2, result.Instance!.DefinitionVersion);
    }

    [Fact]
    public async Task A_definition_whose_steps_all_skip_starts_nothing()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var definition = await db.WorkflowDefinitions.SingleAsync(d => d.Code == AssignmentCode);
        definition.StepsJson = JsonSerializer.Serialize(new[]
        {
            new WorkflowStepSnapshot
            {
                Level = 1, Name = "s", RoleRequired = HrManagementRoles.HrCore,
                Condition = new WorkflowCondition { Field = "x", Operator = "Equals", Value = "never" }
            }
        });
        await db.SaveChangesAsync();

        var result = await service.StartAsync(Request(), Owner);

        Assert.Equal(ExternalApprovalOutcome.DefinitionMissing, result.Outcome);
        Assert.Empty(await db.WorkflowInstances.ToListAsync());
    }

    [Fact]
    public async Task The_loss_report_names_the_holder_as_the_subject()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(AssetApprovalStarter.HttpClientName, c => c.BaseAddress = new Uri("http://hr-core-web:8080/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddDbContext<AdminLogisticsDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<AssetApprovalStarter>();
        await using var provider = services.BuildServiceProvider();
        var holder = Guid.NewGuid();

        await provider.CreateScope().ServiceProvider.GetRequiredService<AssetApprovalStarter>().StartAsync(
            new AssetLossReport { AssetId = Guid.NewGuid(), Reason = AssetLossReason.Theft }, new Asset { TagNumber = "TAG-1", IsPrivileged = true, Value = 10m }, holder);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(holder, body.RootElement.GetProperty("subjectEmployeeId").GetGuid());
    }

    [Fact]
    public async Task The_owner_withdraws_a_running_instance_with_a_reason()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var started = await service.StartAsync(Request(), Owner);

        Assert.Equal(ExternalApprovalService.CancelReasonMessage, (await service.CancelAsync(started.Instance!.Id, Owner, " ")).Error);

        var result = await service.CancelAsync(started.Instance.Id, Owner, "ألغى الطالب الإجراء");

        Assert.Equal(ExternalApprovalOutcome.Withdrawn, result.Outcome);
        var stored = await db.WorkflowInstances.AsNoTracking().SingleAsync();
        Assert.Equal(WorkflowStatus.Cancelled, stored.Status);
        Assert.NotNull(stored.CompletedAt);
        var entry = Assert.Single(JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(stored.StepHistoryJson)!);
        Assert.Equal(WorkflowStepStatus.Withdrawn, entry.Status);
        Assert.Equal("ألغى الطالب الإجراء", entry.Comments);
    }

    [Fact]
    public async Task Only_the_owner_withdraws()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var started = await service.StartAsync(Request(), Owner);

        var other = await service.CancelAsync(started.Instance!.Id, Owner with { ClientId = ServiceClientIds.Payroll }, "سبب");
        var user = await service.CancelAsync(started.Instance.Id, Owner with { IsServiceAccount = false }, "سبب");

        Assert.Equal(ExternalApprovalOutcome.Forbidden, other.Outcome);
        Assert.Equal(ExternalApprovalOutcome.Forbidden, user.Outcome);
        Assert.Equal(WorkflowStatus.InProgress, (await db.WorkflowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(ExternalApprovalOutcome.NotFound, (await service.CancelAsync(Guid.NewGuid(), Owner, "سبب")).Outcome);
    }

    [Fact]
    public async Task A_finished_instance_is_a_conflict_with_its_final_state()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var started = await service.StartAsync(Request(), Owner);
        var stored = await db.WorkflowInstances.SingleAsync();
        stored.Status = WorkflowStatus.Completed;
        await db.SaveChangesAsync();

        var result = await service.CancelAsync(started.Instance!.Id, Owner, "سبب");

        Assert.Equal(ExternalApprovalOutcome.Conflict, result.Outcome);
        Assert.Equal(ExternalApprovalService.FinishedMessage, result.Error);
        Assert.Equal(WorkflowStatus.Completed, result.Instance!.Status);
    }

    [Fact]
    public void Withdraw_needs_a_running_instance()
    {
        var engine = new WorkflowEngine();

        Assert.False(engine.Withdraw(new WorkflowInstanceState { Status = WorkflowStatus.Completed }, null, null, "x").Success);
        var ok = engine.Withdraw(new WorkflowInstanceState { Status = WorkflowStatus.InProgress, CurrentStepLevel = 2 }, Requester, "n", "x");
        Assert.Equal(WorkflowStatus.Cancelled, ok.NewStatus);
        Assert.Equal(WorkflowStepStatus.Withdrawn, ok.HistoryEntry!.Status);
    }

    [Fact]
    public void A_user_never_passes_the_start_policy_and_a_service_never_passes_a_step_policy()
    {
        var auth = Authorization();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, HrManagementRoles.Admin), new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)], "test"));
        var service = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("azp", ServiceClientIds.AdminLogistics),
                new Claim("scope", ServiceScopes.HrCoreWorkflow),
                new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)
            ],
            "test"));

        Assert.False(auth.AuthorizeAsync(user, HrManagementPolicies.WorkflowExternalStart).Result.Succeeded);
        Assert.True(auth.AuthorizeAsync(service, HrManagementPolicies.WorkflowExternalStart).Result.Succeeded);
        Assert.True(auth.AuthorizeAsync(user, HrManagementPolicies.WorkflowStepAction).Result.Succeeded);
        Assert.False(auth.AuthorizeAsync(service, HrManagementPolicies.WorkflowStepAction).Result.Succeeded);
        Assert.False(auth.AuthorizeAsync(service, HrManagementPolicies.HrCoreBranchOperation).Result.Succeeded);
    }

    [Fact]
    public void The_routes_carry_their_policies()
    {
        var external = typeof(WorkflowExternalInstancesController).GetCustomAttributes<AuthorizeAttribute>();
        Assert.Equal([HrManagementPolicies.WorkflowExternalStart], external.Select(a => a.Policy));

        foreach (var name in new[] { nameof(WorkflowInstancesController.Approve), nameof(WorkflowInstancesController.Reject) })
        {
            var policies = typeof(WorkflowInstancesController).GetMethod(name)!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);
            Assert.Contains(HrManagementPolicies.WorkflowStepAction, policies);
        }

        Assert.DoesNotContain(
            typeof(WorkflowInstancesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.Name == "Start");
    }

    [Theory]
    [InlineData("/api/hr-core/api/workflow-instances", HrManagementPolicies.WorkflowExternalStart)]
    [InlineData("/api/hr-core/api/workflow-instances/", HrManagementPolicies.WorkflowExternalStart)]
    [InlineData("/api/hr-core/api/workflow-instances/00000000-0000-0000-0000-000000000001/cancel", HrManagementPolicies.WorkflowExternalStart)]
    [InlineData("/api/hr-core/api/workflow-instances/00000000-0000-0000-0000-000000000001/approve", HrManagementPolicies.WorkflowStepAction)]
    [InlineData("/api/hr-core/api/workflow-instances/00000000-0000-0000-0000-000000000001/reject", HrManagementPolicies.WorkflowStepAction)]
    public void The_gateway_keeps_users_off_the_start(string path, string expected)
    {
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy("hr-core", HttpMethods.Post, new PathString(path), out var policy));
        Assert.Equal(expected, policy);
    }

    [Fact]
    public async Task A_service_account_is_refused_a_step_even_past_the_policies()
    {
        var (db, service) = await ArrangeAsync();
        await using var _ = db;
        var started = await service.StartAsync(Request(), Owner);
        var definition = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == started.Instance!.DefinitionId);
        var evaluator = HR.Core.Infrastructure.Services.Workflow.Gates.WorkflowApprovalEvaluator.CreateDefault(db);
        var actor = new WorkflowActionActor(
            Guid.NewGuid(), "service-account-svc-admin-logistics",
            new HashSet<string> { HrManagementRoles.HrCore }, IsServiceAccount: true);

        var approve = await evaluator.GetBlockReasonAsync(
            started.Instance!, definition, actor, HR.Core.Infrastructure.Services.Workflow.Gates.WorkflowActionKind.Approve);
        var reject = await evaluator.GetBlockReasonAsync(
            started.Instance!, definition, actor, HR.Core.Infrastructure.Services.Workflow.Gates.WorkflowActionKind.Reject);

        Assert.Equal(WorkflowStageGuard.ServiceAccountMessage, approve);
        Assert.Equal(WorkflowStageGuard.ServiceAccountMessage, reject);
    }

    [Fact]
    public void The_allowlist_is_empty_so_nothing_starts_from_enforce()
    {
        Assert.False(PolicyWorkflowTriggerAllowlist.Allows(PolicyDomains.AssetsCustody, "AssetAssignment", AssignmentCode));
        Assert.True(ExternalApprovalTypes.IsRegistered("AssetAssignment"));
        Assert.True(ExternalApprovalTypes.IsRegistered("assetlossreport"));
        Assert.False(ExternalApprovalTypes.IsRegistered("RecruitmentRequisition"));
    }

    [Fact]
    public async Task The_handler_sends_the_acting_user_with_an_encoded_name_and_the_scope_serves_a_job()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Requester.ToString("D")), new Claim("preferred_username", "عمر")], "test"));
        var fromRequest = await SendAsync(new DefaultHttpContext { User = user });
        Assert.Equal(Requester.ToString("D"), Header(fromRequest, ForwardActingUserHandler.HeaderName));
        Assert.Equal("عمر", ForwardActingUserHandler.DecodeName(Header(fromRequest, ForwardActingUserHandler.NameHeaderName)));

        var stored = Guid.NewGuid();
        using (ActingUserScope.Begin(stored, "مهمة خلفية"))
        {
            var fromJob = await SendAsync(null);
            Assert.Equal(stored.ToString("D"), Header(fromJob, ForwardActingUserHandler.HeaderName));
            Assert.Equal("مهمة خلفية", ForwardActingUserHandler.DecodeName(Header(fromJob, ForwardActingUserHandler.NameHeaderName)));

            var signedInWins = await SendAsync(new DefaultHttpContext { User = user });
            Assert.Equal(Requester.ToString("D"), Header(signedInWins, ForwardActingUserHandler.HeaderName));
        }

        Assert.Null(ActingUserScope.Current);
        Assert.False((await SendAsync(null)).Headers.Contains(ForwardActingUserHandler.HeaderName));
    }

    [Fact]
    public async Task The_trigger_sends_the_new_body_with_a_key_a_subject_and_no_requester()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(AssetApprovalStarter.HttpClientName, c => c.BaseAddress = new Uri("http://hr-core-web:8080/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddDbContext<AdminLogisticsDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<AssetApprovalStarter>();
        await using var provider = services.BuildServiceProvider();
        var starter = provider.CreateScope().ServiceProvider.GetRequiredService<AssetApprovalStarter>();
        var (assignment, asset, employee) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await starter.StartAsync(
            new AssetAssignment { Id = assignment, AssetId = asset, EmployeeId = employee },
            new Asset { Id = asset, TagNumber = "12345678901", IsPrivileged = true, Value = 15000.5m });

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal($"AssetAssignment:{assignment:D}", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal(employee, root.GetProperty("subjectEmployeeId").GetGuid());
        Assert.Equal($"/ui/admin-logistics/Assets/Details/{asset:D}", root.GetProperty("detailsUrl").GetString());
        Assert.DoesNotContain("12345678901", root.GetProperty("displayTitle").GetString());
        Assert.Equal("15000.5", root.GetProperty("facts").GetProperty("value").GetString());
        Assert.Equal(asset.ToString("D"), root.GetProperty("facts").GetProperty("assetId").GetString());
        Assert.DoesNotContain(root.EnumerateObject(), p => p.Name.Contains("requested", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_outcome_events_carry_the_instance_id_from_the_instance_and_version_two()
    {
        var publish = new Mock<IPublishEndpoint>();
        object? sent = null;
        publish.Setup(p => p.Publish<IAssetAssignmentApproved>(It.IsAny<object>(), It.IsAny<IPipe<PublishContext<IAssetAssignmentApproved>>>(), It.IsAny<CancellationToken>()))
            .Callback<object, IPipe<PublishContext<IAssetAssignmentApproved>>, CancellationToken>((m, _, _) => sent = m)
            .Returns(Task.CompletedTask);
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var forged = Guid.NewGuid();
        var instance = new WorkflowInstance
        {
            EntityType = "AssetAssignment",
            EntityId = Guid.NewGuid(),
            ContextJson = JsonSerializer.Serialize(new { assetId = Guid.NewGuid(), employeeId = Guid.NewGuid(), workflowInstanceId = forged })
        };

        await WorkflowAssetOutcomes.PublishOutcomeAsync(db, publish.Object, instance, approved: true, reason: null, actor: null);

        Assert.NotNull(sent);
        Assert.Equal(instance.Id, sent!.GetType().GetProperty("WorkflowInstanceId")!.GetValue(sent));
        Assert.Equal(2, sent.GetType().GetProperty("SchemaVersion")!.GetValue(sent));
    }

    [Fact]
    public async Task An_assignment_outcome_applies_only_to_the_linked_instance_and_takes_the_asset_from_the_row()
    {
        var linked = Guid.NewGuid();
        await using var db = await LogisticsDbAsync(linked);
        var row = await db.AssetAssignments.AsNoTracking().SingleAsync();
        var consumer = new AssetAssignmentApprovedConsumer(db, NullLogger<AssetAssignmentApprovedConsumer>.Instance);

        await consumer.Consume(Context<IAssetAssignmentApproved>(new AssignmentApproved(row.Id, Guid.NewGuid(), Guid.NewGuid(), 2)));
        await consumer.Consume(Context<IAssetAssignmentApproved>(new AssignmentApproved(row.Id, Guid.NewGuid(), null, 2)));
        await consumer.Consume(Context<IAssetAssignmentApproved>(new AssignmentApproved(row.Id, Guid.NewGuid(), linked, 1)));
        Assert.Equal(AssetAssignmentStatus.PendingApproval, (await db.AssetAssignments.AsNoTracking().SingleAsync()).Status);

        await consumer.Consume(Context<IAssetAssignmentApproved>(new AssignmentApproved(row.Id, Guid.NewGuid(), linked, 2)));

        Assert.Equal(AssetAssignmentStatus.Active, (await db.AssetAssignments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(AssetStatus.InUse, (await db.Assets.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_rejection_applies_only_to_the_linked_instance()
    {
        var linked = Guid.NewGuid();
        await using var db = await LogisticsDbAsync(linked);
        var row = await db.AssetAssignments.AsNoTracking().SingleAsync();
        var consumer = new AssetAssignmentRejectedConsumer(db, NullLogger<AssetAssignmentRejectedConsumer>.Instance);

        await consumer.Consume(Context<IAssetAssignmentRejected>(new AssignmentRejected(row.Id, Guid.NewGuid(), 2)));
        Assert.Equal(AssetAssignmentStatus.PendingApproval, (await db.AssetAssignments.AsNoTracking().SingleAsync()).Status);

        await consumer.Consume(Context<IAssetAssignmentRejected>(new AssignmentRejected(row.Id, linked, 2)));
        Assert.Equal(AssetAssignmentStatus.Rejected, (await db.AssetAssignments.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_record_with_no_linked_instance_takes_no_outcome()
    {
        await using var db = await LogisticsDbAsync(linked: null);
        var row = await db.AssetAssignments.AsNoTracking().SingleAsync();
        var consumer = new AssetAssignmentApprovedConsumer(db, NullLogger<AssetAssignmentApprovedConsumer>.Instance);

        await consumer.Consume(Context<IAssetAssignmentApproved>(new AssignmentApproved(row.Id, row.AssetId, null, 2)));

        Assert.Equal(AssetAssignmentStatus.PendingApproval, (await db.AssetAssignments.AsNoTracking().SingleAsync()).Status);
    }

    private static IAuthorizationService Authorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static async Task<HttpRequestMessage> SendAsync(HttpContext? context)
    {
        var recorder = new RecordingHandler();
        var handler = new ForwardActingUserHandler(new HttpContextAccessor { HttpContext = context }) { InnerHandler = recorder };
        using var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://hr-core-web:8080/api/workflow-instances");
        (await invoker.SendAsync(request, CancellationToken.None)).Dispose();
        return request;
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private static ConsumeContext<T> Context<T>(T message) where T : class =>
        Mock.Of<ConsumeContext<T>>(c => c.Message == message && c.CancellationToken == CancellationToken.None);

    private static async Task<AdminLogisticsDbContext> LogisticsDbAsync(Guid? linked)
    {
        var db = new AdminLogisticsDbContext(new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var asset = new Asset { TagNumber = "TAG-1", Name = "حاسوب", Status = AssetStatus.InStorage };
        db.Assets.Add(asset);
        db.AssetAssignments.Add(new AssetAssignment
        {
            AssetId = asset.Id,
            EmployeeId = Guid.NewGuid(),
            Status = AssetAssignmentStatus.PendingApproval,
            WorkflowInstanceId = linked
        });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed record AssignmentApproved(Guid AssignmentId, Guid AssetId, Guid? WorkflowInstanceId, int SchemaVersion) : IAssetAssignmentApproved
    {
        public Guid EmployeeId => Guid.Empty;
        public DateTime OccurredAt => DateTime.UtcNow;
        public string SourceSystem => "test";
    }

    private sealed record AssignmentRejected(Guid AssignmentId, Guid? WorkflowInstanceId, int SchemaVersion) : IAssetAssignmentRejected
    {
        public Guid AssetId => Guid.Empty;
        public string? Reason => null;
        public DateTime OccurredAt => DateTime.UtcNow;
        public string SourceSystem => "test";
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"" + Guid.NewGuid() + "\"}") };
        }
    }
}
