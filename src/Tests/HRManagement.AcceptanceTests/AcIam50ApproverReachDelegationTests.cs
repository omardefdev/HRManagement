using System.Globalization;
using System.Net;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.SharedKernel.Legal;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-IAM-50a–e (PLAN-50) on the prod-like stack, through the gateway. a: the management approver reads and approves the movement types
/// and creates or executes nothing, and the creator role does not approve; b: he approves the HR.Core actions of HrCore.Approve; c: the
/// delegation page with its refusals (a role not held, 120 days, a delegated role delegated again); d: a delegate approves step 2 of a
/// requisition on behalf of his principal, recorded in the history and the signature, and is refused after the revocation and on his own
/// request; e: a delegate approves a transfer on behalf, a delegation of another type does not reach it, and after the revocation and
/// the movement service's cache time he is refused. Each scenario revokes the delegations it creates. The delegate is <c>central-a2</c>.
/// </summary>
[RequiredAcceptance]
public class AcIam50ApproverReachDelegationTests
{
    private const string Ac = "AC-IAM-50";
    private const string Approver = "a-management-approver";
    private const string Movements = "a-movements";
    private const string Delegate = "central-a2";
    private const string ApproverRole = "hr-management-approver";
    private const string TenantA = "a0000001-0000-4000-8000-000000000001";
    private const string RoleMessage = "هذه الخطوة تتطلب دور «hr-management-approver».";
    private static readonly TimeSpan MovementCache = TimeSpan.FromSeconds(75);

    private static Task<string> MovementsSql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_movements_db", sql);

    private static async Task<string> ClaimAsync(string persona, string claim)
    {
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D"));
        return AcceptanceHarness.ParseJwt(token).Claims.FirstOrDefault(c => c.Type == claim)?.Value ?? string.Empty;
    }

