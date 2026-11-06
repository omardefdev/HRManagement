using System.Net;
using System.Net.Http.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-43 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password
/// is read from the server's .env into memory only. Evidence goes under <c>artifacts/plan43/vps</c>: it is never committed. The
/// recruitment check leaves one approved requisition named «PLAN-43 server check» on the demo server.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Wf43Marker = "PLAN-43 server check";

    private static EvidenceRecorder StartWf43(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"wf43-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan43", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check runs against the demo server only.");
        return recorder;
    }

    [SkippableFact]
    public void AC_WF_43_vps_definitions_are_backfilled_reported_and_the_realm_has_the_role()
    {
        var recorder = StartWf43("vps-definitions", "Demo server: the backfill matches the before-picture, the health report names the invalid definitions, the realm has workflow-admin");

        var byStatus = Sql("hr_core_db", "select \"Status\", \"IsPublished\", count(*) from \"WorkflowDefinitions\" group by 1, 2 order by 1, 2");
        Assert.Equal(["Published|t|16", "Retired|f|1"], byStatus);
        Assert.Equal(["ASSET-ASSIGN-APPROVAL"], Sql("hr_core_db", "select \"Code\" from \"WorkflowDefinitions\" where \"RequireDistinctApprovers\""));

        var (code, logs) = Sh($"docker logs {ContainerOf("hr-core-web")} 2>&1 | grep -a 'fails validation' | grep -ao 'definition [A-Z-]* v[0-9]*' | sort -u");
        Assert.True(code == 0, logs);
        Assert.Contains("ID-CARD-ISSUANCE", logs, StringComparison.Ordinal);
        Assert.Contains("IT-TICKET", logs, StringComparison.Ordinal);
        Assert.Equal("healthy", Sh($"docker inspect -f '{{{{.State.Health.Status}}}}' {ContainerOf("hr-core-web")}").Output.Trim());

        var role = Sql("keycloak_db",
            "select count(*) from keycloak_role k join realm r on r.id = k.realm_id where r.name = 'hrmanagement' and k.name = 'workflow-admin' and not k.client_role").Single();
        Assert.Equal("1", role);

        recorder.WriteScenarioDbChecks(new { byStatus, invalid = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), role });
        recorder.WriteScenarioSteps("psql: 16 Published and 1 Retired, distinct approvers on ASSET-ASSIGN-APPROVAL alone; the start-up log names the invalid definitions and hr-core-web is healthy; the realm holds workflow-admin.");
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_WF_43_vps_a_user_cannot_start_and_a_recruitment_approval_still_works()
    {
        var recorder = StartWf43("vps-tour", "Demo server: a user token is refused on the start, and e2e-checker-mgmt approves a requisition");
        var password = DemoPassword();
        const string creatorName = "hradmin";
        const string checkerName = "e2e-checker-mgmt";

        var position = Sql("hr_core_db",
            "select p.\"Id\", p.\"OrgUnitId\" from \"FundedPositions\" p where p.\"Status\" = 'Vacant' and not exists " +
            "(select 1 from \"RecruitmentRequisitions\" r where r.\"FundedPositionId\" = p.\"Id\" and r.\"Status\" in ('Draft', 'PendingApproval', 'Approved', 'Published')) " +
            "order by p.\"PositionCode\" limit 1");
        AcceptanceGate.RequireData(recorder, position.Length == 1, "لا وظيفة شاغرة", "No vacant funded position without a requisition.");
        var (positionId, orgUnit) = (position[0].Split('|')[0], position[0].Split('|')[1]);

        var (creator, _) = await SignInAsync(creatorName, password);
        string requisition;
        using (creator)
        {
            var start = await creator.PostAsJsonAsync($"https://{AppDomain}/api/hr-core/api/workflow-instances", new { EntityType = "AssetAssignment", EntityId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);

            const string createPage = "/ui/hr-core/RecruitmentRequisitions/Create";
            var (status, _, body) = await PostFormAsync(creator, createPage, new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(creator, createPage),
                ["ScopeOrgUnitId"] = orgUnit,
                ["FundedPositionId"] = positionId,
                ["JobTitle"] = Wf43Marker,
                ["JustificationNotes"] = Wf43Marker,
                ["LegalSourceId"] = "SYR-LAW-50-2004",
                ["BudgetYear"] = DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["PublishingChannel"] = "PublicOnly"
            });
            var rows = Sql("hr_core_db", $"select \"Id\" from \"RecruitmentRequisitions\" where \"FundedPositionId\" = '{positionId}' and \"Status\" = 'Draft'");
            Assert.True(rows.Length == 1, $"create answered {status}: {body[..Math.Min(body.Length, 300)]}");
            requisition = rows[0];

            var details = $"/ui/hr-core/RecruitmentRequisitions/Details/{requisition}";
            var criteria = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(creator, details),
                ["RequisitionId"] = requisition,
                ["Title"] = Wf43Marker
            };
            (string Key, string Name, string Weight, string Stage)[] items =
                [("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "30", "Experience")];
            for (var i = 0; i < items.Length; i++)
            {
                var row = $"r{i}";
                criteria[$"Items[{row}].Key"] = items[i].Key;
                criteria[$"Items[{row}].NameAr"] = items[i].Name;
                criteria[$"Items[{row}].Weight"] = items[i].Weight;
                criteria[$"Items[{row}].MaxScore"] = "100";
                criteria[$"Items[{row}].Stage"] = items[i].Stage;
                criteria[$"Items[{row}].IsMandatory"] = "true";
            }

            var form = criteria.Select(kv => kv).Concat(items.Select((_, i) => new KeyValuePair<string, string>("Items.Index", $"r{i}")));
            using var saved = await creator.PostAsync($"https://{AppDomain}/ui/hr-core/RecruitmentAssessmentCriteria/Create", new FormUrlEncodedContent(form));
            Assert.True(saved.IsSuccessStatusCode, $"criteria answered {(int)saved.StatusCode}");

            await PostFormAsync(creator, $"/ui/hr-core/RecruitmentRequisitions/Submit/{requisition}", new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(creator, details)
            });
        }

        Assert.Equal("PendingApproval", Sql("hr_core_db", $"select \"Status\" from \"RecruitmentRequisitions\" where \"Id\" = '{requisition}'").Single());

        var (checker, _) = await SignInAsync(checkerName, password);
        using (checker)
        {
            var details = $"/ui/hr-core/RecruitmentRequisitions/Details/{requisition}";
            foreach (var step in new[] { 1, 2 })
            {
                await PostFormAsync(checker, $"/ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{requisition}", new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(checker, details),
                    ["RequisitionId"] = requisition,
                    ["Comments"] = $"{Wf43Marker} {step}"
                });
            }
        }

        var state = Sql("hr_core_db",
            $"select r.\"Status\", i.\"Status\", i.\"RequestedByUserId\" is null from \"RecruitmentRequisitions\" r join \"WorkflowInstances\" i on i.\"Id\" = r.\"WorkflowInstanceId\" where r.\"Id\" = '{requisition}'").Single();
        Assert.Equal("Approved|Completed|t", state);

        recorder.WriteScenarioDbChecks(new { requisition, positionId, state, userStart = 403 });
        recorder.WriteScenarioSteps(
            "1. hradmin posts /api/hr-core/api/workflow-instances through the edge: 403.\n" +
            "2. hradmin creates and submits a requisition; e2e-checker-mgmt approves steps 1 and 2: Approved, the instance Completed, with no requester (psql).");
        recorder.WriteResult("Pass");
    }
}
