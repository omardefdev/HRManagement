using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-52 (P8, P-16) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers. <c>e2e-branch-manager</c>
/// (branch-manager, hr-core, admin-logistics; scope = the HR department <c>…111102</c>, added with
/// <c>scripts/vps/add-demo-user.sh --scope-org-unit-id</c>) stays in his department; the negatives use the Finance department (<c>…111103</c>),
/// central records (no unit) and the satellites' records (no employee snapshot on the demo, so central). The demo users' password is read
/// into memory only; evidence goes under <c>artifacts/plan52/vps</c> and is never committed. The checks create nothing: every write they
/// send is refused before it reaches the database (404 or 403), and the rows they target are read before and after.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Iam52User = "e2e-branch-manager";
    private const string Iam52Branch = "11111111-1111-1111-1111-111111111102";
    private const string Iam52Other = "11111111-1111-1111-1111-111111111103";
    private const string Iam52Marker = "PLAN-52 server check";

    private static readonly Regex Iam52Guid = new("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static EvidenceRecorder StartIam52(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"iam52-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan52", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    /// <summary>The ids of the HR.Core units in a unit's subtree, as a SQL list.</summary>
    private static string Iam52Subtree(string unit) =>
        $"select \"Id\" from \"OrgUnits\" where \"MaterializedPath\" like (select \"MaterializedPath\" from \"OrgUnits\" where \"Id\" = '{unit}') || '%'";

    private static async Task<(int Status, string Body)> Iam52GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"https://{AppDomain}{path}");
        return ((int)response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>A row's count and an md5 of its whole text: what "nothing changed" compares.</summary>
    private static string Iam52Row(string database, string table, string id) =>
        Sql(database, $"select count(*)::text || ':' || md5(coalesce(string_agg(t::text, '|'), '')) from \"{table}\" t where t.\"Id\" = '{id}'").Single();

    private static async Task<int> Iam52PostAsync(HttpClient client, string path, string tokenPage, Dictionary<string, string>? fields = null)
    {
        var form = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = await TokenAsync(client, tokenPage) };
        var (status, _, _) = await PostFormAsync(client, path, form);
        return status;
    }

    [SkippableFact]
    public void AC_IAM_52_vps_the_branch_manager_carries_his_scope_and_the_migration_ran()
    {
        var recorder = StartIam52("setup", "Demo server: e2e-branch-manager holds branch-manager with scope HR; HCM052 is recorded; Diwan and inspections have a unit column; the requisition instances carry a unit");
        var user = KeycloakUserId(Iam52User);
        var scope = Sql("keycloak_db", $"select value from user_attribute where user_id = '{user}' and name = 'scope_org_unit_id'").SingleOrDefault();
        var roles = Sql("keycloak_db",
            "select r.name from user_role_mapping m join keycloak_role r on r.id = m.role_id " +
            $"where m.user_id = '{user}' and r.name in ('branch-manager', 'hr-core', 'admin-logistics', 'hr-admin') order by r.name");
        var migration = Sql("hr_core_db", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%HCM052_OrgScopeKeys'").Single();
        var columns = Sql("hr_core_db",
            "select count(*) from information_schema.columns where column_name = 'OrgUnitId' and table_name in ('DiwanCorrespondences', 'FieldInspectionVisits')").Single();
        var requisitionsWithoutUnit = Sql("hr_core_db",
            "select count(*) from \"WorkflowInstances\" i join \"RecruitmentRequisitions\" r on r.\"Id\" = i.\"EntityId\" " +
            "join \"FundedPositions\" p on p.\"Id\" = r.\"FundedPositionId\" where i.\"EntityType\" = 'RecruitmentRequisition' and i.\"ScopeOrgUnitId\" is null and p.\"OrgUnitId\" is not null").Single();
        recorder.WriteDbChecks(new { scope, roles, migration, columns, requisitionsWithoutUnit });

        Assert.Equal(Iam52Branch, scope);
        Assert.Equal(new[] { "admin-logistics", "branch-manager", "hr-core" }, roles);
        Assert.Equal("1", migration);
        Assert.Equal("2", columns);
        Assert.Equal("0", requisitionsWithoutUnit);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_52_vps_the_branch_manager_stays_in_his_branch_on_the_hr_core_and_logistics_screens()
    {
        var recorder = StartIam52("screens", "Demo server: e2e-branch-manager opens his department's roster; key positions and supply orders list his department's only (counts equal psql); Finance's key position, attendance and supply order answer 404 on details and on a POST by id, and stay unchanged; inventory and vendor writes 403");
        var (client, _) = await SignInAsync(Iam52User, DemoPassword());
        using (client)
        {
            var mine = Iam52Subtree(Iam52Branch);
            var theirs = Iam52Subtree(Iam52Other);
            var roster = Sql("hr_core_db", $"select \"Id\" from \"ShiftRosters\" where \"OrgUnitId\" in ({mine}) order by \"CreatedAt\" limit 1").SingleOrDefault();
            var foreignKey = Sql("hr_core_db", $"select \"Id\" from \"KeyPositions\" where \"OrgUnitId\" in ({theirs}) order by \"CreatedAt\" limit 1").SingleOrDefault();
            var foreignAttendance = Sql("hr_core_db",
                $"select a.\"Id\" from \"DailyAttendanceRecords\" a join \"Employees\" e on e.\"Id\" = a.\"EmployeeId\" where e.\"PrimaryOrgUnitId\" in ({theirs}) order by a.\"AttendanceDate\" desc limit 1").SingleOrDefault();
            var foreignOrder = Sql("admin_db", "select \"Id\" from \"SupplyOrders\" where \"OrgUnitId\" = '" + Iam52Other + "' order by \"OrderedAt\" desc limit 1").SingleOrDefault();
            AcceptanceGate.RequireData(recorder, roster is not null && foreignKey is not null && foreignAttendance is not null,
                "بيانات العرض لا تحوي سجلات القسمين", "The demo data lacks a roster of HR, or a key position or attendance of Finance (P0 found them).");

            var (rosterStatus, _) = await Iam52GetAsync(client, $"/ui/hr-core/Rosters/Details/{roster}");
            var (keyListStatus, keyList) = await Iam52GetAsync(client, "/ui/hr-core/KeyPositions");
            var keyIds = Iam52Guid.Matches(keyList).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
            var keysOutside = keyIds.Count == 0 ? "0" : Sql("hr_core_db",
                $"select count(*) from \"KeyPositions\" where \"Id\" in ({string.Join(",", keyIds.Select(i => $"'{i}'"))}) and (\"OrgUnitId\" is null or \"OrgUnitId\" not in ({mine}))").Single();
            var keysListed = keyIds.Count == 0 ? "0" : Sql("hr_core_db",
                $"select count(*) from \"KeyPositions\" where \"Id\" in ({string.Join(",", keyIds.Select(i => $"'{i}'"))})").Single();
            var keysInBranch = Sql("hr_core_db", $"select count(*) from \"KeyPositions\" where \"OrgUnitId\" in ({mine})").Single();

            // Admin.Logistics resolves his scope from its own unit snapshot. Without one (the demo server, PLAN-52's defect list) his scope
            // there is Denied, so every logistics screen refuses him (403, fail-closed); with one, his department's orders list and another
            // department's answer 404.
            var logisticsKnowsUnit = Sql("admin_db", $"select count(*) from \"OrgUnitSnapshots\" where \"Id\" = '{Iam52Branch}'").Single() == "1";
            var (orderListStatus, orderList) = await Iam52GetAsync(client, "/ui/admin-logistics/SupplyOrders");
            var orderIds = Iam52Guid.Matches(orderList).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
            string[] orderUnits = orderIds.Count == 0 ? [] : Sql("admin_db",
                $"select coalesce(\"OrgUnitId\"::text, 'none') from \"SupplyOrders\" where \"Id\" in ({string.Join(",", orderIds.Select(i => $"'{i}'"))})");
            var branchUnits = Sql("hr_core_db", mine).Select(u => u.ToLowerInvariant()).ToHashSet();
            var ordersInBranch = Sql("admin_db", $"select count(*) from \"SupplyOrders\" where \"OrgUnitId\" in ({string.Join(",", branchUnits.Select(u => $"'{u}'"))})").Single();

            var rowsBefore = new[]
            {
                Iam52Row("hr_core_db", "KeyPositions", foreignKey!), Iam52Row("hr_core_db", "DailyAttendanceRecords", foreignAttendance!),
                foreignOrder is null ? "-" : Iam52Row("admin_db", "SupplyOrders", foreignOrder)
            };
            var inventoryBefore = Sql("admin_db", $"select count(*) from \"InventoryItems\" where \"Name\" = '{Iam52Marker}'").Single();
            var vendorsBefore = Sql("admin_db", $"select count(*) from \"Vendors\" where \"Name\" = '{Iam52Marker}'").Single();

            var answers = new Dictionary<string, int>
            {
                ["key-position-edit"] = (await Iam52GetAsync(client, $"/ui/hr-core/KeyPositions/Edit/{foreignKey}")).Status,
                ["key-position-delete"] = await Iam52PostAsync(client, $"/ui/hr-core/KeyPositions/Delete/{foreignKey}", "/ui/hr-core/KeyPositions"),
                ["attendance-details"] = (await Iam52GetAsync(client, $"/ui/hr-core/DailyAttendanceRecords/Details/{foreignAttendance}")).Status,
                ["attendance-delete"] = await Iam52PostAsync(client, $"/ui/hr-core/DailyAttendanceRecords/Delete/{foreignAttendance}", "/ui/hr-core/DailyAttendanceRecords"),
                ["inventory-create"] = await Iam52PostAsync(client, "/ui/admin-logistics/InventoryItems/Create", "/ui/admin-logistics/InventoryItems",
                    new Dictionary<string, string> { ["Name"] = Iam52Marker, ["Quantity"] = "1" }),
                ["vendor-create"] = await Iam52PostAsync(client, "/ui/admin-logistics/Vendors/Create", "/ui/admin-logistics/Vendors",
                    new Dictionary<string, string> { ["Name"] = Iam52Marker })
            };
            if (foreignOrder is not null)
            {
                answers["supply-order-details"] = (await Iam52GetAsync(client, $"/ui/admin-logistics/SupplyOrders/Details/{foreignOrder}")).Status;
                // The token comes from a page of the segment he opens whatever his scope there (the inventory list, reference data).
                answers["supply-order-cancel"] = await Iam52PostAsync(client, $"/ui/admin-logistics/SupplyOrders/Cancel/{foreignOrder}", "/ui/admin-logistics/InventoryItems");
            }

            var rowsAfter = new[]
            {
                Iam52Row("hr_core_db", "KeyPositions", foreignKey!), Iam52Row("hr_core_db", "DailyAttendanceRecords", foreignAttendance!),
                foreignOrder is null ? "-" : Iam52Row("admin_db", "SupplyOrders", foreignOrder)
            };
            var inventoryAfter = Sql("admin_db", $"select count(*) from \"InventoryItems\" where \"Name\" = '{Iam52Marker}'").Single();
            var vendorsAfter = Sql("admin_db", $"select count(*) from \"Vendors\" where \"Name\" = '{Iam52Marker}'").Single();

            recorder.WriteDbChecks(new
            {
                roster,
                rosterStatus,
                keyListStatus,
                keysListed,
                keysOutside,
                keysInBranch,
                logisticsKnowsUnit,
                orderListStatus,
                ordersListed = orderUnits.Length,
                ordersInBranch,
                ordersOutside = orderUnits.Count(u => !branchUnits.Contains(u.ToLowerInvariant())),
                foreignKey,
                foreignAttendance,
                foreignOrder,
                answers,
                rowsBefore,
                rowsAfter,
                inventoryBefore,
                inventoryAfter,
                vendorsBefore,
                vendorsAfter
            });

            Assert.Equal(200, rosterStatus);
            Assert.Equal(200, keyListStatus);
            Assert.Equal("0", keysOutside);
            Assert.Equal(keysInBranch, keysListed);
            if (logisticsKnowsUnit)
            {
                Assert.Equal(200, orderListStatus);
                Assert.DoesNotContain(orderUnits, u => !branchUnits.Contains(u.ToLowerInvariant()));
                Assert.Equal(ordersInBranch, orderUnits.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                Assert.Equal(403, orderListStatus);
            }

            foreach (var (step, status) in answers)
            {
                var expected = step is "inventory-create" or "vendor-create" || (step.StartsWith("supply-order", StringComparison.Ordinal) && !logisticsKnowsUnit) ? 403 : 404;
                Assert.True(status == expected, $"{step}: {status}, expected {expected}");
            }
            Assert.Equal(rowsBefore, rowsAfter);
            Assert.Equal((inventoryBefore, vendorsBefore), (inventoryAfter, vendorsAfter));
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_IAM_52_vps_workflow_instances_and_the_hire_wizard_stay_in_his_branch()
    {
        var recorder = StartIam52("instances", "Demo server: api/workflow-instances lists only his department's instances (psql on every id); an instance of another unit and a central one answer 404 to him and 200 to hradmin; the hire wizard lists none of the demo's appointments (no employee snapshot: central) and their Step2 answers 404 to him, 200 to hradmin");
        var password = DemoPassword();
        var (branch, _) = await SignInAsync(Iam52User, password);
        var (admin, _) = await SignInAsync("hradmin", password);
        using (branch)
        using (admin)
        {
            var mine = Iam52Subtree(Iam52Branch);
            var foreignInstance = Sql("hr_core_db",
                $"select \"Id\" from \"WorkflowInstances\" where \"ScopeOrgUnitId\" is not null and \"ScopeOrgUnitId\" not in ({mine}) order by \"StartedAt\" desc limit 1").SingleOrDefault();
            var centralInstance = Sql("hr_core_db", "select \"Id\" from \"WorkflowInstances\" where \"ScopeOrgUnitId\" is null order by \"StartedAt\" desc limit 1").SingleOrDefault();
            var appointment = Sql("hr_contracts_db", "select \"Id\" from \"ServiceTransactions\" where \"TransactionType\" = 'Appointment' order by \"CreatedAt\" desc limit 1").SingleOrDefault();
            AcceptanceGate.RequireData(recorder, foreignInstance is not null && centralInstance is not null,
                "لا توجد نسخ مسارات خارج القسم أو مركزية", "The demo holds no workflow instance outside HR, or none without a unit.");

            const string api = "/ui/hr-core/api/workflow-instances";
            var (listStatus, list) = await Iam52GetAsync(branch, api);
            List<string> ids = listStatus == 200
                ? JsonDocument.Parse(list).RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList()
                : [];
            var outside = ids.Count == 0 ? "0" : Sql("hr_core_db",
                $"select count(*) from \"WorkflowInstances\" where \"Id\" in ({string.Join(",", ids.Select(i => $"'{i}'"))}) and (\"ScopeOrgUnitId\" is null or \"ScopeOrgUnitId\" not in ({mine}))").Single();
            var answers = new Dictionary<string, int>
            {
                ["foreign-get"] = (await Iam52GetAsync(branch, $"{api}/{foreignInstance}")).Status,
                ["foreign-history"] = (await Iam52GetAsync(branch, $"{api}/{foreignInstance}/history")).Status,
                ["central-get"] = (await Iam52GetAsync(branch, $"{api}/{centralInstance}")).Status,
                ["admin-foreign-get"] = (await Iam52GetAsync(admin, $"{api}/{foreignInstance}")).Status,
                ["admin-central-get"] = (await Iam52GetAsync(admin, $"{api}/{centralInstance}")).Status
            };
            var (wizardStatus, wizard) = await Iam52GetAsync(branch, "/ui/hr-contracts/HireWizard");
            var appointments = Sql("hr_contracts_db", "select \"Id\"::text from \"ServiceTransactions\" where \"TransactionType\" = 'Appointment'");
            var wizardShows = appointments.Count(a => wizard.Contains(a, StringComparison.OrdinalIgnoreCase));
            if (appointment is not null)
            {
                answers["appointment-step2"] = (await Iam52GetAsync(branch, $"/ui/hr-contracts/HireWizard/Step2?transactionId={appointment}")).Status;
                answers["admin-appointment-step2"] = (await Iam52GetAsync(admin, $"/ui/hr-contracts/HireWizard/Step2?transactionId={appointment}")).Status;
            }

            recorder.WriteDbChecks(new { listStatus, listed = ids.Count, outside, foreignInstance, centralInstance, answers, wizardStatus, appointments = appointments.Length, wizardShows });

            Assert.Equal(200, listStatus);
            Assert.Equal("0", outside);
            Assert.DoesNotContain(foreignInstance!, ids);
            Assert.DoesNotContain(centralInstance!, ids);
            Assert.Equal(404, answers["foreign-get"]);
            Assert.Equal(404, answers["foreign-history"]);
            Assert.Equal(404, answers["central-get"]);
            Assert.Equal(200, answers["admin-foreign-get"]);
            Assert.Equal(200, answers["admin-central-get"]);
            Assert.Equal(200, wizardStatus);
            Assert.Equal(0, wizardShows);
            if (appointment is not null)
            {
                Assert.Equal(404, answers["appointment-step2"]);
                Assert.Equal(200, answers["admin-appointment-step2"]);
            }
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public void AC_IAM_52_vps_transfers_reach_hr_health_and_the_directory_sync_is_as_recorded()
    {
        var recorder = StartIam52("transfers", "Demo server, read-only: ITransferExecuted is bound to an HR.Health queue with a consumer; Keycloak:DirectorySync:Enabled is false, as P0 recorded (no environment override)");
        var rabbit = ContainerOf("rabbitmq");
        var bindings = Sh($"docker exec {rabbit} rabbitmqctl -q list_bindings source_name destination_name").Output;
        var healthQueues = bindings.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t', StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2 && p[0].Contains("ITransferExecuted", StringComparison.Ordinal) && p[1].Contains("health", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[1]).Distinct().ToList();
        var queues = Sh($"docker exec {rabbit} rabbitmqctl -q list_queues name consumers").Output;
        var consumers = healthQueues.Select(q => queues.Split('\n').Select(l => l.Split('\t', StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2 && p[0] == q).Select(p => int.TryParse(p[1], out var n) ? n : 0).FirstOrDefault()).ToList();
        var auth = ContainerOf("auth-directory-web");
        var syncOverride = Sh($"docker exec {auth} printenv | grep -ci 'DirectorySync__Enabled' || true").Output.Trim();
        recorder.WriteDbChecks(new { healthQueues, consumers, syncOverride });

        Assert.NotEmpty(healthQueues);
        Assert.All(consumers, c => Assert.True(c >= 1, "an HR.Health transfer queue has no consumer"));
        Assert.Equal("0", syncOverride);
        recorder.WriteResult("Pass");
    }
}
