using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Workflow;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-WF-43 (PLAN-43) on the prod-like stack: definitions are governed, the owner starts with the real requester, and the three
/// rules hold in a real custody approval. The negatives that cannot be reached live (a service account approving, an unlinked
/// instance) are in <c>Wf043GovernanceIntegrationTests</c>.
/// </summary>
[RequiredAcceptance]
public class AcWf43DefinitionGovernanceTests
{
    private const string Ac = "AC-WF-43";
    private const string Code = "ESTABLISHMENT-CHANGE-APPROVAL";
    private const string ProofOfHandover = "AC-WF-43-EVIDENCE";
    private const int ActiveAssignment = 1;
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(60);

    private static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    private static Task<string> Sql(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    private static async Task<string> Scalar(string database, string sql) => (await Sql(database, sql)).Trim();

    private static object Steps(params (int Level, string Name, string Role)[] steps) =>
        steps.Select(s => new WorkflowStepSnapshot { Level = s.Level, Name = s.Name, RoleRequired = s.Role }).ToArray();

    private static object ValidDraft(DateTime? validTo = null) => new
    {
        Code,
        Name = "اعتماد تغيير ملاك",
        DomainEntityType = "EstablishmentChangeRequest",
        Description = "AC-WF-43",
        // The national establishment rule, as seeded: other classes run on the version published here.
        RequireDistinctApprovers = false,
        ValidTo = validTo,
        Steps = Steps((1, "مراجعة HR", HrManagementRoles.HrCore), (2, "اعتماد إداري", HrManagementRoles.HrManagementApprover))
    };

    /// <summary>An admin client that holds the antiforgery cookie, and the header every definition write needs.</summary>
    private static async Task<HttpClient> AdminAsync(string persona)
    {
        var http = await AcS2s42TrustBoundaryTests.UiClientAsync(persona);
        var token = await http.GetAsync("api/hr-core/api/workflow-definitions/antiforgery");
        var body = await token.Content.ReadAsStringAsync();
        Assert.True(token.IsSuccessStatusCode, $"antiforgery {(int)token.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        http.DefaultRequestHeaders.Add(doc.RootElement.GetProperty("headerName").GetString()!, doc.RootElement.GetProperty("requestToken").GetString());
        return http;
    }

    private static async Task<Guid> CreateDraftAsync(HttpClient http, object draft)
    {
        var response = await http.PostAsJsonAsync("api/hr-core/api/workflow-definitions", draft);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"create {(int)response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> TransitionAsync(HttpClient http, Guid id, string action) =>
        http.PostAsync($"api/hr-core/api/workflow-definitions/{id:D}/{action}", null);

    private static async Task<Guid> PublishNewVersionAsync(EvidenceRecorder r, DateTime? validTo = null)
    {
        using var author = await AdminAsync("p-workflow-admin");
        using var publisher = await AdminAsync("p-workflow-admin-2");
        var id = await CreateDraftAsync(author, ValidDraft(validTo));
        var submit = await TransitionAsync(author, id, "submit");
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        var publish = await TransitionAsync(publisher, id, "publish");
        var body = await publish.Content.ReadAsStringAsync();
        r.CaptureHttp("publish-by-second-person", "POST", $"/api/hr-core/api/workflow-definitions/{id:D}/publish", (int)publish.StatusCode, body);
        Assert.True(publish.IsSuccessStatusCode, body);
        return id;
    }

    private static Task<string> DefinitionStatusAsync(Guid id) =>
        Scalar("hr_core_db", $"SELECT \"Status\" FROM \"WorkflowDefinitions\" WHERE \"Id\"='{id:D}';");

    private static Task<string> DefinitionVersionAsync(Guid id) =>
        Scalar("hr_core_db", $"SELECT \"Version\" FROM \"WorkflowDefinitions\" WHERE \"Id\"='{id:D}';");

    private static Task<string> InstanceVersionOfChangeAsync(Guid change) => Scalar("hr_core_db",
        $"SELECT i.\"DefinitionVersion\" FROM \"WorkflowInstances\" i JOIN \"EstablishmentChangeRequests\" c ON c.\"WorkflowInstanceId\"=i.\"Id\" WHERE c.\"Id\"='{change:D}';");

    private static async Task<Guid> PersonaIdAsync(string persona)
    {
        var (token, _) = await AcceptanceHarness.LoginAsync(AcceptanceHarness.RequirePersona(persona));
        return Guid.Parse(AcceptanceHarness.ParseJwt(token).Subject);
    }

    /// <summary>A privileged asset handed over through the UI by <paramref name="persona"/>: pending, with its instance.</summary>
    private static async Task<(Guid Asset, Guid Assignment, Guid Instance)> PrivilegedHandoverAsync(string persona, Guid employee)
    {
        var asset = await AcS2s42TrustBoundaryTests.InsertAssetAsync("حواسيب", privileged: true);
        using var http = await AcS2s42TrustBoundaryTests.UiClientAsync(persona);
        var (_, page) = await AcS2s42TrustBoundaryTests.AssignAsync(http, asset, employee, ProofOfHandover);

        var row = await Scalar("admin_db", $"SELECT \"Id\" || ',' || coalesce(\"WorkflowInstanceId\"::text,'') FROM \"AssetAssignments\" WHERE \"AssetId\"='{asset:D}';");
        Assert.True(row.Contains(','), AcS2s42TrustBoundaryTests.Alerts(page));
        var parts = row.Split(',');
        Assert.True(Guid.TryParse(parts[1], out var instance), $"The assignment has no workflow instance: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        return (asset, Guid.Parse(parts[0]), instance);
    }

    /// <summary>
    /// A pending assignment started the way Admin.Logistics starts one: on the service identity with the requester in X-Acting-User.
    /// The UI cannot do this for a branch manager, whose scope guard needs an open holder that an asset in storage does not have.
    /// </summary>
    private static async Task<(Guid Asset, Guid Assignment, Guid Instance)> HandoverRequestedByAsync(string persona, Guid employee, EvidenceRecorder r)
    {
        var asset = await AcS2s42TrustBoundaryTests.InsertAssetAsync("حواسيب", privileged: true);
        var assignment = Guid.NewGuid();
        using var logistics = await AcceptanceHarness.CreateServiceClientAsync(
            ServiceClientIds.AdminLogistics, ServiceScopes.HrCoreWorkflow, r, AcceptanceSeedIds.AgencyAId, await PersonaIdAsync(persona));

        var response = await logistics.PostAsJsonAsync("api/workflow-instances", new WorkflowApprovalStartRequest(
            "AssetAssignment",
            assignment,
            "ASSET-ASSIGN-APPROVAL",
            $"AssetAssignment:{assignment:D}",
            [],
            null,
            "AC-WF-43e",
            $"/ui/admin-logistics/Assets/Details/{asset:D}",
            new Dictionary<string, string> { ["assetId"] = asset.ToString("D"), ["employeeId"] = employee.ToString("D") },
            employee));
        var body = await response.Content.ReadAsStringAsync();
        r.CaptureHttp("start-as-owner", "POST", "/api/workflow-instances", (int)response.StatusCode, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var doc = JsonDocument.Parse(body);
        var instance = doc.RootElement.GetProperty("id").GetGuid();

        await Sql("admin_db", $"""
            INSERT INTO "AssetAssignments" ("Id","AssetId","EmployeeId","AssignedAt","Status","TenantId","CreatedAt","UpdatedAt","WorkflowInstanceId")
            VALUES ('{assignment:D}','{asset:D}','{employee:D}',now(),0,'{AcceptanceSeedIds.AgencyAId:D}',now(),now(),'{instance:D}');
            SELECT 1;
            """);
        return (asset, assignment, instance);
    }

    private static async Task<(HttpStatusCode Status, string Body)> ActAsync(string persona, Guid instance, string action, int level)
    {
        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D"));
        using var response = await http.PostAsJsonAsync(
            $"api/hr-core/api/workflow-instances/{instance:D}/{action}", new { Comments = "AC-WF-43", TargetStepLevel = level });
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<long> AssignmentStatusAsync(Guid assignment) =>
        await AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("admin_db", $"SELECT \"Status\" FROM \"AssetAssignments\" WHERE \"Id\"='{assignment:D}';");

    private static async Task<long> WaitForActiveAsync(Guid assignment)
    {
        var deadline = DateTime.UtcNow + DeliveryWindow;
        var status = await AssignmentStatusAsync(assignment);
        while (status != ActiveAssignment && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            status = await AssignmentStatusAsync(assignment);
        }

        return status;
    }

    // ------------------------------------------------------------------------------------------------ 43a

    [SkippableFact]
    public async Task Ac43a_Workflow_admin_publishes_with_a_second_person_and_hr_core_writes_nothing()
    {
        var r = Recorder("43a", "A draft by one workflow-admin, published by another; hr-core refused", "p-workflow-admin");
        AcceptanceGate.RequireEnabled(r);

        var id = await PublishNewVersionAsync(r);

        Assert.Equal("Published", await DefinitionStatusAsync(id));
        var version = await DefinitionVersionAsync(id);
        using var reader = await AdminAsync("p-workflow-admin-2");
        var list = await (await reader.GetAsync($"api/hr-core/api/workflow-definitions?entityType=EstablishmentChangeRequest&publishedOnly=true")).Content.ReadAsStringAsync();
        Assert.Contains(id.ToString("D"), list, StringComparison.OrdinalIgnoreCase);

        var rowsBefore = await Scalar("hr_core_db", "SELECT count(*) FROM \"WorkflowDefinitions\";");
        using var central = await AcS2s42TrustBoundaryTests.UiClientAsync("central-a");
        var refused = await central.PostAsJsonAsync("api/hr-core/api/workflow-definitions", ValidDraft());
        r.CaptureHttp("hr-core-create-negative", "POST", "/api/hr-core/api/workflow-definitions", (int)refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(rowsBefore, await Scalar("hr_core_db", "SELECT count(*) FROM \"WorkflowDefinitions\";"));

        r.WriteScenarioDbChecks(new { id, version, status = "Published", rowsBefore });
        r.WriteScenarioSteps(
            "1. p-workflow-admin creates and submits a two-step draft of ESTABLISHMENT-CHANGE-APPROVAL; p-workflow-admin-2 publishes it: Published, and it is in the list.\n" +
            "2. Negative: central-a (hr-core) posts a definition: 403, and the table has no new row.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 43b

    [SkippableFact]
    public async Task Ac43b_An_invalid_draft_is_refused_with_its_errors_and_the_author_cannot_publish()
    {
        var r = Recorder("43b", "The validator at submit, and the two-person rule at publish", "p-workflow-admin");
        AcceptanceGate.RequireEnabled(r);
        using var author = await AdminAsync("p-workflow-admin");

        var invalid = await CreateDraftAsync(author, new
        {
            Code,
            Name = "مسودة ناقصة",
            DomainEntityType = "EstablishmentChangeRequest",
            Steps = Steps((1, "خطوة", ""))
        });
        var refused = await TransitionAsync(author, invalid, "submit");
        var errors = await refused.Content.ReadAsStringAsync();
        r.CaptureHttp("submit-invalid-negative", "POST", $"/api/hr-core/api/workflow-definitions/{invalid:D}/submit", (int)refused.StatusCode, errors);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("الخطوة 1 بلا دور.", errors, StringComparison.Ordinal);
        Assert.Contains("هذا النوع يتطلب 2 خطوات على الأقل.", errors, StringComparison.Ordinal);
        Assert.Equal("Draft", await DefinitionStatusAsync(invalid));

        var valid = await CreateDraftAsync(author, ValidDraft());
        var submit = await TransitionAsync(author, valid, "submit");
        Assert.True(submit.IsSuccessStatusCode, await submit.Content.ReadAsStringAsync());
        var own = await TransitionAsync(author, valid, "publish");
        var ownBody = await own.Content.ReadAsStringAsync();
        r.CaptureHttp("publish-own-negative", "POST", $"/api/hr-core/api/workflow-definitions/{valid:D}/publish", (int)own.StatusCode, ownBody);
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Contains("لا يجوز لمنشئ التعريف أو مقدّمه نشره", ownBody, StringComparison.Ordinal);
        Assert.Equal("PendingPublish", await DefinitionStatusAsync(valid));

        // Nothing stays pending for another class to meet.
        Assert.True((await author.PostAsJsonAsync($"api/hr-core/api/workflow-definitions/{valid:D}/return", new { Reason = "AC-WF-43b" })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"api/hr-core/api/workflow-definitions/{valid:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"api/hr-core/api/workflow-definitions/{invalid:D}")).StatusCode);

        r.WriteScenarioDbChecks(new { invalid, valid });
        r.WriteScenarioSteps(
            "1. Negative: a one-step draft with no role: submit is 400 with the Arabic errors, and it stays a Draft.\n" +
            "2. Negative: a valid draft submitted by its author: the author's publish is 403 with the two-person message, and it stays PendingPublish.\n" +
            "3. Both drafts are returned and deleted.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 43c

    [SkippableFact]
    public async Task Ac43c_A_new_version_takes_new_starts_a_running_instance_stays_and_an_expired_one_starts_nothing()
    {
        var r = Recorder("43c", "Version pinning across a publication, and the validity window", "central-a");
        AcceptanceGate.RequireEnabled(r);
        var correlation = Guid.NewGuid().ToString("D");
        using var central = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona("central-a"), correlation);

        var running = await EstablishmentChangeFlow.SubmitSampleChangeAsync(central, r, "AC-WF-43c-1");
        var pinned = await InstanceVersionOfChangeAsync(running);

        var published = await PublishNewVersionAsync(r);
        var newVersion = await DefinitionVersionAsync(published);
        Assert.True(int.Parse(newVersion, global::System.Globalization.CultureInfo.InvariantCulture) > int.Parse(pinned, global::System.Globalization.CultureInfo.InvariantCulture));

        var afterPublish = await EstablishmentChangeFlow.SubmitSampleChangeAsync(central, r, "AC-WF-43c-2");
        Assert.Equal(newVersion, await InstanceVersionOfChangeAsync(afterPublish));

        await EstablishmentChangeFlow.ApproveBothStepsAsync(running, correlation, r);
        Assert.Equal(pinned, await InstanceVersionOfChangeAsync(running));

        var expired = await PublishNewVersionAsync(r, DateTime.UtcNow.AddDays(-1));
        var afterExpired = await EstablishmentChangeFlow.SubmitSampleChangeAsync(central, r, "AC-WF-43c-3");
        Assert.Equal(newVersion, await InstanceVersionOfChangeAsync(afterExpired));

        r.WriteScenarioDbChecks(new { running, pinned, published, newVersion, afterPublish, expired, afterExpired });
        r.WriteScenarioSteps(
            "1. An establishment change starts on the published version, and a new version is published.\n" +
            "2. A new change starts on the new version; the running one completes on the version it pinned (psql).\n" +
            "3. Negative: a published version with ValidTo in the past is not used by the next start (psql).");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 43d

    [SkippableFact]
    public async Task Ac43d_The_handover_records_the_real_requester_and_a_user_cannot_start()
    {
        var r = Recorder("43d", "A privileged handover starts through the owner, with the human requester", "a-logistics-maker");
        AcceptanceGate.RequireEnabled(r);
        var maker = await PersonaIdAsync("a-logistics-maker");

        var (_, assignment, instance) = await PrivilegedHandoverAsync("a-logistics-maker", AcceptanceSeedIds.EmpBr1Id);

        var row = await Scalar("hr_core_db",
            $"SELECT \"RequestedByUserId\" || '|' || \"InitiatedBy\" || '|' || \"SourceClientId\" || '|' || \"IdempotencyKey\" || '|' || \"SubjectEmployeeId\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';");
        Assert.Equal(
            $"{maker:D}|{maker:D}|{ServiceClientIds.AdminLogistics}|AssetAssignment:{assignment:D}|{AcceptanceSeedIds.EmpBr1Id:D}",
            row);

        using var central = await AcS2s42TrustBoundaryTests.UiClientAsync("central-a");
        var refused = await central.PostAsJsonAsync("api/hr-core/api/workflow-instances", new { EntityType = "AssetAssignment", EntityId = Guid.NewGuid() });
        r.CaptureHttp("user-start-negative", "POST", "/api/hr-core/api/workflow-instances", (int)refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        r.WriteScenarioDbChecks(new { assignment, instance, maker, row });
        r.WriteScenarioSteps(
            "1. a-logistics-maker hands over a privileged asset through the UI: the instance has RequestedByUserId and InitiatedBy of the user, SourceClientId svc-admin-logistics, the key and the subject employee (psql).\n" +
            "2. Negative: central-a posts /api/hr-core/api/workflow-instances: 403 at the gateway.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 43e

    [SkippableFact]
    public async Task Ac43e_The_requester_acts_on_no_step_and_the_outcome_reaches_the_linked_assignment()
    {
        var r = Recorder("43e", "The requester refused, the role refused, the last approval carries the instance to the assignment", "br1-mgr");
        AcceptanceGate.RequireEnabled(r);

        var (asset, assignment, instance) = await HandoverRequestedByAsync("br1-mgr", AcceptanceSeedIds.EmpBr1Id, r);

        var (requesterStatus, requesterBody) = await ActAsync("br1-mgr", instance, "approve", 1);
        r.CaptureHttp("requester-step1-negative", "POST", $"/api/hr-core/api/workflow-instances/{instance:D}/approve", (int)requesterStatus, requesterBody);
        Assert.Equal(HttpStatusCode.BadRequest, requesterStatus);
        Assert.Contains("لا يجوز لطالب الإجراء", requesterBody, StringComparison.Ordinal);

        // PLAN-45: step 1 belongs to admin-logistics, which acts on the generic page.
        var (hrCoreStatus, hrCoreBody) = await ActAsync("central-a", instance, "approve", 1);
        r.CaptureHttp("role-step1-negative", "POST", $"/api/hr-core/api/workflow-instances/{instance:D}/approve", (int)hrCoreStatus, hrCoreBody);
        Assert.Equal(HttpStatusCode.BadRequest, hrCoreStatus);
        await CustodyJourney.ApproveFromInboxAsync("a-logistics-checker", instance, r);

        var (roleStatus, roleBody) = await ActAsync("central-a", instance, "approve", 2);
        r.CaptureHttp("role-step2-negative", "POST", $"/api/hr-core/api/workflow-instances/{instance:D}/approve", (int)roleStatus, roleBody);
        Assert.Equal(HttpStatusCode.BadRequest, roleStatus);
        Assert.Contains(HrManagementRoles.HrManagementApprover, roleBody, StringComparison.Ordinal);

        var (lastStatus, lastBody) = await ActAsync("a-management-approver", instance, "approve", 2);
        r.CaptureHttp("approve-step2", "POST", $"/api/hr-core/api/workflow-instances/{instance:D}/approve", (int)lastStatus, lastBody);
        Assert.True(lastStatus == HttpStatusCode.OK, lastBody);
        Assert.Equal("Completed", await Scalar("hr_core_db", $"SELECT \"Status\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));

        // The consumer applies only an outcome that names the linked instance, so Active proves the event carried it.
        var status = await WaitForActiveAsync(assignment);
        Assert.Equal(ActiveAssignment, status);

        r.WriteScenarioDbChecks(new { asset, assignment, instance, status });
        r.WriteScenarioSteps(
            "1. Negative: br1-mgr, who requested the handover and holds hr-core, approves step 1: refused with the requester message.\n" +
            "2. Negative: central-a on step 1: refused with the role message. a-logistics-checker approves step 1 on the generic page. Negative: central-a on step 2: refused with the role message.\n" +
            "3. a-management-approver approves step 2: the instance is Completed, and within 60 s the assignment is Active, which the consumer does only for the instance id it is linked to (psql).");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 43f

    [SkippableFact]
    public async Task Ac43f_A_logistics_officer_approves_step_one_of_a_handover_someone_else_requested()
    {
        var r = Recorder("43f", "A person who is not the requester acts on step 1", "a-logistics-checker");
        AcceptanceGate.RequireEnabled(r);

        var (_, assignment, instance) = await PrivilegedHandoverAsync("a-logistics-maker", AcceptanceSeedIds.EmpBr1Id);
        await CustodyJourney.ApproveFromInboxAsync("a-logistics-checker", instance, r);

        Assert.Equal("2", await Scalar("hr_core_db", $"SELECT \"CurrentStepLevel\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance:D}';"));

        r.WriteScenarioDbChecks(new { assignment, instance, currentStep = 2 });
        r.WriteScenarioSteps("1. a-logistics-maker hands over a privileged asset; a-logistics-checker approves step 1 on the generic page, and the instance moves to step 2 (psql).");
        r.WriteResult("Pass");
    }
}