    /// <summary>A POST with the persona's token and no antiforgery token: the 403 of a policy comes before antiforgery.</summary>
    private static async Task<HttpStatusCode> BarePostAsync(string persona, string path)
    {
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(AcceptanceHarness.RequirePersona(persona), Guid.NewGuid().ToString("D"));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(E2eEnv.BaseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await http.PostAsync(path, new FormUrlEncodedContent([new("x", "1")]));
        return response.StatusCode;
    }

    private static string Utc(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static List<KeyValuePair<string, string>> DelegationForm(string delegateEmployee, string role, string? entityType, int days) =>
    [
        new("DelegateEmployeeId", delegateEmployee),
        new("RoleScope", role),
        new("EntityTypeScope", entityType ?? string.Empty),
        new("ActiveFrom", Utc(DateTime.UtcNow.AddMinutes(-2))),
        new("ActiveUntil", Utc(DateTime.UtcNow.AddMinutes(-2).AddDays(days))),
        new("Reason", $"AC-IAM-50 {Guid.NewGuid():N}"[..20])
    ];

    /// <summary>The persona creates a delegation through the page (302 to the list) and its id is returned.</summary>
    private static async Task<string> DelegateAsync(RecruitmentSession s, string step, string principal, string delegateEmployee, string? entityType, int days = 7)
    {
        var principalSub = await ClaimAsync(principal, "sub");
        var since = DateTime.UtcNow.AddSeconds(-5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        await s[principal].PostAsync(step, "ui/hr-core/Delegations/Create", "ui/hr-core/Delegations/Create",
            DelegationForm(delegateEmployee, ApproverRole, entityType, days));
        var id = await Sql(
            $"SELECT \"Id\" FROM \"ApprovalDelegations\" WHERE \"PrincipalId\"='{principalSub}' AND \"DelegateEmployeeId\"='{delegateEmployee}' " +
            $"AND NOT \"IsRevoked\" AND \"CreatedAt\" >= '{since}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"{step}: the delegation was not created.");
        return id;
    }

    private static async Task RevokeAsync(RecruitmentSession s, string step, string principal, string id)
    {
        await s[principal].PostAsync(step, $"ui/hr-core/Delegations/Revoke/{id}", "ui/hr-core/Delegations/Create", [new("reason", "AC-IAM-50 revoke")]);
        Assert.Equal("true|AC-IAM-50 revoke", await Sql(
            $"SELECT \"IsRevoked\"::text || '|' || COALESCE(\"RevokedReason\", '') FROM \"ApprovalDelegations\" WHERE \"Id\"='{id}';"));
        Assert.Equal("1", await Sql($"SELECT count(*)::text FROM \"AuditLogs\" WHERE \"EntityId\"='{id}' AND \"Action\"='ApprovalDelegationRevoked';"));
    }

    private static async Task<(string Employee, string From, string To)> MovementInputsAsync()
    {
        var employee = await Sql($"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{TenantA}' AND \"CurrentStatus\"=0 ORDER BY \"NationalId\", \"Id\" OFFSET 7 LIMIT 1;");
        var units = (await Sql(
            $"SELECT string_agg(\"Id\"::text, ',' ORDER BY \"Code\") FROM (SELECT \"Id\",\"Code\" FROM \"OrgUnits\" WHERE \"TenantId\"='{TenantA}' LIMIT 2) o;")).Split(',');
        return (employee, units[0], units[^1]);
    }

    /// <summary>a-movements creates a transfer (the gateway, the creator role) and its id is returned.</summary>
    private static async Task<string> TransferAsync(RecruitmentSession s, string step)
    {
        var (employee, from, to) = await MovementInputsAsync();
        var since = DateTime.UtcNow.AddSeconds(-5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var day = DateTime.UtcNow.Date.AddDays(60);
        await s[Movements].PostAsync(step, "ui/hr-movements/TransferRequests/Create", "ui/hr-movements/TransferRequests/Create",
        [
            new("EmployeeId", employee), new("FromOrgUnitId", from), new("ToOrgUnitId", to),
            new("RequestDate", DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("EffectiveDate", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), new("Reason", $"AC-IAM-50 {step}")
        ]);
        var id = await MovementsSql($"SELECT \"Id\" FROM \"TransferRequests\" WHERE \"Reason\"='AC-IAM-50 {step}' AND \"CreatedAt\" >= '{since}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"{step}: the transfer was not created.");
        return id;
    }

    private static Task<string> TransferStatusAsync(string id) => MovementsSql($"SELECT \"Status\"::text FROM \"TransferRequests\" WHERE \"Id\"='{id}';");

    /// <summary>GET until the expected status answers, within the movement service's cache time.</summary>
    private static async Task<(HttpStatusCode Status, TimeSpan Waited)> UntilAsync(AcceptanceUi ui, string step, string path, HttpStatusCode expected)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            var (status, _) = await ui.GetWithStatusAsync(step, path);
            if (status == expected || DateTime.UtcNow - started > MovementCache)
                return (status, DateTime.UtcNow - started);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    // ---------------------------------------------------------------- 50a

    [SkippableFact]
    public async Task Ac50a_the_approver_reads_and_approves_movements_and_creates_and_executes_nothing_and_the_creator_role_does_not_approve()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "50a", "AC-IAM-50a: the approver alone approves a transfer and a promotion through the gateway; refused on create and execute; hr-movements refused on approve",
            Movements, Approver, "a-directory-checker");
        s.Steps("""
            1. a-movements creates a transfer and a promotion (the creator role). Setup by psql, as AC-S2S-42 does: the promoted
               employee's active employment record names law50, the regime whose promotion rules the create checks.
            2. a-management-approver (hr-management-approver only) opens the transfers list (pending by default) and the details through the
               gateway (200), approves the transfer → Approved with a TransferApproval row in his name, and approves the promotion → Approved.
            3. Negative: the approver's POST TransferRequests/Create and POST Execute → 403, nothing changes.
            4. Negative: a-movements approves a transfer that another user (a-directory-checker, hr-admin) created → 403, still Pending.
            """);
        var approverSub = await ClaimAsync(Approver, "sub");
        var transfer = await TransferAsync(s, "50a-transfer");
        var employee = await Sql(
            $"SELECT e.\"Id\" FROM \"Employees\" e JOIN \"EmploymentRecords\" r ON r.\"EmployeeId\"=e.\"Id\" AND r.\"EndDate\" IS NULL " +
            $"WHERE e.\"TenantId\"='{TenantA}' AND e.\"CurrentStatus\"=0 ORDER BY e.\"NationalId\", e.\"Id\" OFFSET 9 LIMIT 1;");
        Assert.True(Guid.TryParse(employee, out _), "50a: no active employee with an open employment record in agency A.");
        await Sql($"UPDATE \"EmploymentRecords\" SET \"LegalRegimeId\"='law50' WHERE \"EmployeeId\"='{employee}' AND \"EndDate\" IS NULL AND \"LegalRegimeId\" IS NULL; SELECT 1;");
        var day = DateTime.UtcNow.Date.AddDays(60);
        await s[Movements].PostAsync("50a-promotion", "ui/hr-movements/PromotionRequests/Create", "ui/hr-movements/PromotionRequests/Create",
        [
            new("EmployeeId", employee), new("FromGrade", "4"), new("ToGrade", "5"), new("FromStep", "1"), new("ToStep", "1"),
            new("TransitionDecisionRef", "IAM50-DEC-1"), new("EffectiveDate", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        ]);
        var promotion = await MovementsSql($"SELECT \"Id\" FROM \"PromotionRequests\" WHERE \"TransitionDecisionRef\"='IAM50-DEC-1' AND \"Status\"=0 ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(promotion, out _), "50a: the promotion was not created.");
        s.Recorder.WriteBefore(new { transfer, promotion, transferStatus = await TransferStatusAsync(transfer) });

        var list = await s[Approver].GetAsync("50a-list", "ui/hr-movements/TransferRequests");
        Assert.Contains("بانتظار الاعتماد", list);
        Assert.Contains(transfer, list, StringComparison.OrdinalIgnoreCase);
        await s[Approver].GetAsync("50a-details", $"ui/hr-movements/TransferRequests/Details/{transfer}");
        await s[Approver].PostAsync("50a-approve", $"ui/hr-movements/TransferRequests/Approve/{transfer}",
            $"ui/hr-movements/TransferRequests/Details/{transfer}", []);
        Assert.Equal("1", await TransferStatusAsync(transfer));
        Assert.Equal(approverSub, await MovementsSql($"SELECT \"ApprovedBy\"::text FROM \"TransferApprovals\" WHERE \"TransferRequestId\"='{transfer}';"));
        await s[Approver].PostAsync("50a-approve-promotion", $"ui/hr-movements/PromotionRequests/Approve/{promotion}",
            $"ui/hr-movements/PromotionRequests/Details/{promotion}", []);
        Assert.Equal("1", await MovementsSql($"SELECT \"Status\"::text FROM \"PromotionRequests\" WHERE \"Id\"='{promotion}';"));

        var transfersBefore = await MovementsSql("SELECT count(*)::text FROM \"TransferRequests\";");
        var (create, _) = await s[Approver].PostWithStatusAsync("50a-create-refused", "ui/hr-movements/TransferRequests/Create",
            $"ui/hr-movements/TransferRequests/Details/{transfer}", [new("Reason", "AC-IAM-50a refused")]);
        var (execute, _) = await s[Approver].PostWithStatusAsync("50a-execute-refused", $"ui/hr-movements/TransferRequests/Execute/{transfer}",
            $"ui/hr-movements/TransferRequests/Details/{transfer}", []);
        Assert.Equal(HttpStatusCode.Forbidden, create);
        Assert.Equal(HttpStatusCode.Forbidden, execute);
        Assert.Equal(transfersBefore, await MovementsSql("SELECT count(*)::text FROM \"TransferRequests\";"));
        Assert.Equal("1", await TransferStatusAsync(transfer));

        var other = await TransferByAsync(s, "50a-other", "a-directory-checker");
        var (creatorApproves, _) = await s[Movements].PostWithStatusAsync("50a-creator-role-refused", $"ui/hr-movements/TransferRequests/Approve/{other}",
            $"ui/hr-movements/TransferRequests/Details/{other}", []);
        Assert.Equal(HttpStatusCode.Forbidden, creatorApproves);
        Assert.Equal("0", await TransferStatusAsync(other));

        s.Checks(new { transfer, promotion, approvedBy = approverSub, create = (int)create, execute = (int)execute, creatorRoleApprove = (int)creatorApproves });
        s.Recorder.WriteResult("Pass");
    }

    /// <summary>A transfer created by another user who may create one (hr-admin).</summary>
    private static async Task<string> TransferByAsync(RecruitmentSession s, string step, string persona)
    {
        var (employee, from, to) = await MovementInputsAsync();
        var since = DateTime.UtcNow.AddSeconds(-5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        await s[persona].PostAsync(step, "ui/hr-movements/TransferRequests/Create", "ui/hr-movements/TransferRequests/Create",
        [
            new("EmployeeId", employee), new("FromOrgUnitId", from), new("ToOrgUnitId", to),
            new("RequestDate", DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("EffectiveDate", DateTime.UtcNow.Date.AddDays(60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), new("Reason", $"AC-IAM-50 {step}")
        ]);
        var id = await MovementsSql($"SELECT \"Id\" FROM \"TransferRequests\" WHERE \"Reason\"='AC-IAM-50 {step}' AND \"CreatedAt\" >= '{since}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"{step}: the transfer was not created.");
        return id;
    }

    // ---------------------------------------------------------------- 50b

    [SkippableFact]
    public async Task Ac50b_the_approver_approves_an_employment_record_and_a_correction_through_the_gateway()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "50b", "AC-IAM-50b: EmploymentRecords.Approve and EmployeeCorrections.Approve by the approver; the supervisory reader refused; a legal correction not approved by its requester",
            "central-a", Approver, "a-supervisory-reader");
        s.Steps("""
            1. central-a adds an employment record and requests a legal correction (MiddleName to its current value) for an employee.
            2. Negative: a-supervisory-reader POSTs both approvals → 403, nothing changes.
            3. a-management-approver approves both through the gateway → the record carries ApprovedBy, the correction is Approved.
            4. Negative (the rule of before): central-a requests a second legal correction and approves it himself → 409, still Pending.
            """);
        var approverSub = await ClaimAsync(Approver, "sub");
        var employee = await Sql($"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{TenantA}' AND \"CurrentStatus\"=0 ORDER BY \"NationalId\", \"Id\" OFFSET 11 LIMIT 1;");
        var middle = await Sql($"SELECT COALESCE(\"MiddleName\", '') FROM \"Employees\" WHERE \"Id\"='{employee}';");
        var title = $"AC-IAM-50b {Guid.NewGuid():N}"[..24];
        await s["central-a"].PostAsync("50b-record", "ui/hr-core/EmploymentRecords/Create", $"ui/hr-core/EmploymentRecords/Create?employeeId={employee}",
        [
            new("EmployeeId", employee), new("PositionTitle", title), new("StartDate", DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("EmploymentType", "Permanent"), new("Status", "active"), new("Notes", "AC-IAM-50b")
        ]);
        var record = await Sql($"SELECT \"Id\" FROM \"EmploymentRecords\" WHERE \"PositionTitle\"='{title}';");
        Assert.True(Guid.TryParse(record, out _), "50b: the employment record was not created.");
        var correction = await CorrectionAsync(s, "50b-correction", employee, middle);

        var (readerRecord, _) = await s["a-supervisory-reader"].PostWithStatusAsync("50b-reader-record", $"ui/hr-core/EmploymentRecords/Approve/{record}",
            "ui/hr-core/WorkInbox", []);
        var (readerCorrection, _) = await s["a-supervisory-reader"].PostWithStatusAsync("50b-reader-correction", $"ui/hr-core/EmployeeCorrections/Approve/{correction}",
            "ui/hr-core/WorkInbox", []);
        Assert.Equal(HttpStatusCode.Forbidden, readerRecord);
        Assert.Equal(HttpStatusCode.Forbidden, readerCorrection);
        Assert.Equal("", await Sql($"SELECT COALESCE(\"ApprovedBy\"::text, '') FROM \"EmploymentRecords\" WHERE \"Id\"='{record}';"));

        await s[Approver].PostAsync("50b-approve-record", $"ui/hr-core/EmploymentRecords/Approve/{record}", "ui/hr-core/Delegations/Create", []);
        Assert.Equal(approverSub, await Sql($"SELECT COALESCE(\"ApprovedBy\"::text, '') FROM \"EmploymentRecords\" WHERE \"Id\"='{record}';"));
        await s[Approver].PostAsync("50b-approve-correction", $"ui/hr-core/EmployeeCorrections/Approve/{correction}", "ui/hr-core/Delegations/Create", []);
        Assert.Equal("Approved", await CorrectionStatusAsync(correction));

        var own = await CorrectionAsync(s, "50b-own", employee, middle);
        var (ownStatus, _) = await s["central-a"].PostWithStatusAsync("50b-own-approve", $"ui/hr-core/EmployeeCorrections/Approve/{own}", "ui/hr-core/Delegations/Create", []);
        Assert.Equal(HttpStatusCode.Conflict, ownStatus);
        Assert.Equal("Pending", await CorrectionStatusAsync(own));

        s.Checks(new { record, correction, approvedBy = approverSub, readerRefused = new[] { (int)readerRecord, (int)readerCorrection }, ownLegal = (int)ownStatus });
        s.Recorder.WriteResult("Pass");
    }

    private static async Task<string> CorrectionAsync(RecruitmentSession s, string step, string employee, string middleName)
    {
        var reason = $"AC-IAM-50 {step} {Guid.NewGuid():N}"[..30];
        await s["central-a"].PostAsync(step, "ui/hr-core/EmployeeCorrections/RequestCorrection", "ui/hr-core/Delegations/Create",
        [
            new("employeeId", employee), new("reason", reason),
            new("changesJson", JsonSerializer.Serialize(new Dictionary<string, string> { ["MiddleName"] = middleName }))
        ]);
        var id = await Sql($"SELECT \"Id\" FROM \"EmployeeCorrections\" WHERE \"Reason\"='{reason}';");
        Assert.True(Guid.TryParse(id, out _), $"{step}: the correction was not requested.");
        return id;
    }

    private static async Task<string> CorrectionStatusAsync(string id)
    {
        var raw = await Sql($"SELECT \"Status\"::text FROM \"EmployeeCorrections\" WHERE \"Id\"='{id}';");
        return raw switch { "0" => "Pending", "1" => "Approved", _ => raw };
    }

    // ---------------------------------------------------------------- 50c

    [SkippableFact]
    public async Task Ac50c_the_approver_delegates_his_role_and_the_page_refuses_what_d_wf_5_forbids()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "50c", "AC-IAM-50c: a delegation of hr-management-approver for RecruitmentRequisition, 7 days, seen by both; a role not held, 120 days and a second delegation refused",
            Approver, Delegate);
        s.Steps("""
            1. a-management-approver delegates hr-management-approver for RecruitmentRequisition to central-a2 (an employee picked from the
               directory) for 7 days → it shows in «تفويضاتي» of both, with the audit row.
            2. Negative: the role hr-core, which the approver does not hold → 400; 120 days → 400; central-a2 delegates the role delegated to
               him → 400 (no chain). No row is added by any of them.
            3. The delegation is revoked at the end (Revoke with a reason).
            """);
        var delegateEmployee = await ClaimAsync(Delegate, "employee_id");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(delegateEmployee, out _),
            "رمز central-a2 لا يحمل employee_id", "central-a2's token carries no employee_id (run scripts/acceptance-reset.ps1).");
        var delegation = await DelegateAsync(s, "50c-create", Approver, delegateEmployee, "RecruitmentRequisition");
        try
        {
            Assert.Contains(delegation, await s[Approver].GetAsync("50c-list-principal", "ui/hr-core/Delegations"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(delegation, await s[Delegate].GetAsync("50c-list-delegate", "ui/hr-core/Delegations"), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("1", await Sql($"SELECT count(*)::text FROM \"AuditLogs\" WHERE \"EntityId\"='{delegation}' AND \"Action\"='ApprovalDelegationCreated';"));

            var rows = await Sql("SELECT count(*)::text FROM \"ApprovalDelegations\";");
            var (notHeld, notHeldBody) = await s[Approver].PostWithStatusAsync("50c-role-not-held", "ui/hr-core/Delegations/Create", "ui/hr-core/Delegations/Create",
                DelegationForm(delegateEmployee, "hr-core", null, 7));
            var (tooLong, tooLongBody) = await s[Approver].PostWithStatusAsync("50c-120-days", "ui/hr-core/Delegations/Create", "ui/hr-core/Delegations/Create",
                DelegationForm(delegateEmployee, ApproverRole, null, 120));
            var principalEmployee = await ClaimAsync(Approver, "employee_id");
            var (chain, chainBody) = await s[Delegate].PostWithStatusAsync("50c-no-chain", "ui/hr-core/Delegations/Create", "ui/hr-core/Delegations/Create",
                DelegationForm(principalEmployee, ApproverRole, null, 7));
            Assert.Equal(HttpStatusCode.BadRequest, notHeld);
            Assert.Contains("غير قابل للتفويض", notHeldBody);
            Assert.Equal(HttpStatusCode.BadRequest, tooLong);
            Assert.Contains("90", tooLongBody);
            Assert.Equal(HttpStatusCode.BadRequest, chain);
            Assert.Contains("لا تحمله مباشرة", chainBody);
            Assert.Equal(rows, await Sql("SELECT count(*)::text FROM \"ApprovalDelegations\";"));

            s.Checks(new { delegation, refused = new[] { (int)notHeld, (int)tooLong, (int)chain } });
        }
        finally
        {
            await RevokeAsync(s, "50c-revoke", Approver, delegation);
        }

        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 50d

    [SkippableFact]
    public async Task Ac50d_a_delegate_approves_step_two_of_a_requisition_on_behalf_and_is_refused_after_the_revocation_and_on_his_own_request()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "50d", "AC-IAM-50d: central-a2 approves step 2 on behalf of the approver (inbox badge, history, signature); refused after the revocation and on his own requisition",
            "central-a", "br1-mgr", Delegate, Approver);
        s.Steps($"""
            1. a-management-approver delegates hr-management-approver for RecruitmentRequisition to central-a2.
            2. central-a creates and submits a requisition, br1-mgr approves step 1 → the item is in central-a2's inbox with «بالنيابة عن».
            3. central-a2 approves step 2 → Approved; the last history entry names the principal (OnBehalfOfId) and the delegation; the
               signature of the step is central-a2's with the role hr-management-approver.
            4. Negative: a requisition central-a2 created himself, at step 2 → refused even with the delegation (maker rule).
            5. The delegation is revoked; a second requisition at step 2 → central-a2 refused with «{RoleMessage}», state unchanged.
            """);
        var delegateEmployee = await ClaimAsync(Delegate, "employee_id");
        var principalSub = await ClaimAsync(Approver, "sub");
        var delegateSub = await ClaimAsync(Delegate, "sub");
        var delegation = await DelegateAsync(s, "50d-delegate", Approver, delegateEmployee, "RecruitmentRequisition");
        string first, own, second;
        try
        {
            first = await AtStepTwoAsync(s, "50d-first", "central-a");
            var inbox = await InboxAsync(s[Delegate], "50d-inbox");
            Assert.True(inbox.Has($"/RecruitmentRequisitions/Details/{first}"), "50d: the principal's item is not in the delegate's inbox.");
            Assert.Contains("بالنيابة عن", inbox.Html);

            await ApproveRequisitionAsync(s[Delegate], "50d-approve-on-behalf", first);
            var state = (await RequisitionStateAsync(first)).Split('|');
            Assert.Equal("Approved", state[0]);
            var history = await Sql($"SELECT \"StepHistoryJson\" FROM \"WorkflowInstances\" WHERE \"Id\"='{state[1]}';");
            using (var json = JsonDocument.Parse(history))
            {
                var last = json.RootElement.EnumerateArray().Last();
                Assert.Equal(delegateSub, last.GetProperty("ActorId").GetString());
                Assert.Equal(principalSub, last.GetProperty("OnBehalfOfId").GetString());
                Assert.Equal(delegation, last.GetProperty("DelegationId").GetString());
            }

            var signature = await Sql(
                $"SELECT \"SignerSubject\" || '|' || \"SignerRole\" FROM \"SignatureRecords\" WHERE \"SubjectId\"='{first}' AND \"Intent\"='approve' " +
                "ORDER BY \"OccurredAtUtc\" DESC LIMIT 1;");
            Assert.Equal($"{delegateSub}|{ApproverRole}", signature);

            own = await AtStepTwoAsync(s, "50d-own", Delegate);
            await ApproveRequisitionAsync(s[Delegate], "50d-own-refused", own);
            Assert.StartsWith("PendingApproval|", await RequisitionStateAsync(own));
            Assert.EndsWith("|2|InProgress", await RequisitionStateAsync(own));
        }
        finally
        {
            await RevokeAsync(s, "50d-revoke", Approver, delegation);
        }

        second = await AtStepTwoAsync(s, "50d-second", "central-a");
        var before = await RequisitionStateAsync(second);
        var details = await s[Delegate].GetAsync("50d-after-revoke-panel", ReqDetails(second));
        Assert.Contains(RoleMessage, details);
        await ApproveRequisitionAsync(s[Delegate], "50d-after-revoke", second);
        Assert.Equal(before, await RequisitionStateAsync(second));

        await s.AuditAsync(first, own, second, delegation);
        s.Checks(new { delegation, approvedOnBehalf = first, ownRefused = own, afterRevoke = second, principal = principalSub });
        s.Recorder.WriteResult("Pass");
    }

    /// <summary>A requisition created and submitted by <paramref name="creator"/>, its step 1 approved by br1-mgr.</summary>
    private static async Task<string> AtStepTwoAsync(RecruitmentSession s, string step, string creator)
    {
        var position = await FreePoolPositionAsync(s);
        await s[creator].PostAsync($"{step}-create", "ui/hr-core/RecruitmentRequisitions/Create", "ui/hr-core/RecruitmentRequisitions/Create",
        [
            new("ScopeOrgUnitId", Br1Scope), new("FundedPositionId", position), new("JobTitle", $"وظيفة PLAN-50 {step}"),
            new("JustificationNotes", $"PLAN-50 acceptance {step}"), new("LegalSourceId", Syr001LegislationIds.Law50_2004),
            new("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)), new("PublishingChannel", "PublicOnly")
        ]);
        var id = await Sql($"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\"='{position}' AND \"Status\"='Draft';");
        Assert.True(Guid.TryParse(id, out _), $"{step}: requisition was not created.");
        await s[creator].PostAsync($"{step}-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{id}", ReqDetails(id), []);
        await ApproveRequisitionAsync(s["br1-mgr"], $"{step}-approve-1", id);
        Assert.EndsWith("|2|InProgress", await RequisitionStateAsync(id));
        return id;
    }

    // ---------------------------------------------------------------- 50e

    [SkippableFact]
    public async Task Ac50e_a_delegate_approves_a_transfer_on_behalf_and_a_delegation_of_another_type_or_a_revoked_one_does_not_reach_it()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "50e", "AC-IAM-50e: a TransferRequest delegation lets central-a2 approve a transfer on behalf; a RecruitmentRequisition one does not; after the revocation and the cache time → 403",
            Movements, Approver, Delegate);
        s.Steps("""
            1. a-management-approver delegates hr-management-approver for RecruitmentRequisition only to central-a2 → central-a2's transfers
               list and approve → 403 (the type does not match).
            2. He delegates it for TransferRequest too → within the movement service's cache time (60 s) central-a2 opens the list (200).
            3. a-movements creates a transfer; central-a2 approves it → Approved, and TransferApproval.OnBehalfOfUserId is the principal.
            4. The TransferRequest delegation is revoked → within the cache time the list answers 403 again (the wait is measured).
            """);
        var delegateEmployee = await ClaimAsync(Delegate, "employee_id");
        var principalSub = await ClaimAsync(Approver, "sub");
        var delegateSub = await ClaimAsync(Delegate, "sub");
        var otherType = await DelegateAsync(s, "50e-other-type", Approver, delegateEmployee, "RecruitmentRequisition");
        string? transferDelegation = null;
        try
        {
            var (otherTypeList, _) = await s[Delegate].GetWithStatusAsync("50e-other-type-list", "ui/hr-movements/TransferRequests");
            Assert.Equal(HttpStatusCode.Forbidden, otherTypeList);
            var transfer = await TransferAsync(s, "50e-transfer");
            Assert.Equal(HttpStatusCode.Forbidden, await BarePostAsync(Delegate, $"ui/hr-movements/TransferRequests/Approve/{transfer}"));
            Assert.Equal("0", await TransferStatusAsync(transfer));

            transferDelegation = await DelegateAsync(s, "50e-transfer-type", Approver, delegateEmployee, "TransferRequest", days: 1);
            var (granted, grantWait) = await UntilAsync(s[Delegate], "50e-list-granted", "ui/hr-movements/TransferRequests", HttpStatusCode.OK);
            Assert.Equal(HttpStatusCode.OK, granted);

            await s[Delegate].PostAsync("50e-approve-on-behalf", $"ui/hr-movements/TransferRequests/Approve/{transfer}",
                $"ui/hr-movements/TransferRequests/Details/{transfer}", []);
            Assert.Equal("1", await TransferStatusAsync(transfer));
            Assert.Equal($"{delegateSub}|{principalSub}", await MovementsSql(
                $"SELECT \"ApprovedBy\"::text || '|' || COALESCE(\"OnBehalfOfUserId\"::text, '') FROM \"TransferApprovals\" WHERE \"TransferRequestId\"='{transfer}';"));
            Assert.Contains("بالنيابة عن", await s[Delegate].GetAsync("50e-details-after", $"ui/hr-movements/TransferRequests/Details/{transfer}"));

            await RevokeAsync(s, "50e-revoke", Approver, transferDelegation);
            transferDelegation = null;
            var (revoked, revokeWait) = await UntilAsync(s[Delegate], "50e-list-after-revoke", "ui/hr-movements/TransferRequests", HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.Forbidden, revoked);

            s.Checks(new
            {
                transfer,
                onBehalfOf = principalSub,
                otherTypeList = (int)otherTypeList,
                grantedAfterSeconds = Math.Round(grantWait.TotalSeconds),
                refusedAfterRevokeSeconds = Math.Round(revokeWait.TotalSeconds)
            });
        }
        finally
        {
            if (transferDelegation is not null)
                await RevokeAsync(s, "50e-revoke-final", Approver, transferDelegation);
            await RevokeAsync(s, "50e-revoke-other", Approver, otherType);
        }

        s.Recorder.WriteResult("Pass");
    }
}
