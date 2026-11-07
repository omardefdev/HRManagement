using System.Reflection;
using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>PLAN-43 (G-WF-04): the two-person rule, the national tenant, immutability, the binding warning, retiring.</summary>
public sealed class Wf043DefinitionLifecycleTests
{
    private const string Code = "ESTABLISHMENT-CHANGE-APPROVAL";

    private static readonly WorkflowDefinitionActor Author = new(Guid.Parse("43000000-0000-4000-8000-00000000000a"), TenantDefaults.DefaultTenantId);
    private static readonly WorkflowDefinitionActor Second = new(Guid.Parse("43000000-0000-4000-8000-00000000000b"), TenantDefaults.DefaultTenantId);
    private static readonly WorkflowDefinitionActor Third = new(Guid.Parse("43000000-0000-4000-8000-00000000000c"), TenantDefaults.DefaultTenantId);

    private static HRCoreDbContext NewDb() => new(new DbContextOptionsBuilder<HRCoreDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static WorkflowDefinitionChange Valid(string code = Code) => new()
    {
        Code = code,
        Name = "اعتماد تغيير ملاك",
        DomainEntityType = "EstablishmentChangeRequest",
        Steps =
        [
            new WorkflowStepSnapshot { Level = 1, Name = "مراجعة", RoleRequired = HrManagementRoles.HrCore },
            new WorkflowStepSnapshot { Level = 2, Name = "اعتماد", RoleRequired = HrManagementRoles.HrManagementApprover }
        ]
    };

    private static async Task<WorkflowDefinition> SubmittedAsync(WorkflowDefinitionLifecycleService service)
    {
        var draft = await service.CreateDraftAsync(Valid(), Author);
        Assert.True(draft.Success);
        Assert.True((await service.SubmitForPublishAsync(draft.Definition!.Id, Author)).Success);
        return draft.Definition;
    }

    [Fact]
    public async Task A_second_person_publishes_and_the_fields_keep_the_actors()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var definition = await SubmittedAsync(service);

        var published = await service.PublishAsync(definition.Id, Second);

        Assert.True(published.Success);
        var stored = await db.WorkflowDefinitions.SingleAsync();
        Assert.Equal(WorkflowDefinitionStatus.Published, stored.Status);
        Assert.True(stored.IsPublished);
        Assert.Equal(Author.UserId, stored.CreatedBy);
        Assert.Equal(Author.UserId, stored.SubmittedBy);
        Assert.Equal(Second.UserId, stored.PublishedBy);
        Assert.NotNull(stored.PublishedAt);
        Assert.Equal(TenantDefaults.DefaultTenantId, stored.TenantId);
    }

    [Fact]
    public async Task The_author_or_the_submitter_cannot_publish()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var draft = await service.CreateDraftAsync(Valid(), Author);
        await service.SubmitForPublishAsync(draft.Definition!.Id, Second);

        foreach (var actor in new[] { Author, Second })
        {
            var refused = await service.PublishAsync(draft.Definition.Id, actor);
            Assert.Equal(WorkflowDefinitionFailure.Forbidden, refused.Failure);
            Assert.Contains(WorkflowDefinitionLifecycleService.SecondPersonMessage, refused.Errors!);
        }

