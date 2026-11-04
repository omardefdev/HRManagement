using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-38 (P6 on the server): the two PLAN-37 demo defects checked through the real UI of the demo server
/// (<c>VPS_SSH_HOST=hrm-vps</c>), with the AC-OPS-36 helpers (sign-in through the edge, ssh, psql). A leave filed by
/// <c>e2e-maker-contracts</c> is approved by <c>hradmin</c> (separation of duties), the balance is debited once and the
/// service transaction completes; the leave is then cancelled (the balance comes back), so the demo data stays as it
/// was. <c>hradmin</c> issues an ID card through <c>IssuedCards/Create</c>. The demo users' password is read from the
/// server's .env into memory only; evidence (<c>acceptance-evidence/AC-OPS-38/vps/</c>) holds ids and states.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string LeaveMaker = "e2e-maker-contracts";
    private const string LeaveApprover = "hradmin";

    private static readonly Regex AntiforgeryToken =
        new(@"name=""__RequestVerificationToken""[^>]*value=""(?<v>[^""]+)""|value=""(?<v>[^""]+)""[^>]*name=""__RequestVerificationToken""", RegexOptions.IgnoreCase);

    private static EvidenceRecorder StartOps38(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"ops38-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence", "AC-OPS-38"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check runs against the demo server only.");
        var (code, output) = Sh($"docker ps -q --filter label=com.docker.compose.project={Project} --filter label=com.docker.compose.service=edge");
        AcceptanceGate.RequireData(recorder, code == 0 && output.Trim().Length > 0, "الخادم الواحد غير منشور", "The demo server's project hrm is not running.");
        return recorder;
    }

    /// <summary>One psql statement as the administrator inside the postgres container; unaligned rows, '|' separated.</summary>
    private static string[] Sql(string database, string sql)
    {
        var pg = ContainerOf("postgresql");
        var (code, output) = Sh($"docker exec {pg} sh -c {Q($"psql -U \"$POSTGRES_USER\" -d {database} -v ON_ERROR_STOP=1 -tA -F '|' -c {Q(sql)}")}");
        Assert.True(code == 0, $"psql on {database} failed: {output}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var page = await client.GetAsync($"https://{AppDomain}{path}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, $"GET {path} answered {(int)page.StatusCode}");
        var token = AntiforgeryToken.Match(html).Groups["v"].Value;
        Assert.False(string.IsNullOrEmpty(token), $"no antiforgery token on {path}");
        return WebUtility.HtmlDecode(token);
    }

    private static async Task<(int Status, string FinalPath, string Body)> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        using var response = await client.PostAsync($"https://{AppDomain}{path}", new FormUrlEncodedContent(fields));
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, response.RequestMessage!.RequestUri!.AbsolutePath, body);
    }

    private static string KeycloakUserId(string username) =>
        Sql("keycloak_db", $"select u.id from user_entity u join realm r on r.id = u.realm_id where r.name = 'hrmanagement' and u.username = '{username}'").Single();

    [SkippableFact]
    public async Task AC_OPS_38_vps_leave_approval_and_id_card_creation_work_on_the_demo_server()
    {
        var recorder = StartOps38("38-vps-p6", "PLAN-38 P6 on the demo server: a leave filed by e2e-maker-contracts is approved by hradmin (balance debited once, transaction completed) and cancelled again; hradmin issues an ID card");
        var password = DemoPassword();
        var approverId = KeycloakUserId(LeaveApprover);

        // A future working period with notice, inside the year of the seeded Annual balances.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.AddDays(21);
        while (start.DayOfWeek != DayOfWeek.Sunday)
            start = start.AddDays(1);
        var end = start.AddDays(2);
        var startText = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endText = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var candidate = Sql("hr_contracts_db",
            "select b.\"EmployeeId\", b.\"Id\", b.\"Used\" from \"LeaveBalances\" b " +
            $"where b.\"LeaveType\" = 'Annual' and b.\"Year\" = {start.Year} and b.\"TotalEntitlement\" - b.\"Used\" >= 10 " +
            "and not exists (select 1 from \"LeaveRequests\" l where l.\"EmployeeId\" = b.\"EmployeeId\" " +
            $"and l.\"Status\" in ('Pending', 'Approved') and l.\"EndDate\" >= '{startText}' and l.\"StartDate\" <= '{endText}') " +
            "order by b.\"EmployeeId\" limit 1");
        AcceptanceGate.RequireData(recorder, candidate.Length == 1, "لا رصيد إجازات سنوية مناسب",
            $"No employee with an Annual {start.Year} balance of 10 days or more and no leave in {startText}..{endText}.");
        var parts = candidate[0].Split('|');
        var employeeId = Guid.Parse(parts[0]);
        var balanceId = Guid.Parse(parts[1]);
        var usedBefore = int.Parse(parts[2], CultureInfo.InvariantCulture);
        Before(recorder, new { employeeId, balanceId, usedBefore, start = startText, end = endText });

        // 1. The maker files the leave through the UI.
        var (maker, _) = await SignInAsync(LeaveMaker, password, "/ui/hr-contracts/");
        string leaveId;
        using (maker)
        {
            var token = await TokenAsync(maker, "/ui/hr-contracts/LeaveRequests/Create");
            var (status, finalPath, body) = await PostFormAsync(maker, "/ui/hr-contracts/LeaveRequests/Create", new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmployeeId"] = employeeId.ToString(),
                ["LeaveType"] = "Annual",
                ["StartDate"] = startText,
                ["EndDate"] = endText,
                ["DurationDays"] = "0",
                ["Notes"] = "PLAN-38 server check (cancelled afterwards)",
            });
            Assert.True(status == 200 && (finalPath.EndsWith("/LeaveRequests", StringComparison.OrdinalIgnoreCase)
                        || finalPath.EndsWith("/LeaveRequests/Index", StringComparison.OrdinalIgnoreCase)),
                $"leave create ended at {finalPath} ({status}); validation: {Regex.Match(body, "validation-summary[^>]*>(?<t>[\\s\\S]{0,300})").Groups["t"].Value}");
            var created = Sql("hr_contracts_db",
                $"select \"Id\", \"Status\", \"DurationDays\", coalesce(\"CreatedBy\"::text, '') from \"LeaveRequests\" where \"EmployeeId\" = '{employeeId}' " +
                $"and \"StartDate\" = '{startText}' and \"Notes\" like 'PLAN-38 server check%' order by \"CreatedAt\" desc limit 1");
            Assert.Single(created);
            var row = created[0].Split('|');
            leaveId = row[0];
            Assert.Equal("Pending", row[1]);
            Assert.NotEqual(approverId, row[3]);
        }

        var duration = int.Parse(Sql("hr_contracts_db", $"select \"DurationDays\" from \"LeaveRequests\" where \"Id\" = '{leaveId}'").Single(), CultureInfo.InvariantCulture);

        // 2. hradmin approves it (PLAN-37 defect 1: every approval failed with a concurrency error on PostgreSQL).
        var (approver, _) = await SignInAsync(LeaveApprover, password);
        using (approver)
        {
            // An earlier run that stopped after its approval left its leave Approved: cancel it the same way (UI).
            foreach (var leftover in Sql("hr_contracts_db",
                         $"select \"Id\" from \"LeaveRequests\" where \"Notes\" like 'PLAN-38 server check%' and \"Status\" = 'Approved' and \"Id\" <> '{leaveId}'"))
            {
                var cancelToken = await TokenAsync(approver, "/ui/hr-contracts/LeaveRequests");
                await PostFormAsync(approver, $"/ui/hr-contracts/LeaveRequests/Cancel/{leftover}",
                    new Dictionary<string, string> { ["__RequestVerificationToken"] = cancelToken });
                Assert.Equal("Cancelled", Sql("hr_contracts_db", $"select \"Status\" from \"LeaveRequests\" where \"Id\" = '{leftover}'").Single());
            }

            var token = await TokenAsync(approver, "/ui/hr-contracts/LeaveRequests");
            var (status, finalPath, _) = await PostFormAsync(approver, $"/ui/hr-contracts/LeaveRequests/Approve/{leaveId}",
                new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
            Assert.True(status == 200, $"approve answered {status} at {finalPath}");

            var approved = Sql("hr_contracts_db",
                $"select l.\"Status\", coalesce(l.\"ApprovedBy\"::text, ''), b.\"Used\", coalesce(t.\"State\", '') from \"LeaveRequests\" l " +
                $"join \"LeaveBalances\" b on b.\"Id\" = '{balanceId}' left join \"ServiceTransactions\" t on t.\"Id\" = l.\"ServiceTransactionId\" " +
                $"where l.\"Id\" = '{leaveId}'").Single().Split('|');
            Assert.Equal("Approved", approved[0]);
            Assert.Equal(approverId, approved[1]);
            Assert.Equal(usedBefore + duration, int.Parse(approved[2], CultureInfo.InvariantCulture));
            // PLAN-40: Records.Archive now answers and the transaction goes on to Archived (its consumer failed before).
            Assert.Contains(approved[3], new[] { "Completed", "Archived" });

            // 3. Cancel it again (the cancel handler had the same defect): the balance comes back.
            token = await TokenAsync(approver, "/ui/hr-contracts/LeaveRequests");
            (status, finalPath, _) = await PostFormAsync(approver, $"/ui/hr-contracts/LeaveRequests/Cancel/{leaveId}",
                new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
            Assert.True(status == 200, $"cancel answered {status} at {finalPath}");
            var cancelled = Sql("hr_contracts_db",
                $"select l.\"Status\", b.\"Used\" from \"LeaveRequests\" l join \"LeaveBalances\" b on b.\"Id\" = '{balanceId}' where l.\"Id\" = '{leaveId}'").Single().Split('|');
            Assert.Equal("Cancelled", cancelled[0]);
            Assert.Equal(usedBefore, int.Parse(cancelled[1], CultureInfo.InvariantCulture));

            // 4. ID card (PLAN-37 defect 2: IssuedCards/Create bound the entity and never passed validation).
            var design = Sql("idcard_db", "select \"Id\" from \"CardDesigns\" order by \"Id\" limit 1").First();
            var cardsBefore = int.Parse(Sql("idcard_db", $"select count(*) from \"IssuedCards\" where \"EmployeeId\" = '{employeeId}'").Single(), CultureInfo.InvariantCulture);
            token = await TokenAsync(approver, "/ui/idcard/IssuedCards/Create");
            (status, finalPath, _) = await PostFormAsync(approver, "/ui/idcard/IssuedCards/Create", new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmployeeId"] = employeeId.ToString(),
                ["CardDesignId"] = design,
                ["IssuedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                ["ExpiresAt"] = today.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["Status"] = "Active",
            });
            Assert.True(status == 200, $"ID card create answered {status} at {finalPath}");
            var card = Sql("idcard_db",
                $"select \"Id\", \"SerialNumber\", \"Status\" from \"IssuedCards\" where \"EmployeeId\" = '{employeeId}' order by \"IssuedAt\" desc limit 1").Single().Split('|');
            var cardsAfter = int.Parse(Sql("idcard_db", $"select count(*) from \"IssuedCards\" where \"EmployeeId\" = '{employeeId}'").Single(), CultureInfo.InvariantCulture);
            Assert.Equal(cardsBefore + 1, cardsAfter);
            Assert.False(string.IsNullOrWhiteSpace(card[1]), "the issued card has no serial number");

            Db(recorder, new
            {
                leave = new { id = leaveId, employeeId, durationDays = duration, approvedBy = LeaveApprover, approvedStatus = "Approved", transaction = "Completed", balanceUsedBefore = usedBefore, balanceUsedAfterApproval = usedBefore + duration, cancelledStatus = "Cancelled", balanceUsedAfterCancel = usedBefore },
                idCard = new { id = card[0], serialNumber = card[1], status = card[2], design, cardsBefore, cardsAfter },
            });
            After(recorder, new { leave = "filed by e2e-maker-contracts, approved and cancelled by hradmin", idCard = "issued by hradmin with a generated serial" });
            recorder.WriteResult("Pass", $"leave {leaveId}: approved by hradmin (balance +{duration}, transaction Completed) and cancelled (balance back to {usedBefore}); ID card {card[1]} issued");
        }
    }
}
