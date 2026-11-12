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

    public sealed class PlantedBindingController : ControllerBase
    {
        [HttpPost]
        public IActionResult Create(SampleEntity entity) => Ok(entity);

        [HttpPost]
        public IActionResult Edit(SampleViewModel model) => Ok(model);
    }

    [Fact]
    public void An_action_that_binds_an_entity_is_an_offender_and_a_view_model_is_not() =>
        Assert.Equal(["PlantedBinding.Create"], MassAssignmentArchitectureTests.Offenders(Read("hr-core", typeof(PlantedBindingController))).ToList());
}
