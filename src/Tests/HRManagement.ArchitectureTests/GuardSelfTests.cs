using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.ArchitectureTests;

/// <summary>PLAN-51 (P1): each guard of this project fails on a planted defect, and passes the same controller once it is fixed.</summary>
public sealed class GuardSelfTests
{
    private static IReadOnlyList<ActionInfo> Read(string service, Type controller) => ActionCatalog.ReadController(service, controller).ToList();

    // --- writes ---

    public sealed class PlantedWriteController : ControllerBase
    {
        [HttpGet]
        public IActionResult Index() => Ok();

        [HttpPost]
        public IActionResult Create() => Ok();

        [HttpDelete]
        [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
        public IActionResult Delete() => Ok();

        [HttpPut]
        [AllowAnonymous]
        public IActionResult Public() => Ok();

        [AcceptVerbs("PATCH")]
        public IActionResult Patch() => Ok();
    }

    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public sealed class FixedWriteController : ControllerBase
    {
        [HttpPost]
        public IActionResult Create() => Ok();
    }

    [Fact]
    public void A_write_without_a_policy_is_an_offender_and_a_read_a_policy_or_anonymous_is_not()
    {
        var offenders = WriteActionsHaveExplicitPolicyTests.Offenders(Read("hr-core", typeof(PlantedWriteController))).ToList();
        Assert.Equal(["PlantedWrite.Create", "PlantedWrite.Patch"], offenders);
        Assert.Empty(WriteActionsHaveExplicitPolicyTests.Offenders(Read("hr-core", typeof(FixedWriteController))));
    }

    [Fact]
    public void A_list_misses_an_unlisted_offender_keeps_no_stale_row_and_rejects_an_unsigned_one()
    {
        var list = CeilingList.Parse("x.txt",
        [
            "# comment",
            "PlantedWrite.Create | PLAN-53: owned",
            "Gone.Action | IAM-F: owned",
            "NoReason.Action",
            "Unsigned.Action | to save time",
        ]);

        Assert.Equal(["PlantedWrite.Patch"], CeilingChecks.Unlisted(["PlantedWrite.Create", "PlantedWrite.Patch"], list));
        Assert.Equal(["Gone.Action"], CeilingChecks.Stale(["PlantedWrite.Create"], list));
        Assert.Equal(2, list.Malformed.Count);
    }