        Assert.Equal(WorkflowDefinitionStatus.PendingPublish, (await db.WorkflowDefinitions.SingleAsync()).Status);
        Assert.True((await service.PublishAsync(draft.Definition.Id, Third)).Success);
    }

    [Fact]
    public async Task An_editor_of_a_colleagues_draft_cannot_publish_it_even_after_a_later_edit()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var draft = await service.CreateDraftAsync(Valid(), Author);
        await service.UpdateDraftAsync(draft.Definition!.Id, new WorkflowDefinitionChange { Name = "اسم آخر" }, Second);
        await service.UpdateDraftAsync(draft.Definition.Id, new WorkflowDefinitionChange { Description = "وصف" }, Author);
        await service.SubmitForPublishAsync(draft.Definition.Id, Author);

        var refused = await service.PublishAsync(draft.Definition.Id, Second);

        Assert.Equal(WorkflowDefinitionFailure.Forbidden, refused.Failure);
    }

    [Fact]
    public async Task Submit_refuses_with_every_error_and_the_draft_stays_a_draft()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var draft = await service.CreateDraftAsync(new WorkflowDefinitionChange
        {
            Code = Code,
            Name = "اسم",
            DomainEntityType = "EstablishmentChangeRequest",
            Steps = [new WorkflowStepSnapshot { Level = 1, Name = "خطوة", RoleRequired = "" }]
        }, Author);

        var refused = await service.SubmitForPublishAsync(draft.Definition!.Id, Author);

        Assert.Equal(WorkflowDefinitionFailure.Invalid, refused.Failure);
        Assert.Contains("الخطوة 1 بلا دور.", refused.Errors!);
        Assert.Contains("هذا النوع يتطلب 2 خطوات على الأقل.", refused.Errors!);
        Assert.Equal(WorkflowDefinitionStatus.Draft, (await db.WorkflowDefinitions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Publish_needs_a_submitted_definition()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var draft = await service.CreateDraftAsync(Valid(), Author);

        var refused = await service.PublishAsync(draft.Definition!.Id, Second);

        Assert.Contains(WorkflowDefinitionLifecycleService.NotPendingMessage, refused.Errors!);
    }

    [Fact]
    public async Task Another_tenant_is_refused_for_every_write()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var definition = await SubmittedAsync(service);
        var agency = new WorkflowDefinitionActor(Third.UserId, Guid.Parse("a0000001-0000-4000-8000-000000000001"));

        var results = new[]
        {
            await service.CreateDraftAsync(Valid(), agency),
            await service.UpdateDraftAsync(definition.Id, new WorkflowDefinitionChange(), agency),
            await service.SubmitForPublishAsync(definition.Id, agency),
            await service.PublishAsync(definition.Id, agency),
            await service.ReturnToDraftAsync(definition.Id, "سبب", agency),
            await service.RetireAsync(definition.Id, "سبب كافٍ للسحب", agency),
            await service.DeleteDraftAsync(definition.Id, agency)
        };

        Assert.All(results, r =>
        {
            Assert.Equal(WorkflowDefinitionFailure.Forbidden, r.Failure);
            Assert.Contains(WorkflowDefinitionLifecycleService.NationalOnlyMessage, r.Errors!);
        });
    }

    [Fact]
    public async Task A_published_version_is_never_edited_or_deleted()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var definition = await SubmittedAsync(service);
        await service.PublishAsync(definition.Id, Second);
        var stepsBefore = (await db.WorkflowDefinitions.AsNoTracking().SingleAsync()).StepsJson;

        var edit = await service.UpdateDraftAsync(definition.Id, new WorkflowDefinitionChange { Steps = [] }, Third);
        var delete = await service.DeleteDraftAsync(definition.Id, Third);

        Assert.Contains(WorkflowDefinitionLifecycleService.NotDraftMessage, edit.Errors!);
        Assert.Equal(WorkflowDefinitionFailure.Invalid, delete.Failure);
        Assert.Equal(stepsBefore, (await db.WorkflowDefinitions.AsNoTracking().SingleAsync()).StepsJson);
    }

    [Fact]
    public async Task A_submitted_definition_is_not_edited_until_it_is_returned()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var definition = await SubmittedAsync(service);

        Assert.False((await service.UpdateDraftAsync(definition.Id, new WorkflowDefinitionChange { Name = "x" }, Author)).Success);

        var returned = await service.ReturnToDraftAsync(definition.Id, "يحتاج مراجعة", Second);

        Assert.True(returned.Success);
        var stored = await db.WorkflowDefinitions.SingleAsync();
        Assert.Equal(WorkflowDefinitionStatus.Draft, stored.Status);
        Assert.Null(stored.SubmittedBy);
        Assert.True((await service.UpdateDraftAsync(definition.Id, new WorkflowDefinitionChange { Name = "x" }, Author)).Success);
    }

    [Fact]
    public async Task A_new_version_numbers_itself_and_defaults_to_distinct_approvers()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        await SubmittedAsync(service);

        var next = await service.CreateDraftAsync(Valid(), Third);

        Assert.Equal(2, next.Definition!.Version);
        Assert.True(next.Definition.RequireDistinctApprovers);
        Assert.False((await service.CreateDraftAsync(Valid() with { RequireDistinctApprovers = false }, Third)).Definition!.RequireDistinctApprovers);
    }

    [Fact]
    public async Task A_new_version_cannot_leave_the_entity_type_of_its_code()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        await SubmittedAsync(service);

        // AssetLossReport needs one step: a code that starts by name must not escape its limits this way.
        var weaker = await service.CreateDraftAsync(new WorkflowDefinitionChange
        {
            Code = Code,
            Name = "أضعف",
            DomainEntityType = "AssetLossReport",
            Steps = [new WorkflowStepSnapshot { Level = 1, Name = "خطوة", RoleRequired = HrManagementRoles.HrCore }]
        }, Third);

        var refused = await service.SubmitForPublishAsync(weaker.Definition!.Id, Third);

        Assert.Contains(WorkflowDefinitionLifecycleService.EntityTypeChangedMessage, refused.Errors!);
    }

    [Fact]
    public async Task A_validity_window_that_ends_before_it_starts_is_refused()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var now = DateTime.UtcNow;
        var draft = await service.CreateDraftAsync(Valid() with { ValidFrom = now, ValidTo = now.AddDays(-1) }, Author);

        var refused = await service.SubmitForPublishAsync(draft.Definition!.Id, Author);

        Assert.Contains(WorkflowDefinitionLifecycleService.ValidityMessage, refused.Errors!);
    }

    [Fact]
    public async Task Publish_warns_when_a_binding_pins_another_version()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var v1 = await SubmittedAsync(service);
        await service.PublishAsync(v1.Id, Second);
        db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Domain = "Core.Establishment",
            WorkflowCode = Code,
            WorkflowDefinitionId = v1.Id
        });
        await db.SaveChangesAsync();

        var v2 = await service.CreateDraftAsync(Valid(), Author);
        await service.SubmitForPublishAsync(v2.Definition!.Id, Author);
        var published = await service.PublishAsync(v2.Definition.Id, Second);

        Assert.True(published.Success);
        Assert.Contains(WorkflowDefinitionLifecycleService.BindingWarning(1), published.Warnings!);
    }

    [Fact]
    public async Task Retire_needs_a_reason_and_a_replacement_for_a_started_workflow()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var v1 = await SubmittedAsync(service);
        await service.PublishAsync(v1.Id, Second);

        Assert.Contains(WorkflowDefinitionLifecycleService.RetireReasonMessage, (await service.RetireAsync(v1.Id, "قصير", Third)).Errors!);
        Assert.Contains(WorkflowDefinitionLifecycleService.LastPublishedMessage, (await service.RetireAsync(v1.Id, "لم يعد مستعملاً في الجهة", Third)).Errors!);

        var v2 = await service.CreateDraftAsync(Valid(), Author);
        await service.SubmitForPublishAsync(v2.Definition!.Id, Author);
        await service.PublishAsync(v2.Definition.Id, Second);

        // The latest version cannot go: every start would fall back to the older one with no second person.
        Assert.Contains(WorkflowDefinitionLifecycleService.LastPublishedMessage, (await service.RetireAsync(v2.Definition.Id, "رجوع إلى الإصدار الأول", Third)).Errors!);

        var retired = await service.RetireAsync(v1.Id, "استُبدل بالإصدار الثاني", Third);

        Assert.True(retired.Success);
        var stored = await db.WorkflowDefinitions.SingleAsync(d => d.Id == v1.Id);
        Assert.Equal(WorkflowDefinitionStatus.Retired, stored.Status);
        Assert.False(stored.IsPublished);
        Assert.Equal(Third.UserId, stored.RetiredBy);
        Assert.Equal("استُبدل بالإصدار الثاني", stored.ChangeReason);
    }

    [Fact]
    public async Task A_version_marked_published_by_an_older_tag_is_not_a_draft()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);
        var draft = await service.CreateDraftAsync(Valid(), Author);
        var stored = await db.WorkflowDefinitions.SingleAsync();
        stored.IsPublished = true;   // what the previous tag's Publish leaves after a rollback
        await db.SaveChangesAsync();

        Assert.False((await service.UpdateDraftAsync(draft.Definition!.Id, new WorkflowDefinitionChange { Name = "x" }, Second)).Success);
        Assert.False((await service.DeleteDraftAsync(draft.Definition.Id, Second)).Success);
    }

    [Fact]
    public async Task A_person_must_be_named()
    {
        await using var db = NewDb();
        var service = new WorkflowDefinitionLifecycleService(db);

        var refused = await service.CreateDraftAsync(Valid(), new WorkflowDefinitionActor(null, TenantDefaults.DefaultTenantId));

        Assert.Equal(WorkflowDefinitionFailure.Forbidden, refused.Failure);
    }

    [Fact]
    public async Task The_seed_marks_status_and_only_custody_requires_distinct_approvers()
    {
        await using var db = NewDb();
        await WorkflowPackSeeder.SeedAsync(db);

        var all = await db.WorkflowDefinitions.AsNoTracking().ToListAsync();
        Assert.All(all.Where(d => d.IsPublished), d => Assert.Equal(WorkflowDefinitionStatus.Published, d.Status));
        Assert.Equal(WorkflowDefinitionStatus.Retired, all.Single(d => d.Code == "RECRUITMENT-HIRE").Status);
        Assert.Equal(["ASSET-ASSIGN-APPROVAL", "ASSET-LOSS-APPROVAL"], all.Where(d => d.RequireDistinctApprovers).Select(d => d.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_seed_patches_rows_older_than_the_governance_columns_and_leaves_a_draft_alone()
    {
        await using var db = NewDb();
        await WorkflowPackSeeder.SeedAsync(db);
        foreach (var definition in await db.WorkflowDefinitions.ToListAsync())
        {
            definition.Status = WorkflowDefinitionStatus.Draft;
            definition.RequireDistinctApprovers = false;
        }

        var draft = await new WorkflowDefinitionLifecycleService(db).CreateDraftAsync(Valid("NEW-CODE") with { RequireDistinctApprovers = false }, Author);
        await db.SaveChangesAsync();

        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.SeedAsync(db);

        var all = await db.WorkflowDefinitions.AsNoTracking().ToListAsync();
        Assert.All(all.Where(d => d.IsPublished), d => Assert.Equal(WorkflowDefinitionStatus.Published, d.Status));
        Assert.Equal(WorkflowDefinitionStatus.Retired, all.Single(d => d.Code == "RECRUITMENT-HIRE").Status);
        Assert.True(all.Single(d => d.Code == "ASSET-ASSIGN-APPROVAL").RequireDistinctApprovers);
        var untouched = all.Single(d => d.Id == draft.Definition!.Id);
        Assert.Equal(WorkflowDefinitionStatus.Draft, untouched.Status);
        Assert.False(untouched.RequireDistinctApprovers);
    }

    [Fact]
    public async Task EnsureVersion_publishes_once_and_retires_older_versions()
    {
        await using var db = NewDb();
        await WorkflowPackSeeder.SeedAsync(db);
        const string code = "ASSET-LOSS-APPROVAL";
        WorkflowStepSnapshot[] target =
        [
            new() { Level = 1, Name = "لوجستية", RoleRequired = HrManagementRoles.AdminLogistics },
            new() { Level = 2, Name = "إدارية", RoleRequired = HrManagementRoles.HrManagementApprover }
        ];

        Assert.True(await WorkflowPackSeeder.EnsureVersionAsync(db, code, target, "PLAN-45"));
        Assert.False(await WorkflowPackSeeder.EnsureVersionAsync(db, code, target, "PLAN-45"));

        var versions = await db.WorkflowDefinitions.AsNoTracking().Where(d => d.Code == code).OrderBy(d => d.Version).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(WorkflowDefinitionStatus.Retired, versions[0].Status);
        Assert.False(versions[0].IsPublished);
        Assert.Equal(WorkflowDefinitionStatus.Published, versions[1].Status);
        Assert.Null(versions[1].PublishedBy);
        Assert.Equal("seed:PLAN-45", versions[1].ChangeReason);
        Assert.Equal(2, JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(versions[1].StepsJson)!.Count);
    }

    [Fact]
    public async Task The_starter_skips_a_version_outside_its_window_also_through_a_binding()
    {
        await using var db = NewDb();
        var now = DateTime.UtcNow;
        var v1 = Published(1, validTo: null);
        var expired = Published(2, validTo: now.AddDays(-1));
        var future = Published(3, validFrom: now.AddDays(1));
        db.WorkflowDefinitions.AddRange(v1, expired, future);
        db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Domain = "D",
            WorkflowCode = Code,
            WorkflowDefinitionId = expired.Id
        });
        await db.SaveChangesAsync();
        var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);

        var byCode = await starter.StartByWorkflowCodeAsync(Code, "EstablishmentChangeRequest", Guid.NewGuid(), TenantDefaults.DefaultTenantId, null, null);
        var byBinding = await starter.StartByWorkflowCodeAsync(Code, "EstablishmentChangeRequest", Guid.NewGuid(), TenantDefaults.DefaultTenantId, null, null, policyDomain: "D");

        Assert.Equal(1, (await db.WorkflowInstances.SingleAsync(i => i.Id == byCode)).DefinitionVersion);
        Assert.Equal(1, (await db.WorkflowInstances.SingleAsync(i => i.Id == byBinding)).DefinitionVersion);
    }

    [Fact]
    public async Task The_health_report_lists_invalid_definitions_and_never_throws()
    {
        await using var db = NewDb();
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);

        var invalid = await WorkflowDefinitionHealthReport.RunAsync(db, NullLogger.Instance);

        Assert.Contains(invalid, d => d.Code == "ID-CARD-ISSUANCE");
        Assert.Contains(invalid, d => d.Code == "IT-TICKET");
        Assert.DoesNotContain(invalid, d => d.Code == "ASSET-ASSIGN-APPROVAL");
        Assert.DoesNotContain(invalid, d => d.Code == "ASSET-LOSS-APPROVAL");
    }

    [Fact]
    public void Every_definition_action_that_is_not_a_read_needs_the_admin_policy_and_antiforgery()
    {
        var actions = typeof(WorkflowDefinitionsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any())
            .ToList();
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var verbs = action.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().SelectMany(a => a.HttpMethods);
            if (verbs.All(v => v == "GET") && action.Name != nameof(WorkflowDefinitionsController.Antiforgery))
                continue;

            Assert.Contains(
                action.GetCustomAttributes<AuthorizeAttribute>(),
                a => a.Policy == HrManagementPolicies.WorkflowDefinitionAdmin);
            if (verbs.Any(v => v != "GET"))
                Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }

    private static WorkflowDefinition Published(int version, DateTime? validTo = null, DateTime? validFrom = null) => new()
    {
        TenantId = TenantDefaults.DefaultTenantId,
        Code = Code,
        Name = "اعتماد",
        Version = version,
        DomainEntityType = "EstablishmentChangeRequest",
        IsPublished = true,
        Status = WorkflowDefinitionStatus.Published,
        ValidFrom = validFrom,
        ValidTo = validTo,
        StepsJson = JsonSerializer.Serialize(new[] { new WorkflowStepSnapshot { Level = 1, Name = "خطوة", RoleRequired = HrManagementRoles.HrCore } })
    };
}
