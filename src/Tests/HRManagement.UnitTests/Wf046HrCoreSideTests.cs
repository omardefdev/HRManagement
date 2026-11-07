using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HR.Core.Web.Services;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-46 (section 2): the registry, the outcome event, the status for the owner only, and the reach of <c>idcard</c> and <c>compliance</c>:
/// the inbox and the approval page, nothing else of HR.Core.
/// </summary>
public sealed class Wf046HrCoreSideTests
{
    private static readonly Guid Requester = Guid.Parse("46000000-0000-4000-8000-0000000000aa");
    private static readonly Guid Reviewer = Guid.Parse("46000000-0000-4000-8000-0000000000bb");

    private static readonly ExternalApprovalCaller IdCardService = new(true, ServiceClientIds.IdCard, Requester.ToString("D"), "a-idcard");

    // The registry.

    [Theory]
    [InlineData("CardRequest", ServiceClientIds.IdCard, HrManagementRoles.IdCard, "/ui/idcard/", "ID-CARD-ISSUANCE")]
    [InlineData("DisciplinaryAction", ServiceClientIds.Compliance, HrManagementRoles.Compliance, "/ui/compliance/", "DISCIPLINARY-APPROVAL")]
    [InlineData("SeparationRecord", ServiceClientIds.HrContracts, HrManagementRoles.Compliance, "/ui/hr-contracts/", "SEPARATION-APPROVAL", "SEPARATION-APPROVAL-SENSITIVE")]
    public void The_three_types_are_registered_with_their_owner(
        string entityType, string owner, string audience, string prefix, params string[] codes)
    {
        var type = ExternalApprovalTypes.Find(entityType)!;

        Assert.Equal(owner, type.OwnerClientId);
        Assert.Equal(ExternalOutcomeEvent.Generic, type.OutcomeEvent);
        Assert.Equal([audience], type.AudienceRoles);
        Assert.Equal(prefix, type.DetailsUrlPrefix);
        Assert.Equal(codes, type.AllowedWorkflowCodes);
        Assert.Equal(codes[0], type.DefaultWorkflowCode);
        Assert.Contains(entityType, WorkflowInstanceActionService.HandledEntityTypes);
    }

    [Fact]
    public void The_custody_types_keep_their_own_events() =>
        Assert.All(["AssetAssignment", "AssetLossReport"], t => Assert.False(ExternalApprovalTypes.IsGeneric(t)));

    // The reach of the two roles.

    [Theory]
    [InlineData(HrManagementRoles.IdCard, HrManagementPolicies.WorkflowStepAction, true)]
    [InlineData(HrManagementRoles.IdCard, HrManagementPolicies.HrCoreWorkInbox, true)]
    [InlineData(HrManagementRoles.Compliance, HrManagementPolicies.WorkflowStepAction, true)]
    [InlineData(HrManagementRoles.Compliance, HrManagementPolicies.HrCoreWorkInbox, true)]
    [InlineData(HrManagementRoles.IdCard, HrManagementPolicies.ModuleHrCore, false)]
    [InlineData(HrManagementRoles.IdCard, HrManagementPolicies.HrCoreReadOnly, false)]
    [InlineData(HrManagementRoles.IdCard, HrManagementPolicies.HrCoreWrite, false)]
    [InlineData(HrManagementRoles.IdCard, HrManagementPolicies.HrCoreApprove, false)]
    [InlineData(HrManagementRoles.IdCard, HrManagementPolicies.WorkflowExternalStart, false)]
    [InlineData(HrManagementRoles.Compliance, HrManagementPolicies.ModuleHrCore, false)]
    [InlineData(HrManagementRoles.Compliance, HrManagementPolicies.HrCoreReadOnly, false)]
    [InlineData(HrManagementRoles.Compliance, HrManagementPolicies.HrCoreWrite, false)]
    [InlineData(HrManagementRoles.Compliance, HrManagementPolicies.HrCoreApprove, false)]
    [InlineData(HrManagementRoles.Compliance, HrManagementPolicies.WorkflowExternalStart, false)]
    public async Task The_card_office_and_compliance_reach_the_inbox_and_the_steps_only(string role, string policy, bool allowed) =>
        Assert.Equal(allowed, (await Authorization().AuthorizeAsync(User(role), policy)).Succeeded);