    [Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
    public sealed class ReaderClassWriteController : ControllerBase
    {
        [HttpPost]
        public IActionResult Create() => Ok();

        [HttpPost]
        [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
        public IActionResult Edit() => Ok();
    }

    [Fact]
    public void A_write_under_a_read_policy_alone_is_caught()
    {
        // Review M1: the class's read policy counts as "explicit", yet the management approver could write through it.
        Assert.Equal(["hr-contracts:ReaderClassWrite.Create"],
            WriteActionsHaveExplicitPolicyTests.ReadOnlyWrites(Read("hr-contracts", typeof(ReaderClassWriteController))).ToList());
    }

    // --- sensitive actions ---

    public sealed class PlantedSensitiveController : ControllerBase
    {
        [HttpPost]
        [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
        public IActionResult Approve() => Ok();

        [HttpPost]
        [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
        public IActionResult Reject() => Ok();

        [HttpPost]
        public IActionResult ReleaseHold() => Ok();

        [HttpGet]
        public IActionResult Revoke() => Ok();

        [HttpPost]
        [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
        public IActionResult RunReport() => Ok();
    }

    public sealed class AccessPermissionsController : ControllerBase
    {
        [HttpPost]
        [Authorize(Policy = HrManagementPolicies.ModuleIdCard)]
        public IActionResult Create() => Ok();
    }

    [Fact]
    public void A_sensitive_write_without_an_approval_policy_is_an_offender()
    {
        Assert.Equal(
            ["hr-core:PlantedSensitive.Approve", "hr-core:PlantedSensitive.ReleaseHold"],
            SensitiveActionsHaveApprovalPolicyTests.Offenders(Read("hr-core", typeof(PlantedSensitiveController))).ToList());
        Assert.Equal(
            ["idcard:AccessPermissions.Create"],
            SensitiveActionsHaveApprovalPolicyTests.Offenders(Read("idcard", typeof(AccessPermissionsController))).ToList());
    }

    // --- internal routes ---

    [Route("api/internal/sample")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public sealed class UserPolicyInternalController : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    [Route("api/internal/sample")]
    [Authorize]
    public sealed class NoPolicyInternalController : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    [Route("api/internal/sample")]
    [Authorize(Policy = ServicePolicies.CodeGenerate)]
    public sealed class AnonymousInternalController : ControllerBase
    {
        [HttpGet("x")]
        [AllowAnonymous]
        public IActionResult Get() => Ok();
    }

    public sealed class ActionRouteInternalController : ControllerBase
    {
        [HttpPost("api/internal/sample/y")]
        [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
        public IActionResult Post() => Ok();
    }

    [Route("api/internal/sample")]
    [Authorize(Policy = ServicePolicies.CodeGenerate)]
    public sealed class ServiceInternalController : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    [Route("api/sample")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public sealed class PublicRouteController : ControllerBase
    {
        [HttpGet("x")]
        public IActionResult Get() => Ok();
    }

    [Theory]
    [InlineData(typeof(UserPolicyInternalController))]
    [InlineData(typeof(NoPolicyInternalController))]
    [InlineData(typeof(AnonymousInternalController))]
    [InlineData(typeof(ActionRouteInternalController))]
    public void An_internal_route_that_lets_a_user_in_fails_the_guard(Type sample) =>
        Assert.NotEmpty(InternalRoutesAreServiceOnlyTests.Violations(Read("hr-core", sample)));

    [Theory]
    [InlineData(typeof(ServiceInternalController))]
    [InlineData(typeof(PublicRouteController))]
    public void A_service_route_and_a_public_route_pass(Type sample) =>
        Assert.Empty(InternalRoutesAreServiceOnlyTests.Violations(Read("hr-core", sample)));

    // --- overrides ---

    [ModulePolicyOverride(HrManagementPolicies.HrCoreWorkInbox)]
    public sealed class ClassOverrideWriteController : ControllerBase
    {
        [HttpPost]
        public IActionResult Create() => Ok();

        [HttpPost]
        [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
        public IActionResult Edit() => Ok();
    }

    public sealed class ActionOverrideWriteController : ControllerBase
    {
        [HttpPost]
        [ModulePolicyOverride(HrManagementPolicies.HrCoreReadOnly)]
        [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
        public IActionResult Create() => Ok();

        [HttpPost]
        [ModulePolicyOverride(HrManagementPolicies.HrMovementsApprovalAccess)]
        [Authorize(Policy = HrManagementPolicies.HrManagementApprove)]
        public IActionResult Approve() => Ok();

        [HttpGet]
        [ModulePolicyOverride(HrManagementPolicies.HrCoreReadOnly)]
        public IActionResult Index() => Ok();
    }

    [Fact]
    public void An_override_that_authorizes_a_write_by_itself_fails_the_guard()
    {
        var classViolations = ModuleOverridesAreDeclaredTests.Violations(Read("hr-core", typeof(ClassOverrideWriteController)));
        Assert.Single(classViolations);
        Assert.Contains("ClassOverrideWrite.Create", classViolations[0], StringComparison.Ordinal);

        var actionViolations = ModuleOverridesAreDeclaredTests.Violations(Read("hr-core", typeof(ActionOverrideWriteController)));
        Assert.Single(actionViolations);
        Assert.Contains("ActionOverrideWrite.Create", actionViolations[0], StringComparison.Ordinal);
    }

    // --- entity bindings ---

    public sealed class SampleEntity : BaseEntity;

    public sealed record SampleViewModel(string Name);

    public sealed class SampleWrapper
    {
        public SampleEntity? Item { get; set; }
    }

    public sealed class PlantedBindingController : ControllerBase
    {
        [HttpPost]
        public IActionResult Create(SampleEntity entity) => Ok(entity);

        [HttpPost]
        public IActionResult Edit(SampleViewModel model) => Ok(model);

        [HttpPost]
        public IActionResult Bulk(List<SampleEntity> items) => Ok(items);

        [HttpPost]
        public IActionResult Wrapped(SampleWrapper wrapper) => Ok(wrapper);
    }

    [Fact]
    public void An_action_that_binds_an_entity_is_an_offender_and_a_view_model_is_not() =>
        Assert.Equal(["PlantedBinding.Bulk", "PlantedBinding.Create", "PlantedBinding.Wrapped"],
            MassAssignmentArchitectureTests.Offenders(Read("hr-core", typeof(PlantedBindingController))).Order(StringComparer.Ordinal).ToList());

    // --- PLAN-52 (§6, P-11): the org-scope stance and the ids of a scoped action ---

    public sealed class PlantedRecord
    {
        public Guid Id { get; set; }

        public Guid? OrgUnitId { get; set; }
    }

    public sealed class PlantedScopeDb(Microsoft.EntityFrameworkCore.DbContextOptions<PlantedScopeDb> options)
        : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        public Microsoft.EntityFrameworkCore.DbSet<PlantedRecord> Records => Set<PlantedRecord>();
    }

    public sealed class PlantedForm
    {
        public Guid OwnerId { get; set; }

        public List<Guid> MemberIds { get; set; } = [];

        [NotOrgScopeKey("a reference type")]
        public Guid TypeId { get; set; }
    }

    public sealed class PlantedNoStanceController : ControllerBase
    {
        [HttpGet]
        public IActionResult Index() => Ok();
    }

    [OrgScoped(typeof(PlantedRecord))]
    public sealed class PlantedScopedController : ControllerBase
    {
        [HttpGet]
        public IActionResult Details(Guid id) => Ok(id);

        [HttpPost]
        public IActionResult Assign(Guid id, Guid employeeId) => Ok(employeeId);

        [HttpPost]
        public IActionResult Bulk(List<Guid> recordIds) => Ok(recordIds);

        [HttpPost]
        public IActionResult Save(Guid id, PlantedForm form) => Ok(form);
    }

    [OrgScopeNotApplicable("a page with no org records")]
    public sealed class PlantedDeclaredController : ControllerBase
    {
        [HttpGet]
        public IActionResult Index(Guid anyId) => Ok(anyId);
    }

    private static OrgScopeKeyRegistry PlantedRegistry(bool complete)
    {
        var registry = new OrgScopeKeyRegistry("hr-core").Key<PlantedScopeDb, PlantedRecord>(_ => r => r.OrgUnitId);
        if (complete)
            registry.Id("employeeId", typeof(OrgScopeUnit)).Id("recordId", typeof(PlantedRecord)).Id("ownerId", typeof(OrgScopeUnit))
                .Id("memberId", typeof(OrgScopeUnit));
        return registry;
    }

    [Fact]
    public void A_controller_without_a_stance_is_an_offender_and_a_declared_one_is_not() =>
        Assert.Equal(["PlantedNoStance"],
            OrgScopeCoverageTests.Undeclared("hr-core", [typeof(PlantedNoStanceController), typeof(PlantedDeclaredController), typeof(PlantedScopedController)]));

    [Fact]
    public void An_id_a_list_of_ids_and_a_model_id_without_a_key_are_offenders_and_pass_once_keyed()
    {
        var offenders = OrgScopeCoverageTests.UnkeyedIds("hr-core", [typeof(PlantedScopedController)], PlantedRegistry(complete: false)).ToList();
        Assert.Contains(offenders, o => o.StartsWith("PlantedScoped.Assign(employeeId)", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.StartsWith("PlantedScoped.Bulk(recordIds)", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.StartsWith("PlantedScoped.Save(form.OwnerId)", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.StartsWith("PlantedScoped.Save(form.MemberIds)", StringComparison.Ordinal));
        Assert.DoesNotContain(offenders, o => o.Contains("TypeId", StringComparison.Ordinal));
        Assert.Equal(4, offenders.Count);
        Assert.Empty(OrgScopeCoverageTests.UnkeyedIds("hr-core", [typeof(PlantedScopedController)], PlantedRegistry(complete: true)));
    }

    [Fact]
    public void A_stance_naming_an_entity_without_a_key_is_an_offender() =>
        Assert.Contains(OrgScopeCoverageTests.UnkeyedIds("hr-core", [typeof(PlantedScopedController)], new OrgScopeKeyRegistry("hr-core")),
            o => o.Contains("the stance names PlantedRecord", StringComparison.Ordinal));

    [Fact]
    public void An_org_scope_row_needs_an_owner_and_a_new_or_stale_row_is_caught()
    {
        var list = CeilingList.Parse("org-scope.txt",
        [
            "PlantedNoStance | IAM-H: latent, no restricted role reaches it (D-IAM-3)",
            "PlantedDeclared | IAM-H: latent",
            "Unsigned | latent, no restricted role reaches it",
        ]);
        Assert.Single(list.Malformed);
        var undeclared = OrgScopeCoverageTests.Undeclared("hr-core",
            [typeof(PlantedNoStanceController), typeof(PlantedDeclaredController), typeof(PlantedScopedController)]).ToList();
        Assert.Equal(["PlantedDeclared"], CeilingChecks.Stale(undeclared, list));
        Assert.Empty(CeilingChecks.Unlisted(undeclared, list));
        // A row above the ceiling: the list holds two valid rows where its ceiling allows one.
        Assert.True(list.Rows.Count > 1);
    }
}
