using System.Globalization;
using System.Net;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-50 (P7) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. The demo users' password is
/// read from the server's .env into memory only; evidence goes under <c>artifacts/plan50/vps</c> and is never committed. The checks leave
/// on the server, each marked «PLAN-50 server check»: the transfers they create (approved, then cancelled by their creator) and the
/// delegations they create (revoked at the end). <c>e2e-delegate-hr</c> is the demo user this plan adds (hr-core and an employee id, no
/// approver role) with <c>scripts/vps/add-demo-user.sh --employee-id</c>.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Iam50Marker = "PLAN-50 server check";
    private const string Iam50Approver = "e2e-checker-mgmt";
    private const string Iam50Creator = "e2e-movements-ops";
    private const string Iam50Delegate = "e2e-delegate-hr";
    private static readonly TimeSpan Iam50Cache = TimeSpan.FromSeconds(75);

    private static readonly string[] Iam50Lists =
        ["TransferRequests", "SecondmentRequests", "LoanAssignmentRequests", "PromotionRequests", "DelegationRequests"];

    private static EvidenceRecorder StartIam50(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"iam50-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan50", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    private static async Task<int> Iam50GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"https://{AppDomain}{path}");
        return (int)response.StatusCode;
    }

    private static string Iam50Transfer(string reason) =>
        Sql("hr_movements_db", $"select \"Id\" from \"TransferRequests\" where \"Reason\" = '{reason}' order by \"CreatedAt\" desc limit 1").Single();

    /// <summary>The creator files a transfer marked for this run; its id.</summary>
    private static async Task<string> Iam50CreateTransferAsync(HttpClient creator, string reason)
    {
        var units = Sql("hr_core_db", "select \"Id\" from \"OrgUnits\" order by \"Code\" limit 2");
        var employee = Sql("hr_core_db", "select \"Id\" from \"Employees\" where \"CurrentStatus\"=0 order by \"NationalId\", \"Id\" offset 3 limit 1").Single();
        var day = DateTime.UtcNow.Date.AddDays(60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (status, path, _) = await PostFormAsync(creator, "/ui/hr-movements/TransferRequests/Create", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(creator, "/ui/hr-movements/TransferRequests/Create"),
            ["EmployeeId"] = employee,
            ["FromOrgUnitId"] = units[0],
            ["ToOrgUnitId"] = units[^1],
            ["RequestDate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["EffectiveDate"] = day,
            ["Reason"] = reason
        });
        Assert.True(status == 200, $"transfer create answered {status} at {path}");
        var id = Iam50Transfer(reason);
        Assert.True(Guid.TryParse(id, out _), "the transfer was not created");
        return id;
    }

    private static async Task Iam50CancelAsync(HttpClient creator, string transferId)
    {
        var (status, path, _) = await PostFormAsync(creator, $"/ui/hr-movements/TransferRequests/Cancel/{transferId}", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(creator, "/ui/hr-movements/TransferRequests/Create"),
            ["reason"] = Iam50Marker
        });
        Assert.True(status == 200, $"transfer cancel answered {status} at {path}");
    }

    [SkippableFact]
    public async Task AC_IAM_50_vps_the_migrations_ran_and_the_old_rows_are_revoked()
    {
        var recorder = StartIam50("migrations", "Demo server: HCM050 and MOV050 are recorded, WorkflowDelegations is gone, no delegation in force lacks a role");
        var hcm = Sql("hr_core_db", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%HCM050_ApprovalDelegationScope'").Single();
        var mov = Sql("hr_movements_db", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%MOV050_ApprovalOnBehalfOf'").Single();
        var table = Sql("hr_core_db", "select count(*) from pg_tables where tablename = 'WorkflowDelegations'").Single();
        var unscoped = Sql("hr_core_db", "select count(*) from \"ApprovalDelegations\" where not \"IsRevoked\" and (\"RoleScope\" is null or trim(\"RoleScope\") = '' or lower(trim(\"RoleScope\")) <> 'hr-management-approver')").Single();
        var revokedByMigration = Sql("hr_core_db", "select count(*) from \"ApprovalDelegations\" where \"RevokedReason\" = 'PLAN-50 migration'").Single();
        var checks = Sql("hr_core_db", "select count(*) from pg_constraint where conname in ('CK_ApprovalDelegations_ActiveHasRole', 'CK_ApprovalDelegations_HasDelegate')").Single();
        Assert.Equal(["1", "1", "0", "0", "2"], [hcm, mov, table, unscoped, checks]);
        recorder.WriteScenarioDbChecks(new { hcm050 = hcm, mov050 = mov, workflowDelegationsTables = table, inForceWithoutDelegableRole = unscoped, revokedByMigration, checks });
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_50_vps_the_approver_reaches_the_movements_and_the_creator_role_does_not_approve()
    {
        var recorder = StartIam50("reach", "Demo server: the approver opens the five lists (200), is refused on create (403); the creator is refused on approve (403); a transfer is created, approved by the approver, then cancelled by its creator");
        var password = DemoPassword();
        var approverId = KeycloakUserId(Iam50Approver);
        var (approver, _) = await SignInAsync(Iam50Approver, password, "/ui/hr-movements/");
        var (creator, _) = await SignInAsync(Iam50Creator, password, "/ui/hr-movements/");
        using (approver)
        using (creator)
        {
            var lists = new Dictionary<string, int>();
            foreach (var list in Iam50Lists)
                lists[list] = await Iam50GetAsync(approver, $"/ui/hr-movements/{list}");
            Assert.All(lists.Values, status => Assert.Equal(200, status));

            var reason = $"{Iam50Marker} reach {Guid.NewGuid():N}"[..44];
            var transfer = await Iam50CreateTransferAsync(creator, reason);
            try
            {
                var approverToken = await TokenAsync(approver, $"/ui/hr-movements/TransferRequests/Details/{transfer}");
                var (create, _, _) = await PostFormAsync(approver, "/ui/hr-movements/TransferRequests/Create",
                    new Dictionary<string, string> { ["__RequestVerificationToken"] = approverToken, ["Reason"] = Iam50Marker });
                Assert.Equal(403, create);

                var (creatorApproves, _, _) = await PostFormAsync(creator, $"/ui/hr-movements/TransferRequests/Approve/{transfer}",
                    new Dictionary<string, string> { ["__RequestVerificationToken"] = await TokenAsync(creator, "/ui/hr-movements/TransferRequests/Create") });
                Assert.Equal(403, creatorApproves);
                Assert.Equal("0", Sql("hr_movements_db", $"select \"Status\" from \"TransferRequests\" where \"Id\" = '{transfer}'").Single());

                var (approve, path, _) = await PostFormAsync(approver, $"/ui/hr-movements/TransferRequests/Approve/{transfer}",
                    new Dictionary<string, string> { ["__RequestVerificationToken"] = approverToken });
                Assert.True(approve == 200, $"approve answered {approve} at {path}");
                Assert.Equal($"1|{approverId}|", Sql("hr_movements_db",
                    $"select t.\"Status\", a.\"ApprovedBy\", coalesce(a.\"OnBehalfOfUserId\"::text, '') from \"TransferRequests\" t join \"TransferApprovals\" a on a.\"TransferRequestId\" = t.\"Id\" where t.\"Id\" = '{transfer}'").Single());
                recorder.WriteScenarioDbChecks(new { lists, transfer, create, creatorApproves, approvedBy = approverId });
            }
            finally
            {
                await Iam50CancelAsync(creator, transfer);
            }

            Assert.Equal("t", Sql("hr_movements_db", $"select \"IsCancelled\" from \"TransferRequests\" where \"Id\" = '{transfer}'").Single());
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_50_vps_a_delegate_approves_a_transfer_on_behalf_and_is_refused_after_the_revocation()
    {
        var recorder = StartIam50("delegation", "Demo server: the approver delegates his role to e2e-delegate-hr for one day; both see it; the delegate opens the transfers list and approves a transfer on behalf; after the revocation and the cache time the list is 403; the internal endpoint refuses a user");
        var password = DemoPassword();
        var principalId = KeycloakUserId(Iam50Approver);
        var delegateId = KeycloakUserId(Iam50Delegate);
        var delegateEmployee = Sql("keycloak_db",
            "select a.value from user_attribute a join user_entity u on u.id = a.user_id join realm r on r.id = u.realm_id " +
            $"where r.name = 'hrmanagement' and u.username = '{Iam50Delegate}' and a.name = 'employee_id'").SingleOrDefault() ?? "";
        AcceptanceGate.RequireData(recorder, Guid.TryParse(delegateEmployee, out _), "المستخدم المفوَّض بلا employee_id",
            $"{Iam50Delegate} has no employee_id: add it with scripts/vps/add-demo-user.sh --employee-id (PLAN-50 P-13).");

        var (principal, _) = await SignInAsync(Iam50Approver, password);
        var (creator, _) = await SignInAsync(Iam50Creator, password, "/ui/hr-movements/");
        var (delegateUser, _) = await SignInAsync(Iam50Delegate, password);
        using (principal)
        using (creator)
        using (delegateUser)
        {
            // What an earlier run left in force is revoked first.
            foreach (var left in Sql("hr_core_db", $"select \"Id\" from \"ApprovalDelegations\" where \"PrincipalId\" = '{principalId}' and \"Reason\" like '{Iam50Marker}%' and not \"IsRevoked\""))
                await PostFormAsync(principal, $"/ui/hr-core/Delegations/Revoke/{left}", new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(principal, "/ui/hr-core/Delegations/Create"),
                    ["reason"] = Iam50Marker
                });

            var now = DateTime.UtcNow.AddMinutes(-2);
            var (created, createdPath, _) = await PostFormAsync(principal, "/ui/hr-core/Delegations/Create", new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(principal, "/ui/hr-core/Delegations/Create"),
                ["DelegateEmployeeId"] = delegateEmployee,
                ["RoleScope"] = "hr-management-approver",
                ["EntityTypeScope"] = "TransferRequest",
                ["ActiveFrom"] = now.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                ["ActiveUntil"] = now.AddDays(1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                ["Reason"] = $"{Iam50Marker} {Guid.NewGuid():N}"[..40]
            });
            Assert.True(created == 200, $"delegation create answered {created} at {createdPath}");
            var delegation = Sql("hr_core_db",
                $"select \"Id\" from \"ApprovalDelegations\" where \"PrincipalId\" = '{principalId}' and \"DelegateEmployeeId\" = '{delegateEmployee}' and not \"IsRevoked\" order by \"CreatedAt\" desc limit 1").Single();
            var transfer = "";
            int grantedAfter = -1, refusedAfter = -1, internalStatus = -1;
            try
            {
                var principalPage = await (await principal.GetAsync($"https://{AppDomain}/ui/hr-core/Delegations")).Content.ReadAsStringAsync();
                var delegatePage = await (await delegateUser.GetAsync($"https://{AppDomain}/ui/hr-core/Delegations")).Content.ReadAsStringAsync();
                Assert.Contains(delegation, principalPage, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(delegation, delegatePage, StringComparison.OrdinalIgnoreCase);

                var started = DateTime.UtcNow;
                while (await Iam50GetAsync(delegateUser, "/ui/hr-movements/TransferRequests") != 200 && DateTime.UtcNow - started < Iam50Cache)
                    await Task.Delay(TimeSpan.FromSeconds(5));
                grantedAfter = (int)(DateTime.UtcNow - started).TotalSeconds;
                Assert.Equal(200, await Iam50GetAsync(delegateUser, "/ui/hr-movements/TransferRequests"));

                transfer = await Iam50CreateTransferAsync(creator, $"{Iam50Marker} on behalf {Guid.NewGuid():N}"[..44]);
                var (approve, path, _) = await PostFormAsync(delegateUser, $"/ui/hr-movements/TransferRequests/Approve/{transfer}",
                    new Dictionary<string, string> { ["__RequestVerificationToken"] = await TokenAsync(delegateUser, $"/ui/hr-movements/TransferRequests/Details/{transfer}") });
                Assert.True(approve == 200, $"approve on behalf answered {approve} at {path}");
                Assert.Equal($"1|{delegateId}|{principalId}", Sql("hr_movements_db",
                    $"select t.\"Status\", a.\"ApprovedBy\", a.\"OnBehalfOfUserId\" from \"TransferRequests\" t join \"TransferApprovals\" a on a.\"TransferRequestId\" = t.\"Id\" where t.\"Id\" = '{transfer}'").Single());

                // The internal endpoint is for services only: a signed-in user is refused.
                internalStatus = await Iam50GetAsync(principal, $"/api/hr-core/api/internal/approval-delegations/active?employeeId={delegateEmployee}");
                Assert.Equal(403, internalStatus);
            }
            finally
            {
                await PostFormAsync(principal, $"/ui/hr-core/Delegations/Revoke/{delegation}", new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(principal, "/ui/hr-core/Delegations/Create"),
                    ["reason"] = Iam50Marker
                });
                if (transfer.Length > 0)
                    await Iam50CancelAsync(creator, transfer);
            }

            Assert.Equal("t", Sql("hr_core_db", $"select \"IsRevoked\" from \"ApprovalDelegations\" where \"Id\" = '{delegation}'").Single());
            var revokedAt = DateTime.UtcNow;
            while (await Iam50GetAsync(delegateUser, "/ui/hr-movements/TransferRequests") != 403 && DateTime.UtcNow - revokedAt < Iam50Cache)
                await Task.Delay(TimeSpan.FromSeconds(5));
            refusedAfter = (int)(DateTime.UtcNow - revokedAt).TotalSeconds;
            Assert.Equal(403, await Iam50GetAsync(delegateUser, "/ui/hr-movements/TransferRequests"));

            recorder.WriteScenarioDbChecks(new
            {
                delegation,
                transfer,
                principal = principalId,
                @delegate = delegateId,
                grantedAfterSeconds = grantedAfter,
                refusedAfterRevocationSeconds = refusedAfter,
                internalEndpointForAUser = internalStatus
            });
        }

        recorder.WriteResult("Pass");
    }

    /// <summary>
    /// The engine on the server: hradmin files a requisition (as the PLAN-43 check does); e2e-delegate-hr approves step 1 in his own name
    /// (hr-core is his role) and step 2, whose role he does not hold, on behalf of e2e-checker-mgmt under a one-day RecruitmentRequisition
    /// delegation. The step history names the principal and the delegation; the signature is the delegate's, in the delegated role.
    /// Leaves an approved requisition marked «PLAN-50 server check» on one vacant funded position, and the revoked delegation.
    /// </summary>
    [SkippableFact]
    public async Task AC_IAM_50_vps_a_delegate_approves_step_two_of_a_requisition_on_behalf()
    {
        var recorder = StartIam50("engine", "Demo server: hradmin files a requisition; e2e-delegate-hr approves step 1 himself and step 2 on behalf of e2e-checker-mgmt; the history and the signature name the principal");
        var password = DemoPassword();
        var principalId = KeycloakUserId(Iam50Approver);
        var delegateId = KeycloakUserId(Iam50Delegate);
        var delegateEmployee = Sql("keycloak_db",
            "select a.value from user_attribute a join user_entity u on u.id = a.user_id join realm r on r.id = u.realm_id " +
            $"where r.name = 'hrmanagement' and u.username = '{Iam50Delegate}' and a.name = 'employee_id'").SingleOrDefault() ?? "";
        AcceptanceGate.RequireData(recorder, Guid.TryParse(delegateEmployee, out _), "المستخدم المفوَّض بلا employee_id",
            $"{Iam50Delegate} has no employee_id: add it with scripts/vps/add-demo-user.sh --employee-id (PLAN-50 P-13).");
        var position = Sql("hr_core_db",
            "select p.\"Id\", p.\"OrgUnitId\" from \"FundedPositions\" p where p.\"Status\" = 'Vacant' and not exists " +
            "(select 1 from \"RecruitmentRequisitions\" r where r.\"FundedPositionId\" = p.\"Id\" and r.\"Status\" in ('Draft', 'PendingApproval', 'Approved', 'Published')) " +
            "order by p.\"PositionCode\" limit 1");
        AcceptanceGate.RequireData(recorder, position.Length == 1, "لا وظيفة شاغرة", "No vacant funded position without a requisition.");
        var (positionId, orgUnit) = (position[0].Split('|')[0], position[0].Split('|')[1]);

        string requisition;
        var (creator, _) = await SignInAsync("hradmin", password);
        using (creator)
        {
            const string createPage = "/ui/hr-core/RecruitmentRequisitions/Create";
            string[] rows = [];
            var status = 0;
            for (var attempt = 0; attempt < 5 && rows.Length == 0; attempt++)
            {
                (status, _, _) = await PostFormAsync(creator, createPage, new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(creator, createPage),
                    ["ScopeOrgUnitId"] = orgUnit,
                    ["FundedPositionId"] = positionId,
                    ["JobTitle"] = Iam50Marker,
                    ["JustificationNotes"] = Iam50Marker,
                    ["LegalSourceId"] = "SYR-LAW-50-2004",
                    ["BudgetYear"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
                    ["PublishingChannel"] = "PublicOnly"
                });
                rows = Sql("hr_core_db", $"select \"Id\" from \"RecruitmentRequisitions\" where \"FundedPositionId\" = '{positionId}' and \"Status\" = 'Draft'");
            }

            Assert.True(rows.Length == 1, $"create answered {status}");
            requisition = rows[0];
            var details = $"/ui/hr-core/RecruitmentRequisitions/Details/{requisition}";
            var criteria = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(creator, details),
                ["RequisitionId"] = requisition,
                ["Title"] = Iam50Marker
            };
            (string Key, string Name, string Weight, string Stage)[] items =
                [("interview", "المقابلة", "40", "Interview"), ("exam", "الاختبار", "30", "Exam"), ("experience", "الخبرة", "30", "Experience")];
            for (var i = 0; i < items.Length; i++)
            {
                criteria[$"Items[r{i}].Key"] = items[i].Key;
                criteria[$"Items[r{i}].NameAr"] = items[i].Name;
                criteria[$"Items[r{i}].Weight"] = items[i].Weight;
                criteria[$"Items[r{i}].MaxScore"] = "100";
                criteria[$"Items[r{i}].Stage"] = items[i].Stage;
                criteria[$"Items[r{i}].IsMandatory"] = "true";
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

        var (principal, _) = await SignInAsync(Iam50Approver, password);
        var (delegateUser, _) = await SignInAsync(Iam50Delegate, password);
        using (principal)
        using (delegateUser)
        {
            var now = DateTime.UtcNow.AddMinutes(-2);
            var (created, createdPath, _) = await PostFormAsync(principal, "/ui/hr-core/Delegations/Create", new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await TokenAsync(principal, "/ui/hr-core/Delegations/Create"),
                ["DelegateEmployeeId"] = delegateEmployee,
                ["RoleScope"] = "hr-management-approver",
                ["EntityTypeScope"] = "RecruitmentRequisition",
                ["ActiveFrom"] = now.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                ["ActiveUntil"] = now.AddDays(1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                ["Reason"] = $"{Iam50Marker} engine {Guid.NewGuid():N}"[..40]
            });
            Assert.True(created == 200, $"delegation create answered {created} at {createdPath}");
            var delegation = Sql("hr_core_db",
                $"select \"Id\" from \"ApprovalDelegations\" where \"PrincipalId\" = '{principalId}' and \"DelegateEmployeeId\" = '{delegateEmployee}' and \"EntityTypeScope\" = 'RecruitmentRequisition' and not \"IsRevoked\" order by \"CreatedAt\" desc limit 1").Single();
            try
            {
                var details = $"/ui/hr-core/RecruitmentRequisitions/Details/{requisition}";
                foreach (var step in new[] { 1, 2 })
                {
                    await PostFormAsync(delegateUser, $"/ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{requisition}", new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = await TokenAsync(delegateUser, details),
                        ["RequisitionId"] = requisition,
                        ["Comments"] = $"{Iam50Marker} {step}"
                    });
                }

                var state = Sql("hr_core_db",
                    $"select r.\"Status\", i.\"Status\", i.\"Id\" from \"RecruitmentRequisitions\" r join \"WorkflowInstances\" i on i.\"Id\" = r.\"WorkflowInstanceId\" where r.\"Id\" = '{requisition}'").Single().Split('|');
                Assert.Equal(["Approved", "Completed"], state[..2]);
                var history = Sql("hr_core_db", $"select \"StepHistoryJson\" from \"WorkflowInstances\" where \"Id\" = '{state[2]}'").Single();
                using var json = System.Text.Json.JsonDocument.Parse(history);
                var entries = json.RootElement.EnumerateArray().ToArray();
                var first = entries[^2];
                var last = entries[^1];
                Assert.Equal(delegateId, first.GetProperty("ActorId").GetString());
                Assert.False(first.TryGetProperty("OnBehalfOfId", out _), "step 1 was approved in the delegate's own name");
                Assert.Equal(delegateId, last.GetProperty("ActorId").GetString());
                Assert.Equal(principalId, last.GetProperty("OnBehalfOfId").GetString());
                Assert.Equal(delegation, last.GetProperty("DelegationId").GetString());
                var signature = Sql("hr_core_db",
                    $"select \"SignerSubject\" || '|' || \"SignerRole\" from \"SignatureRecords\" where \"SubjectId\" = '{requisition}' and \"Intent\" = 'approve' order by \"OccurredAtUtc\" desc limit 1").Single();
                Assert.Equal($"{delegateId}|hr-management-approver", signature);
                recorder.WriteScenarioDbChecks(new { requisition, positionId, delegation, principal = principalId, @delegate = delegateId, state = string.Join('|', state[..2]), signature });
            }
            finally
            {
                await PostFormAsync(principal, $"/ui/hr-core/Delegations/Revoke/{delegation}", new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(principal, "/ui/hr-core/Delegations/Create"),
                    ["reason"] = Iam50Marker
                });
            }

            Assert.Equal("t", Sql("hr_core_db", $"select \"IsRevoked\" from \"ApprovalDelegations\" where \"Id\" = '{delegation}'").Single());
        }

        recorder.WriteScenarioSteps(
            "1. hradmin creates a requisition on a vacant funded position, its criteria, and submits it: PendingApproval.\n" +
            "2. e2e-checker-mgmt delegates hr-management-approver for RecruitmentRequisition to e2e-delegate-hr for one day.\n" +
            "3. e2e-delegate-hr approves step 1 (his own hr-core) and step 2 (on behalf): Approved, Completed; the last history entry names the principal and the delegation; the signature is the delegate's in the delegated role.\n" +
            "4. The delegation is revoked.");
        recorder.WriteResult("Pass");
    }
}