    [Theory]
    [InlineData(HrManagementRoles.IdCard)]
    [InlineData(HrManagementRoles.Compliance)]
    public async Task The_employee_list_is_refused_to_both(string role)
    {
        // The gateway puts the employee list behind HrCoreReadOnly; neither role passes it, so the answer is 403.
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(
            "hr-core", "GET", new PathString("/ui/hr-core/Employees"), out var policy));
        Assert.Equal(HrManagementPolicies.HrCoreReadOnly, policy);
        Assert.False((await Authorization().AuthorizeAsync(User(role), policy!)).Succeeded);
    }

    [Fact]
    public async Task A_service_account_never_acts_on_a_step()
    {
        var service = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("azp", ServiceClientIds.IdCard),
            new Claim("client_id", ServiceClientIds.IdCard),
            new Claim(ClaimTypes.Role, HrManagementRoles.IdCard)
        ], "test"));

        Assert.False((await Authorization().AuthorizeAsync(service, HrManagementPolicies.WorkflowStepAction)).Succeeded
            && ServiceCallerPrincipal.IsServiceAccount(service));
    }

    [Fact]
    public void The_card_office_sees_its_type_and_compliance_its_two()
    {
        Assert.Equal(["CardRequest"], WorkInboxAudience.EntityTypesFor(User(HrManagementRoles.IdCard)));
        Assert.Equal(["DisciplinaryAction", "SeparationRecord"], WorkInboxAudience.EntityTypesFor(User(HrManagementRoles.Compliance)));
        Assert.True(WorkInboxAudience.IsAudienceOnly(User(HrManagementRoles.IdCard)));
        Assert.False(WorkInboxAudience.IsAudienceOnly(User(HrManagementRoles.IdCard, HrManagementRoles.HrCore)));
    }

    [Fact]
    public void No_type_is_exempt_from_the_step_role()
    {
        Assert.Empty(WorkflowStepRoleOptions.DefaultExemptEntityTypes);
        Assert.All(ExternalApprovalTypes.All, t => Assert.True(new WorkflowStepRoleOptions().IsEnforced(t.EntityType), t.EntityType));
    }

    // The status, for the owner only.

    [Fact]
    public async Task The_owner_reads_the_status_and_anyone_else_reads_nothing()
    {
        await using var db = await SeededAsync();
        var instance = await StartCardAsync(db);

        var status = await new ExternalApprovalService(db, new WorkflowEngine()).GetStatusAsync(instance.Id, IdCardService);

        Assert.NotNull(status);
        Assert.Equal(WorkflowApprovalOutcomes.InProgress, status.Status);
        Assert.Equal("ID-CARD-ISSUANCE", status.WorkflowCode);
        Assert.Equal(HrManagementRoles.IdCard, status.CurrentStepRole);
        Assert.Equal($"/ui/hr-core/WorkflowApprovals/Details/{instance.Id:D}", status.ApprovalUrl);
        Assert.Equal($"CardRequest:{instance.EntityId:D}:1", status.CorrelationId);

        var service = new ExternalApprovalService(db, new WorkflowEngine());
        Assert.Null(await service.GetStatusAsync(instance.Id, IdCardService with { ClientId = ServiceClientIds.Compliance }));
        Assert.Null(await service.GetStatusAsync(instance.Id, IdCardService with { IsServiceAccount = false }));
        Assert.Null(await service.GetStatusAsync(Guid.NewGuid(), IdCardService));
    }

    [Fact]
    public async Task A_withdrawal_stages_the_cancelled_outcome_with_its_reason()
    {
        await using var db = await SeededAsync();
        var instance = await StartCardAsync(db);
        var (publish, published) = Recorder();

        var result = await new ExternalApprovalService(db, new WorkflowEngine(), publish.Object)
            .CancelAsync(instance.Id, IdCardService, "لم يعد مطلوباً");

        Assert.Equal(ExternalApprovalOutcome.Withdrawn, result.Outcome);
        var message = Assert.Single(published);
        Assert.Equal(WorkflowApprovalOutcomes.Cancelled, Read(message, "Outcome"));
        Assert.Equal("لم يعد مطلوباً", Read(message, "Reason"));
        Assert.Equal(Requester.ToString("D"), Read(message, "DecidedByUserId"));
        Assert.Equal(instance.TenantId.ToString("D"), Read(message, "TenantId"));
        Assert.Equal("Withdrawn", JsonDocument.Parse(JsonSerializer.Serialize(message)).RootElement.GetProperty("Decisions")[0].GetProperty("Action").GetString());
    }

    [Fact]
    public async Task A_withdrawal_of_a_custody_instance_stages_nothing_and_needs_no_bus()
    {
        await using var db = await SeededAsync();
        var started = await new ExternalApprovalService(db, new WorkflowEngine()).StartAsync(
            new WorkflowApprovalStartRequest("AssetLossReport", Guid.NewGuid(), "ASSET-LOSS-APPROVAL", "AssetLossReport:1", [], null,
                "فقدان أصل", "/ui/admin-logistics/Assets/Details/1", null),
            new ExternalApprovalCaller(true, ServiceClientIds.AdminLogistics, Requester.ToString("D"), null));

        var withdrawn = await new ExternalApprovalService(db, new WorkflowEngine())
            .CancelAsync(started.Instance!.Id, new ExternalApprovalCaller(true, ServiceClientIds.AdminLogistics, Requester.ToString("D"), null), "سبب");

        Assert.Equal(ExternalApprovalOutcome.Withdrawn, withdrawn.Outcome);
    }

    [Fact]
    public async Task The_last_approval_stages_the_outcome_with_every_decision()
    {
        await using var db = await SeededAsync();
        var instance = await StartCardAsync(db);
        var (publish, published) = Recorder();
        var actions = WorkflowActions(db, publish.Object);

        var approved = await actions.ApproveAsync(
            instance.Id, new WorkflowActionActor(Reviewer, "a-idcard-2", new HashSet<string> { HrManagementRoles.IdCard }), "موافق", 1);

        Assert.True(approved.Success, approved.Error);
        var message = Assert.Single(published);
        Assert.Equal(WorkflowApprovalOutcomes.Approved, Read(message, "Outcome"));
        Assert.Null(Read(message, "Reason"));
        Assert.Equal("ID-CARD-ISSUANCE", Read(message, "WorkflowCode"));
        var decision = JsonDocument.Parse(JsonSerializer.Serialize(message)).RootElement.GetProperty("Decisions")[0];
        Assert.Equal(HrManagementRoles.IdCard, decision.GetProperty("RoleRequired").GetString());
        Assert.Equal(Reviewer, decision.GetProperty("UserId").GetGuid());
    }

    [Fact]
    public async Task The_requester_cannot_approve_the_own_card()
    {
        await using var db = await SeededAsync();
        var instance = await StartCardAsync(db);
        var (publish, published) = Recorder();

        var refused = await WorkflowActions(db, publish.Object).ApproveAsync(
            instance.Id, new WorkflowActionActor(Requester, "a-idcard", new HashSet<string> { HrManagementRoles.IdCard }), "موافق", 1);

        Assert.False(refused.Success);
        Assert.Empty(published);
    }

    [Fact]
    public async Task A_rejection_stages_the_reason()
    {
        await using var db = await SeededAsync();
        var instance = await StartCardAsync(db);
        var (publish, published) = Recorder();

        var rejected = await WorkflowActions(db, publish.Object).RejectAsync(
            instance.Id, new WorkflowActionActor(Reviewer, "a-idcard-2", new HashSet<string> { HrManagementRoles.IdCard }), "الصورة غير واضحة", targetStepLevel: 1);

        Assert.True(rejected.Success, rejected.Error);
        Assert.Equal(WorkflowApprovalOutcomes.Rejected, Read(Assert.Single(published), "Outcome"));
        Assert.Equal("الصورة غير واضحة", Read(published[0], "Reason"));
    }

    // Helpers.

    private static async Task<HRCoreDbContext> SeededAsync()
    {
        var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);
        return db;
    }

    private static async Task<WorkflowInstance> StartCardAsync(HRCoreDbContext db)
    {
        var entityId = Guid.NewGuid();
        var started = await new ExternalApprovalService(db, new WorkflowEngine()).StartAsync(
            new WorkflowApprovalStartRequest("CardRequest", entityId, "ID-CARD-ISSUANCE", $"CardRequest:{entityId:D}:1", [Requester], null,
                "بطاقة تعريف", $"/ui/idcard/CardRequests/Details/{entityId:D}", null, Guid.NewGuid()),
            IdCardService);
        Assert.Equal(ExternalApprovalOutcome.Started, started.Outcome);
        Assert.Equal(TenantDefaults.DefaultTenantId, started.Instance!.TenantId);
        return started.Instance!;
    }

    private static WorkflowInstanceActionService WorkflowActions(HRCoreDbContext db, IPublishEndpoint publish) =>
        new(db, new WorkflowEngine(), Mock.Of<IWorkflowSignatureService>(), publish, null!, null!, null!, null!, null!);

    private static (Mock<IPublishEndpoint> Mock, List<object> Published) Recorder()
    {
        var published = new List<object>();
        var publish = new Mock<IPublishEndpoint>();
        publish.Setup(p => p.Publish<IWorkflowOutcomeDecided>(
                It.IsAny<object>(), It.IsAny<IPipe<PublishContext<IWorkflowOutcomeDecided>>>(), It.IsAny<CancellationToken>()))
            .Callback<object, IPipe<PublishContext<IWorkflowOutcomeDecided>>, CancellationToken>((values, _, _) => published.Add(values))
            .Returns(Task.CompletedTask);
        // The callback overload is an extension that ends in the pipe overload above.
        return (publish, published);
    }

    private static string? Read(object message, string name)
    {
        var value = JsonDocument.Parse(JsonSerializer.Serialize(message)).RootElement.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.ToString();
    }

    private static IAuthorizationService Authorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r)).Prepend(new Claim(ClaimTypes.NameIdentifier, Reviewer.ToString("D"))), "test"));
}
