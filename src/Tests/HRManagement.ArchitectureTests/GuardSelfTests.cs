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

    // --- PLAN-67: the interface text guard (one planted defect per rule, and the same view fixed) ---

    private static IReadOnlyList<string> UiRules(string view, string path = "src/Services/HRCore/HR.Core.Web/Views/Planted/Index.cshtml") =>
        InterfaceTextScanner.Scan(path, view).Select(f => f.Rule).ToList();

    [Theory]
    [InlineData("<table><thead><tr><th>Status</th></tr></thead></table>", "<table><thead><tr><th>الحالة</th></tr></thead></table>")]
    [InlineData("<dl><dt>Domain</dt><dd>x</dd></dl>", "<dl><dt>المجال</dt><dd>x</dd></dl>")]
    [InlineData("<button type=\"submit\">Save</button>", "<button type=\"submit\">حفظ</button>")]
    [InlineData("<h3>مصالحة الموازنة vs المناصب</h3>", "<h3>مصالحة الموازنة مقابل المناصب</h3>")]
    [InlineData("<label>رمز الصنف</label><label>Stock code</label>", "<label>رمز الصنف (SKU)</label>")]
    public void An_english_label_is_found_and_its_arabic_form_passes(string planted, string fixedView)
    {
        Assert.Contains(InterfaceTextScanner.EnglishLabel, UiRules(planted));
        Assert.DoesNotContain(InterfaceTextScanner.EnglishLabel, UiRules(fixedView));
    }

    public sealed class PlantedLabelModel
    {
        public int PlannedCount { get; set; }

        [System.ComponentModel.DataAnnotations.Display(Name = "العدد المخطط")]
        public int ArabicCount { get; set; }

        [System.ComponentModel.DataAnnotations.Display(Name = "Planned count")]
        public int EnglishCount { get; set; }
    }

    [Theory]
    [InlineData("<label asp-for=\"PlannedCount\" class=\"form-label\"></label>", true)]
    [InlineData("<label asp-for=\"EnglishCount\"></label>", true)]
    [InlineData("<th>@Html.DisplayNameFor(m => m.PlannedCount)</th>", true)]
    [InlineData("<label asp-for=\"ArabicCount\"></label>", false)]
    [InlineData("<label asp-for=\"PlannedCount\">العدد المخطط</label>", false)]
    public void A_label_taken_from_a_model_property_without_an_arabic_name_is_found(string view, bool flagged)
    {
        var rules = InterfaceTextScanner.Scan("src/Services/HRCore/HR.Core.Web/Views/Planted/Form.cshtml", "@model PlantedLabelModel\n" + view, modelType: typeof(PlantedLabelModel))
            .Select(f => f.Rule).ToList();
        Assert.Equal(flagged, rules.Contains(InterfaceTextScanner.EnglishLabel));
    }

    [Fact]
    public void An_internal_code_shown_as_secondary_text_is_not_part_of_the_label() =>
        Assert.Empty(UiRules("<h3>المناصب الممولة <span class=\"hr-internal-code\">EST-001</span></h3>"));

    [Theory]
    [InlineData("<span class=\"badge\">@requisition.Status</span>")]
    [InlineData("<td>@(x.Requisition?.Status.ToString() ?? \"—\")</td>")]
    [InlineData("<td>@item.ChangeType</td>")]
    [InlineData("<span title=\"@Model.Scope\">x</span>")]
    public void A_raw_status_is_found(string planted) =>
        Assert.Contains(InterfaceTextScanner.RawStatus, UiRules(planted));

    [Theory]
    [InlineData("<span class=\"badge\">@Html.HrEnumDisplay(requisition.Status)</span>")]
    [InlineData("<span data-status=\"@requisition.Status\">x</span>")]
    [InlineData("@if (Model.Status == X) { <span>نعم</span> }")]
    public void A_status_through_the_helper_or_in_an_attribute_passes(string view) =>
        Assert.DoesNotContain(InterfaceTextScanner.RawStatus, UiRules(view));

    [Theory]
    [InlineData("<code>@(publication.MakerUserId?.ToString() ?? publication.MakerRole ?? \"—\")</code>")]
    [InlineData("<td>@x.CreatedBy</td>")]
    [InlineData("<td>@c.Id.ToString()[..8]</td>")]
    [InlineData("<dd>@Model.OrgUnitId</dd>")]
    public void A_printed_id_is_found(string planted) =>
        Assert.Contains(InterfaceTextScanner.PrintedId, UiRules(planted));

    [Theory]
    [InlineData("<span data-hr-user-id=\"@x.CreatedBy\"></span>")]
    [InlineData("<a asp-action=\"Details\" asp-route-id=\"@x.Id\">عرض</a>")]
    public void An_id_in_an_attribute_passes(string view) =>
        Assert.DoesNotContain(InterfaceTextScanner.PrintedId, UiRules(view));

    [Fact]
    public void A_technical_id_is_named_in_the_guard_and_passes_only_in_its_file()
    {
        const string view = "<td><code>@row.CorrelationId</code></td>";
        Assert.DoesNotContain(InterfaceTextScanner.PrintedId, UiRules(view, "src/Gateway/HRManagement.Gateway/Views/Home/Audit.cshtml"));
        Assert.Contains(InterfaceTextScanner.PrintedId, UiRules(view));
    }

    [Fact]
    public void Raw_json_is_found_outside_the_technical_partial()
    {
        const string view = "<code>@r.WhenJson</code>";
        Assert.Contains(InterfaceTextScanner.RawJson, UiRules(view));
        Assert.DoesNotContain(InterfaceTextScanner.RawJson, UiRules(view, "src/Services/HRCore/HR.Core.Web/Views/Shared/_TechnicalJson.cshtml"));
    }

    [Theory]
    [InlineData("<form method=\"post\" onsubmit=\"return confirm('حذف؟');\"></form>")]
    [InlineData("<button type=\"submit\" onclick=\"go()\">موافقة</button>")]
    [InlineData("<a href=\"javascript:history.back()\">رجوع</a>")]
    [InlineData("<form method=\"post\"\n      onsubmit=\"return confirm('x');\">\n</form>")]
    public void An_inline_handler_is_found(string planted)
    {
        Assert.Contains(InterfaceTextScanner.InlineHandler, UiRules(planted));
        Assert.DoesNotContain(InterfaceTextScanner.InlineHandler, UiRules("<form method=\"post\" data-hr-confirm=\"حذف؟\"></form>"));
    }

    [Fact]
    public void A_style_attribute_is_found_and_a_class_passes()
    {
        Assert.Contains(InterfaceTextScanner.StyleAttribute, UiRules("<div class=\"progress-bar\" style=\"width:@(pct)%\"></div>"));
        Assert.DoesNotContain(InterfaceTextScanner.StyleAttribute, UiRules("<div class=\"progress-bar\" data-hr-progress=\"@pct\"></div>"));
    }

    [Fact]
    public void A_looped_table_without_an_empty_message_is_found_and_one_with_it_passes()
    {
        const string planted = "<table class=\"table\"><tbody>\n@foreach (var x in Model.Rows)\n{\n<tr><td>@x.Name</td></tr>\n}\n</tbody></table>";
        Assert.Contains(InterfaceTextScanner.EmptyTable, UiRules(planted));
        var fixedView = "@if (Model.Rows.Count == 0)\n{\n<partial name=\"_HrEmptyState\" />\n}\nelse\n{\n" + planted + "\n}";
        Assert.DoesNotContain(InterfaceTextScanner.EmptyTable, UiRules(fixedView));
    }

    [Theory]
    [InlineData("<td>@x.CreatedAt.ToString(\"yyyy/MM/dd HH:mm\")</td>")]
    [InlineData("<td>@(x.ClosingDate?.ToString(\"dd-MM-yyyy\") ?? \"—\")</td>")]
    [InlineData("<input type=\"date\" value=\"@x.Date.ToString(\"yyyy-MM-dd\")\" />")]
    [InlineData("<span>@publication.PublishedAt.ToString(\"u\", inv)</span>")]
    [InlineData("<td>@x.CreatedAtUtc</td>")]
    [InlineData("<td>@l.OccurredAt.ToLocalTime()</td>")]
    [InlineData("<input type=\"datetime-local\" name=\"at\" value=\"@x.At.ToString(\"yyyy-MM-ddTHH:mm\")\" />")]
    public void A_time_outside_the_helpers_is_found(string planted) =>
        Assert.Contains(InterfaceTextScanner.UnformattedTime, UiRules(planted));

    [Theory]
    [InlineData("<td>@Html.HrDateTime(x.CreatedAt)</td>")]
    [InlineData("<td>@Html.HrDate(x.ClosingDate)</td>")]
    [InlineData("<input type=\"date\" value=\"@Html.HrDateInputValue(x.Date)\" />")]
    [InlineData("<td>@x.Amount.ToString(\"N2\")</td>")]
    public void A_time_through_the_helpers_or_a_number_passes(string view) =>
        Assert.DoesNotContain(InterfaceTextScanner.UnformattedTime, UiRules(view));

    [Fact]
    public void An_interface_row_needs_its_owner_and_a_stale_row_is_caught()
    {
        var owner = CeilingList.Owners[0];
        var list = CeilingList.Parse("ui-text-allowlist/HR.Core.Web.txt",
        [
            $"src/x.cshtml#raw-status#@x.Status | {owner}: the status gets its Arabic name",
            $"src/y.cshtml#printed-id#@y.CreatedBy | {owner}: the user's name is shown",
            "src/z.cshtml#style-attribute#style=\"x\" | no owner",
        ]);
        Assert.Single(list.Malformed);
        Assert.Equal(["src/y.cshtml#printed-id#@y.CreatedBy"], CeilingChecks.Stale(["src/x.cshtml#raw-status#@x.Status"], list));
        Assert.Equal(["src/w.cshtml#raw-json#@w.ThenJson"], CeilingChecks.Unlisted(["src/x.cshtml#raw-status#@x.Status", "src/w.cshtml#raw-json#@w.ThenJson"], list));
    }
}
