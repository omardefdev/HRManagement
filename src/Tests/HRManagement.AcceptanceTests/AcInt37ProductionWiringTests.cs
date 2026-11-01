using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-37 (AC-INT-37a..h) on the prod-like acceptance stack (<c>scripts/acceptance-up.ps1 -ProdLike</c>), whose
/// service wiring comes from the shared compose files the production chain uses (deploy/wiring/service-wiring.json):
/// cross-service numbers, the archive consumers that never drop a message, the legal pack in every reading image,
/// explicit start-up failures, S2S scope enforcement and the wiring check. Scenarios that stop containers or run one-off
/// containers restore the stack and prove the restore.
/// </summary>
[RequiredAcceptance]
[Collection(AcInt37Collection.Name)]
public class AcInt37ProductionWiringTests
{
    private const string Ac = "AC-INT-37";
    private const string Project = "hrm-acceptance";
    private const string LabSecret = "dev-svc-secret-local-only";
    private static readonly Guid TenantA = AcceptanceSeedIds.AgencyAId;
    private static readonly string[] ComposeFiles =
    [
        "docker-compose.yml", "docker-compose.services.yml", "docker-compose.dev-profiles.yml",
        "docker-compose.full-stack-gateway.yml", "docker-compose.acceptance.yml", "docker-compose.acceptance-prodlike.yml"
    ];

    private static string RepoRoot => AcceptanceHarness.RepoRoot;
    private static string KeycloakBase => Environment.GetEnvironmentVariable("KEYCLOAK_BASE_URL") ?? "http://localhost:6388";
    private static string HrCoreDirect => Environment.GetEnvironmentVariable("HR_CORE_DIRECT_URL") ?? "http://localhost:6210";
    private static string RabbitManagement => Environment.GetEnvironmentVariable("RABBITMQ_MANAGEMENT_URL") ?? "http://localhost:15683";

    private static Task<string> Db(string database, string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);
    private static Task<long> DbCount(string database, string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(database, sql);
    private static string Since(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);

    private static async Task<string> EmployeeAsync(RecruitmentSession s, string? regime = null)
    {
        var filter = regime is null ? "" : $" AND r.\"LegalRegimeId\"='{regime}'";
        var id = await Db("hr_core_db",
            "SELECT e.\"Id\" FROM \"Employees\" e LEFT JOIN \"EmploymentRecords\" r ON r.\"EmployeeId\"=e.\"Id\" " +
            $"WHERE e.\"TenantId\"='{TenantA}'{filter} ORDER BY (e.\"Id\"='{AcceptanceSeedIds.EmpBr1Id}') DESC, e.\"CreatedAt\" LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(id, out _),
            "لا يوجد موظف مناسب في الجهة A", $"No agency-A employee{(regime is null ? "" : " with regime " + regime)} in hr_core_db.");
        return id;
    }

    /// <summary>The code was issued by HR.Core: exactly one CodeIssuances row of the kind.</summary>
    private static async Task AssertIssuedAsync(string step, string code, string kind)
    {
        Assert.False(string.IsNullOrWhiteSpace(code), $"{step}: no number was issued.");
        Assert.Equal(1, await DbCount("hr_core_db",
            $"SELECT COUNT(*) FROM \"CodeIssuances\" WHERE \"Code\"='{code.Replace("'", "''")}' AND \"EntityKind\"='{kind}';"));
    }

    private static async Task<string> WaitForAsync(string what, TimeSpan timeout, Func<Task<string>> probe)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var value = await probe();
            if (!string.IsNullOrWhiteSpace(value) || sw.Elapsed > timeout)
                return value;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    // ---------------------------------------------------------------- 37a

    [SkippableFact]
    public async Task Ac37a_leave_advance_and_separation_get_hr_core_numbers()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37a", "Leave, advance and separation numbers from HR.Core", "a-contracts-maker");
        // An employee with no leave or separation yet, so the scenario repeats (one open separation per employee).
        var used = await Db("hr_contracts_db",
            "SELECT COALESCE(string_agg(DISTINCT x::text, ','), '') FROM (SELECT \"EmployeeId\" x FROM \"SeparationRecords\" " +
            "UNION SELECT \"EmployeeId\" FROM \"LeaveRequests\" UNION SELECT \"EmployeeId\" FROM \"AdvanceRequests\") u;");
        var exclude = string.Join(",", used.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => $"'{id}'").DefaultIfEmpty("'00000000-0000-0000-0000-000000000000'"));
        var employee = await Db("hr_core_db",
            $"SELECT \"Id\" FROM \"Employees\" WHERE \"TenantId\"='{TenantA}' AND \"Id\" NOT IN ({exclude}) ORDER BY \"CreatedAt\" LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(employee, out _),
            "لا يوجد موظف بلا طلبات", "No agency-A employee without leave/separation records left (scripts/acceptance-reset.ps1).");
        var start = DateTime.UtcNow.AddSeconds(-1);
        var maker = s["a-contracts-maker"];
        var day = DateTime.UtcNow.Date.AddDays(20);

        var leaveHtml = await maker.PostAsync("leave", "ui/hr-contracts/LeaveRequests/Create", "ui/hr-contracts/LeaveRequests/Create",
        [
            new("EmployeeId", employee), new("LeaveType", "Annual"), new("StartDate", day.ToString("yyyy-MM-dd")),
            new("EndDate", day.AddDays(2).ToString("yyyy-MM-dd")), new("DurationDays", "3"), new("Notes", "AC-INT-37a")
        ]);
        var advanceHtml = await maker.PostAsync("advance", "ui/hr-contracts/AdvanceRequests/Create", "ui/hr-contracts/AdvanceRequests/Create",
            [new("EmployeeId", employee), new("Amount", "1000"), new("Reason", "AC-INT-37a")]);
        var separationHtml = await maker.PostAsync("separation", "ui/hr-contracts/SeparationWizard/Step1", "ui/hr-contracts/SeparationWizard/Step1",
        [
            new("EmployeeId", employee), new("SeparationType", "Resignation"), new("EffectiveDate", day.AddDays(30).ToString("yyyy-MM-dd")),
            new("Reason", "AC-INT-37a")
        ]);

        var rows = (await Db("hr_contracts_db",
            "SELECT string_agg(\"TransactionType\"::text || '|' || COALESCE(\"ReferenceNumber\",''), E'\\n' ORDER BY \"CreatedAt\") " +
            $"FROM \"ServiceTransactions\" WHERE \"EmployeeId\"='{employee}' AND \"CreatedAt\" >= '{Since(start)}';"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Split('|'))
            .ToList();
        var checks = new List<object>();
        Assert.True(rows.Count >= 3, $"expected three service transactions (leave, advance, separation), found {rows.Count}: " +
            $"leave [{FormErrors(leaveHtml)}] advance [{FormErrors(advanceHtml)}] separation [{FormErrors(separationHtml)}]");
        foreach (var r in rows)
        {
            await AssertIssuedAsync($"37a-{r[0]}", r[1], "ServiceTransaction");
            checks.Add(new { transactionType = r[0], referenceNumber = r[1], codeIssuance = 1 });
        }

        s.Steps("1. a-contracts-maker: leave request, advance request, separation (SeparationWizard/Step1, Resignation).\n" +
                "2. Every new ServiceTransaction has a ReferenceNumber and one hr_core_db.CodeIssuances row of kind ServiceTransaction.");
        s.Checks(new { employee, transactions = checks });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 37b

    [SkippableFact]
    public async Task Ac37b_prod_like_journey_appointment_has_a_number_and_a_decision_number()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37b", "Recruitment-to-hire journey on the prod-like stack: appointment and decision numbers",
            "a-contracts-maker");
        // The journey itself is RecruitmentToHireJourneyProdLikeE2ETests (HRManagement.E2E, Category=JourneyProdLike),
        // run on this stack before this scenario; its evidence is acceptance-evidence/AC-HCM-33/prodlike.
        var prodLike = await AcceptanceStack.ContainerEnvAsync("hr-core-web", "NationalScope__IsLabScale");
        AcceptanceGate.RequireData(s.Recorder, prodLike == "false",
            "المكدس ليس شبيهاً بالإنتاج", "hr-core-web is not on the prod-like profile (scripts/acceptance-up.ps1 -ProdLike).");
        var txn = await Db("hr_contracts_db",
            "SELECT \"Id\" || '|' || COALESCE(\"ReferenceNumber\",'') || '|' || \"EmployeeId\" FROM \"ServiceTransactions\" " +
            "WHERE \"TransactionType\"='Appointment' AND \"AppointmentDecisionId\" IS NULL AND \"CreatedAt\" > now() - interval '12 hours' " +
            "ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, txn.Contains('|'),
            "لم تُشغّل رحلة التوظيف الشبيهة بالإنتاج", "No fresh Appointment transaction: run Category=JourneyProdLike (HRManagement.E2E) first.");
        var t = txn.Split('|');
        await AssertIssuedAsync("37b-appointment", t[1], "ServiceTransaction");

        // As in AC-HCM-29g: the steps before the decision belong to other plans; mark them done (test-only precondition).
        await Db("hr_contracts_db",
            $"UPDATE \"ServiceTransactions\" SET \"State\"='Executing' WHERE \"Id\"='{t[0]}';" +
            "UPDATE \"HireChecklists\" SET \"IsCompleted\"=true, \"CompletedAt\"=now(), \"EvidenceRef\"=COALESCE(\"EvidenceRef\",'int037:precondition') " +
            $"WHERE \"ServiceTransactionId\"='{t[0]}' AND \"Step\" NOT IN ('AppointmentDecisionIssued','PreEmploymentMedical','HireArchived');");
        await s["a-contracts-maker"].PostAsync("decision", "ui/hr-contracts/HireWizard/IssueAppointmentDecision",
            $"ui/hr-contracts/HireWizard/Step2?transactionId={t[0]}", [new("transactionId", t[0])]);
        var decision = await Db("hr_contracts_db",
            $"SELECT d.\"DecisionNumber\" FROM \"ServiceTransactions\" tx JOIN \"AdministrativeDecisions\" d ON d.\"Id\"=tx.\"AppointmentDecisionId\" WHERE tx.\"Id\"='{t[0]}';");
        await AssertIssuedAsync("37b-decision", decision, "AdministrativeDecision");

        s.Steps("1. RecruitmentToHireJourneyProdLikeE2ETests ran on the prod-like stack (no wiring from an acceptance file).\n" +
                "2. Its Appointment transaction has a number with a CodeIssuances row; IssueAppointmentDecision issues a decision number with its row.");
        s.Checks(new { appointment = t[0], reference = t[1], decisionNumber = decision });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 37c

    [SkippableFact]
    public async Task Ac37c_card_device_case_and_asset_get_hr_core_numbers()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37c", "ID card, IT device, legal case and asset numbers from HR.Core",
            "a-idcard", "a-it", "a-compliance", "a-logistics-maker");
        var employee = await EmployeeAsync(s);
        var start = DateTime.UtcNow.AddSeconds(-1);
        var today = DateTime.UtcNow.Date;
        var checks = new List<object>();

        // The card is issued through Renew (a new card with a new HR.Core serial). IssuedCards/Create cannot be used:
        // it binds the IssuedCard entity, whose required CardDesign navigation always fails model validation
        // (pre-existing IDCard defect, reported in the PLAN-37 final report).
        var card = await Db("idcard_db", $"SELECT \"Id\" FROM \"IssuedCards\" WHERE \"TenantId\"='{TenantA}' AND \"Status\"=0 ORDER BY \"CreatedAt\" LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(card, out _), "لا توجد بطاقة نشطة", "No active card for agency A in idcard_db.");
        await s["a-idcard"].PostAsync("card", $"ui/idcard/IssuedCards/Renew/{card}", $"ui/idcard/IssuedCards/Details/{card}", [new("id", card)]);
        var serial = await Db("idcard_db", $"SELECT \"SerialNumber\" FROM \"IssuedCards\" WHERE \"CreatedAt\" >= '{Since(start)}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        await AssertIssuedAsync("37c-card", serial, "IdCardSerial");
        checks.Add(new { idCardSerial = serial });

        await s["a-it"].PostAsync("device", "ui/it-infra/UserDevices/Create", "ui/it-infra/UserDevices/Create",
            [new("AssignedTo", employee), new("DeviceType", "Laptop"), new("SerialNumber", $"INT37-{Guid.NewGuid():N}"[..20]), new("AssetTag", "PENDING"), new("Status", "InUse")]);
        var tag = await Db("it_infra_db", $"SELECT \"AssetTag\" FROM \"UserDevices\" WHERE \"CreatedAt\" >= '{Since(start)}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        await AssertIssuedAsync("37c-device", tag, "ItAssetTag");
        checks.Add(new { itAssetTag = tag });

        await s["a-compliance"].PostAsync("case", "ui/compliance/LegalCases/Create", "ui/compliance/LegalCases/Create",
            [new("CaseNumber", "PENDING"), new("Subject", "AC-INT-37c"), new("Parties", "AC-INT-37"), new("Status", "Open"), new("CourtDetails", "-")]);
        var caseNumber = await Db("compliance_db", $"SELECT \"CaseNumber\" FROM \"LegalCases\" WHERE \"CreatedAt\" >= '{Since(start)}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        await AssertIssuedAsync("37c-case", caseNumber, "LegalCase");
        checks.Add(new { legalCase = caseNumber });

        await s["a-logistics-maker"].PostAsync("asset", "ui/admin-logistics/Assets/CreateLegacy", "ui/admin-logistics/Assets/CreateLegacy",
        [
            new("Name", "AC-INT-37c laptop"), new("Category", "IT"), new("Brand", "Lab"),
            new("PurchaseDate", today.AddDays(-30).ToString("yyyy-MM-dd")), new("Value", "500"), new("Status", "InStorage"),
            new("LegacyEntryReason", "PLAN-37 acceptance: legacy asset entered to prove the HR.Core asset tag")
        ]);
        var asset = await Db("admin_db", $"SELECT \"TagNumber\" FROM \"Assets\" WHERE \"CreatedAt\" >= '{Since(start)}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        await AssertIssuedAsync("37c-asset", asset, "AssetTag");
        checks.Add(new { assetTag = asset });

        s.Steps("1. a-idcard issues a card, a-it registers a device, a-compliance opens a legal case, a-logistics-maker enters an asset.\n" +
                "   (the card through IssuedCards/Renew: IssuedCards/Create cannot bind its entity, a pre-existing defect).\n" +
                "2. Each gets its number from HR.Core (svc-idcard / svc-it-infra / svc-compliance / svc-admin-logistics, scope hr-core.workflow) with one CodeIssuances row.");
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 37d

    [SkippableFact]
    public async Task Ac37d_leave_approval_is_archived_and_survives_hr_core_down()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37d", "Archive reference: positive within 60 s; HR.Core down = retry and _error, replay = one row",
            "a-contracts-maker", "a-contracts-checker");
        // Approval checks the annual balance of the leave's year: the agency-A employee with the most days left this year.
        var employee = await Db("hr_contracts_db",
            $"SELECT \"EmployeeId\" FROM \"LeaveBalances\" WHERE \"TenantId\"='{TenantA}' AND \"LeaveType\"='Annual' AND \"Year\"={DateTime.UtcNow.Year} " +
            "AND \"TotalEntitlement\" - \"Used\" >= 4 ORDER BY \"TotalEntitlement\" - \"Used\" DESC, \"EmployeeId\" LIMIT 1;");
        AcceptanceGate.RequireData(s.Recorder, Guid.TryParse(employee, out _),
            "لا يوجد رصيد إجازات كافٍ", "No agency-A employee with 4 annual leave days left this year (scripts/acceptance-reset.ps1).");

        // Positive.
        var first = await CreateLeaveAsync(s, employee, "37d-1", 14);
        await ApproveLeaveAsync(s, first, "37d-1-approve");
        var archived = await WaitForAsync("archive", TimeSpan.FromSeconds(60), () => ArchiveReferenceAsync(first));
        Assert.False(string.IsNullOrWhiteSpace(archived), "the approved leave was not archived within 60 seconds");
        await AssertIssuedAsync("37d-archive", archived, "ArchiveReference");

        // Negative: HR.Core stopped while the approval is archived.
        var second = await CreateLeaveAsync(s, employee, "37d-2", 18);
        var hrCore = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("hr-core-web");
        string errorQueue;
        long dead;
        try
        {
            await AcceptanceMessagingHelper.RunProcessAsync("docker", "stop", hrCore);
            await ApproveLeaveAsync(s, second, "37d-2-approve");
            errorQueue = await WaitForAsync("error queue", TimeSpan.FromMinutes(3), async () =>
            {
                var q = await LeaveErrorQueueAsync();
                return q is not null && q.Value.Messages > 0 ? q.Value.Name : "";
            });
            Assert.False(string.IsNullOrWhiteSpace(errorQueue), "the leave approval did not reach the archive consumer's _error queue (dropped?)");
            dead = (await LeaveErrorQueueAsync())!.Value.Messages;
            Assert.Equal(0, await DbCount("archive_db", $"SELECT COUNT(*) FROM \"ArchiveItems\" WHERE \"Title\" LIKE '%[{Guid.Parse(second):N}]%';"));
        }
        finally
        {
            await AcceptanceMessagingHelper.RunProcessAsync("docker", "start", hrCore);
        }

        await WaitHealthyAsync("hr-core-web", TimeSpan.FromMinutes(4));
        var moved = await ReplayErrorQueueAsync(errorQueue);
        var replayed = await WaitForAsync("archive after replay", TimeSpan.FromSeconds(90), () => ArchiveReferenceAsync(second));
        Assert.False(string.IsNullOrWhiteSpace(replayed), "the replayed approval was not archived");
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await DbCount("archive_db", $"SELECT COUNT(*) FROM \"ArchiveItems\" WHERE \"Title\" LIKE '%[{Guid.Parse(second):N}]%';"));

        s.Steps("1. Leave created (UI) and its approval event ILeaveApproved published as HR.Contracts does: an ArchiveItem with an HR.Core ArchiveReference appears within 60 s.\n" +
                "2. hr-core-web stopped; a second leave approved: no ArchiveItem, the message is retried and lands in the archive consumer's _error queue.\n" +
                "3. hr-core-web started and healthy; the _error messages are moved back to the consumer queue (replay): exactly one ArchiveItem.");
        s.Checks(new { positive = new { leave = first, reference = archived }, negative = new { leave = second, errorQueue, deadLettered = dead, replayed = moved, reference = replayed, rows = 1 } });
        s.Recorder.WriteResult("Pass");
    }

    private static async Task<string> CreateLeaveAsync(RecruitmentSession s, string employee, string step, int offsetDays)
    {
        var start = DateTime.UtcNow.AddSeconds(-1);
        // Same calendar year as the balance; past any minimum notice.
        var day = DateTime.UtcNow.Date.AddDays(offsetDays);
        if (day.Year != DateTime.UtcNow.Year) day = new DateTime(DateTime.UtcNow.Year, 12, 20).AddDays(offsetDays % 7);
        await s["a-contracts-maker"].PostAsync(step, "ui/hr-contracts/LeaveRequests/Create", "ui/hr-contracts/LeaveRequests/Create",
        [
            new("EmployeeId", employee), new("LeaveType", "Annual"), new("StartDate", day.ToString("yyyy-MM-dd")),
            new("EndDate", day.AddDays(1).ToString("yyyy-MM-dd")), new("DurationDays", "2"), new("Notes", $"AC-INT-{step}")
        ]);
        var id = await Db("hr_contracts_db",
            $"SELECT \"Id\" FROM \"LeaveRequests\" WHERE \"EmployeeId\"='{employee}' AND \"CreatedAt\" >= '{Since(start)}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"{step}: leave request not created");
        return id;
    }

    /// <summary>
    /// The approval event of a leave created through the UI, exactly as HR.Contracts publishes it after an approval
    /// (EventPublisher.PublishLeaveApprovedAsync: ILeaveApproved with the tenant header). The UI approval itself cannot be
    /// used: ApproveLeaveRequestHandler claims the row with ExecuteUpdate and then saves the tracked entity against the
    /// old xmin, so every approval on PostgreSQL ends in a DbUpdateConcurrencyException (pre-existing HR.Contracts
    /// defect, reported in the PLAN-37 final report).
    /// </summary>
    private static async Task ApproveLeaveAsync(RecruitmentSession s, string leaveId, string step)
    {
        var row = (await Db("hr_contracts_db",
            "SELECT l.\"EmployeeId\" || '|' || COALESCE(l.\"ServiceTransactionId\"::text,'') || '|' || l.\"StartDate\"::text || '|' || l.\"EndDate\"::text " +
            $"|| '|' || l.\"DurationDays\" FROM \"LeaveRequests\" l WHERE l.\"Id\"='{leaveId}';")).Split('|');
        var (bus, provider) = await AcceptanceMessagingHelper.ConnectBusAsync();
        try
        {
            await provider.GetRequiredService<MassTransit.IPublishEndpoint>().Publish<HRManagement.MessageContracts.Events.ILeaveApproved>(new
            {
                TransactionId = Guid.TryParse(row[1], out var txn) ? txn : Guid.NewGuid(),
                LeaveRequestId = Guid.Parse(leaveId),
                EmployeeId = Guid.Parse(row[0]),
                ApprovedBy = Guid.NewGuid(),
                OccurredAt = DateTime.UtcNow,
                CorrelationId = $"AC-INT-{step}",
                SourceSystem = "HR.Contracts",
                LeaveType = "Annual",
                StartDate = DateTime.Parse(row[2], CultureInfo.InvariantCulture),
                EndDate = DateTime.Parse(row[3], CultureInfo.InvariantCulture),
                DurationDays = int.Parse(row[4], CultureInfo.InvariantCulture),
                TenantId = TenantA
            }, ctx => ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantA));
            s.Recorder.CaptureHttp($"37d-{step}-publish", "AMQP", "ILeaveApproved", 0, new { leaveId, employee = row[0] });
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable d) await d.DisposeAsync();
        }
    }

    private static Task<string> ArchiveReferenceAsync(string leaveId) =>
        Db("archive_db", $"SELECT \"ReferenceNumber\" FROM \"ArchiveItems\" WHERE \"Title\" LIKE '%[{Guid.Parse(leaveId):N}]%' LIMIT 1;");

    private static HttpClient RabbitClient()
    {
        var user = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER") ?? DotEnv("RABBITMQ_DEFAULT_USER") ?? "guest";
        var pass = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS") ?? DotEnv("RABBITMQ_DEFAULT_PASS") ?? "guest";
        var http = new HttpClient { BaseAddress = new Uri(RabbitManagement.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));
        return http;
    }

    private static string? DotEnv(string name)
    {
        var path = Path.Combine(RepoRoot, ".env");
        if (!File.Exists(path)) return null;
        var line = File.ReadLines(path).FirstOrDefault(l => l.StartsWith(name + "=", StringComparison.Ordinal));
        return line?[(name.Length + 1)..].Trim().Trim('"');
    }

    /// <summary>The archive leave consumer's error queue (MassTransit: &lt;endpoint&gt;_error) and its message count.</summary>
    private static async Task<(string Name, long Messages)?> LeaveErrorQueueAsync()
    {
        using var http = RabbitClient();
        using var doc = JsonDocument.Parse(await http.GetStringAsync("api/queues/%2F?columns=name,messages"));
        foreach (var q in doc.RootElement.EnumerateArray())
        {
            var name = q.GetProperty("name").GetString()!;
            if (name.EndsWith("_error", StringComparison.Ordinal) && name.Contains("leave", StringComparison.OrdinalIgnoreCase)
                && name.Contains("archive", StringComparison.OrdinalIgnoreCase))
                return (name, q.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt64() : 0);
        }

        // Endpoint names without the service prefix: the consumer is LeaveApprovedConsumer of Records.Archive.
        foreach (var q in doc.RootElement.EnumerateArray())
        {
            var name = q.GetProperty("name").GetString()!;
            if (name.EndsWith("_error", StringComparison.Ordinal) && name.Contains("leave-approved", StringComparison.OrdinalIgnoreCase))
                return (name, q.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt64() : 0);
        }

        return null;
    }

    /// <summary>ErrorQueueReplay on the broker: every message of <paramref name="errorQueue"/> back to its consumer queue, headers kept.</summary>
    private static async Task<int> ReplayErrorQueueAsync(string errorQueue)
    {
        var target = errorQueue[..^"_error".Length];
        using var http = RabbitClient();
        var resp = await http.PostAsJsonAsync($"api/queues/%2F/{Uri.EscapeDataString(errorQueue)}/get",
            new { count = 50, ackmode = "ack_requeue_false", encoding = "auto", truncate = 5_000_000 });
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var moved = 0;
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var properties = JsonSerializer.Deserialize<Dictionary<string, object>>(m.GetProperty("properties").GetRawText()) ?? [];
            var publish = await http.PostAsJsonAsync("api/exchanges/%2F/amq.default/publish", new
            {
                properties,
                routing_key = target,
                payload = m.GetProperty("payload").GetString(),
                payload_encoding = m.GetProperty("payload_encoding").GetString()
            });
            publish.EnsureSuccessStatusCode();
            moved++;
        }

        return moved;
    }

    private static async Task WaitHealthyAsync(string service, TimeSpan timeout)
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(service);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var (_, health) = await Docker("inspect", "-f", "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}", container);
            if (health.Trim() is "healthy" or "none") return;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        Assert.Fail($"{service} did not become healthy within {timeout}");
    }

    // ---------------------------------------------------------------- 37e

    [SkippableFact]
    public async Task Ac37e_legal_pack_is_loaded_with_the_repository_fingerprint_and_its_absence_stops_the_service()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37e", "Legal pack: fingerprint in three services, separation/transfer/promotion work; a missing pack stops the service",
            "a-contracts-maker", "a-movements");
        var sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(RepoRoot, "docs", "syr", "service-regimes.json")))).ToLowerInvariant();
        var lines = new Dictionary<string, string>();
        foreach (var service in new[] { "hr-core-web", "hr-contracts-web", "hr-movements-web" })
        {
            var (_, logs) = await Docker("logs", await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(service));
            var line = Regex.Matches(logs, @"LegalPack loaded pack=syr003-service-regimes-v1 source=\w+ sha256=([0-9a-f]{64})[^\r\n]*").LastOrDefault();
            Assert.True(line is not null, $"{service}: no 'LegalPack loaded' start-up line");
            Assert.Equal(sha, line!.Groups[1].Value);
            lines[service] = line.Value;
        }

        // The rules are not empty: a Resignation for a law50 employee, a transfer and a promotion are accepted.
        var employee = await EmployeeAsync(s, "law50");
        var start = DateTime.UtcNow.AddSeconds(-1);
        var day = DateTime.UtcNow.Date.AddDays(60);
        await s["a-contracts-maker"].PostAsync("resignation", "ui/hr-contracts/SeparationWizard/Step1", "ui/hr-contracts/SeparationWizard/Step1",
            [new("EmployeeId", employee), new("SeparationType", "Resignation"), new("EffectiveDate", day.ToString("yyyy-MM-dd")), new("Reason", "AC-INT-37e")]);
        Assert.Equal(1, await DbCount("hr_contracts_db",
            $"SELECT COUNT(*) FROM \"SeparationRecords\" WHERE \"EmployeeId\"='{employee}' AND \"CreatedAt\" >= '{Since(start)}';"));

        var orgUnits = (await Db("hr_core_db",
            $"SELECT string_agg(\"Id\"::text, ',' ORDER BY \"Code\") FROM (SELECT \"Id\",\"Code\" FROM \"OrgUnits\" WHERE \"TenantId\"='{TenantA}' LIMIT 2) o;")).Split(',');
        await s["a-movements"].PostAsync("transfer", "ui/hr-movements/TransferRequests/Create", "ui/hr-movements/TransferRequests/Create",
        [
            new("EmployeeId", employee), new("FromOrgUnitId", orgUnits[0]), new("ToOrgUnitId", orgUnits[^1]),
            new("RequestDate", DateTime.UtcNow.Date.ToString("yyyy-MM-dd")), new("EffectiveDate", day.ToString("yyyy-MM-dd")), new("Reason", "AC-INT-37e")
        ]);
        Assert.Equal(1, await DbCount("hr_movements_db",
            $"SELECT COUNT(*) FROM \"TransferRequests\" WHERE \"EmployeeId\"='{employee}' AND \"CreatedAt\" >= '{Since(start)}';"));
        var promotionHtml = await s["a-movements"].PostAsync("promotion", "ui/hr-movements/PromotionRequests/Create", "ui/hr-movements/PromotionRequests/Create",
        [
            new("EmployeeId", employee), new("FromGrade", "4"), new("ToGrade", "5"), new("FromStep", "1"), new("ToStep", "1"),
            new("TransitionDecisionRef", "INT37-DEC-1"), new("EffectiveDate", day.ToString("yyyy-MM-dd"))
        ]);
        Assert.True(1 == await DbCount("hr_movements_db",
            $"SELECT COUNT(*) FROM \"PromotionRequests\" WHERE \"EmployeeId\"='{employee}' AND \"CreatedAt\" >= '{Since(start)}';"),
            "37e-promotion: " + FormErrors(promotionHtml));

        // Negative: a configured pack path that does not exist stops hr-movements-web (one-off container).
        var (exit, output) = await OneOffAsync("hr-movements-web", ["LegalPacks__ServiceRegimesPath=/nonexistent.json"], TimeSpan.FromMinutes(3));
        Assert.NotEqual(0, exit);
        Assert.Contains("LegalPacks:ServiceRegimesPath", output);
        Assert.Contains("syr003-service-regimes-v1", output);

        s.Steps("1. hr-core-web, hr-contracts-web and hr-movements-web log 'LegalPack loaded ... sha256=' with the repository file's SHA-256.\n" +
                "2. A Resignation for a law50 employee, a transfer and a promotion are created (the rules are not empty).\n" +
                "3. A one-off hr-movements-web with LegalPacks__ServiceRegimesPath=/nonexistent.json exits with a message naming the key and the pack.");
        s.Checks(new { sha256 = sha, lines, employee, negative = new { exit, named = true } });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 37f

    [SkippableFact]
    public async Task Ac37f_missing_or_localhost_hr_core_url_stops_hr_contracts_at_start_up()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37f", "HR.Contracts refuses to start without its HR.Core URL or with localhost", "a-contracts-maker");

        var (emptyExit, emptyOut) = await OneOffAsync("hr-contracts-web", ["Integration__HrCoreBaseUrl="], TimeSpan.FromMinutes(3));
        Assert.NotEqual(0, emptyExit);
        Assert.Contains("Integration:HrCoreBaseUrl is required", emptyOut);

        var (localExit, localOut) = await OneOffAsync("hr-contracts-web", ["Integration__HrCoreBaseUrl=http://localhost:5010"], TimeSpan.FromMinutes(3));
        Assert.NotEqual(0, localExit);
        Assert.Contains("Docker service DNS", localOut);

        // The running service was not touched.
        var (status, _) = await HttpGetStatusAsync($"{E2eEnv.BaseUrl.TrimEnd('/')}/ui/hr-contracts/health/ready");
        s.Steps("1. One-off hr-contracts-web with Integration__HrCoreBaseUrl= (empty): exits at start-up naming the key.\n" +
                "2. One-off hr-contracts-web with http://localhost:5010: exits with 'must use Docker service DNS'.\n" +
                "3. The running stack is unchanged.");
        s.Checks(new { empty = new { exit = emptyExit, message = Excerpt(emptyOut, "Integration:HrCoreBaseUrl") }, localhost = new { exit = localExit, message = Excerpt(localOut, "Docker service DNS") }, liveReady = (int)status });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- 37g

    [SkippableFact]
    public async Task Ac37g_code_generation_needs_the_workflow_scope()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37g", "S2S: svc-hr-contracts may generate a number, svc-hr-health may not", "a-contracts-maker");
        var countSql = "SELECT COUNT(*) FROM \"CodeIssuances\" WHERE \"EntityKind\"='ServiceTransaction';";

        var contractsToken = await ServiceTokenAsync("svc-hr-contracts", "hr-core.workflow hr-core.lookup file-storage.references");
        var before = await DbCount("hr_core_db", countSql);
        var (okStatus, okBody) = await GenerateAsync(contractsToken);
        Assert.Equal(HttpStatusCode.OK, okStatus);
        var code = JsonDocument.Parse(okBody).RootElement.GetProperty("code").GetString()!;
        await AssertIssuedAsync("37g-positive", code, "ServiceTransaction");
        Assert.Equal(before + 1, await DbCount("hr_core_db", countSql));

        var healthToken = await ServiceTokenAsync("svc-hr-health", null);
        var beforeDenied = await DbCount("hr_core_db", countSql);
        var (deniedStatus, _) = await GenerateAsync(healthToken);
        // 401 rather than 403: without hr-core.workflow (or any matrix scope) the token carries no api-hr-core audience,
        // so HR.Core refuses it at authentication, before the policy. Either way no number is issued.
        Assert.True(deniedStatus is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"svc-hr-health got {(int)deniedStatus}");
        Assert.Equal(beforeDenied, await DbCount("hr_core_db", countSql));

        s.Recorder.CaptureHttp("37g-generate-svc-hr-contracts", "POST", "/api/internal/codes/generate", (int)okStatus, new { code });
        s.Recorder.CaptureHttp("37g-generate-svc-hr-health", "POST", "/api/internal/codes/generate", (int)deniedStatus, null);
        s.Steps("1. client_credentials token for svc-hr-contracts with its matrix scopes; POST /api/internal/codes/generate (X-Tenant-ID) = 200 and one CodeIssuances row.\n" +
                "2. Token for svc-hr-health (no hr-core.workflow, hence no api-hr-core audience): refused (401) and the CodeIssuances count is unchanged.");
        s.Checks(new { positive = new { status = (int)okStatus, code, issuancesBefore = before }, negative = new { status = (int)deniedStatus, issuancesUnchanged = beforeDenied } });
        s.Recorder.WriteResult("Pass");
    }

    private static async Task<string> ServiceTokenAsync(string clientId, string? scope)
    {
        using var http = new HttpClient();
        var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials"), new("client_id", clientId), new("client_secret", LabSecret) };
        if (scope is not null) form.Add(new("scope", scope));
        var resp = await http.PostAsync($"{KeycloakBase.TrimEnd('/')}/realms/hrmanagement/protocol/openid-connect/token", new FormUrlEncodedContent(form));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"token for {clientId} failed: {(int)resp.StatusCode}");
        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<(HttpStatusCode Status, string Body)> GenerateAsync(string token)
    {
        using var http = new HttpClient { BaseAddress = new Uri(HrCoreDirect.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add(TenantIdMiddleware.HeaderName, TenantA.ToString("D"));
        var request = new CodeGenerationRequest { EntityKind = CodeEntityKind.ServiceTransaction, TenantId = TenantA, Preview = false };
        var resp = await http.PostAsJsonAsync("api/internal/codes/generate", request);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- 37h

    [SkippableFact]
    public async Task Ac37h_rendered_wiring_check_passes_and_reports_a_missing_key()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await RecruitmentAcceptance.StartAsync(Ac, "37h", "int037-wiring-check.ps1 on compose production, prod-like and k8s; negative copy", "a-contracts-maker");

        var (exit, output) = await PowerShellAsync(Path.Combine(RepoRoot, "scripts", "int037-wiring-check.ps1"), $"-RepoRoot \"{RepoRoot}\"");
        Assert.True(exit == 0, output);
        var report = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "artifacts", "int037", "wiring-report.json"));
        Assert.Contains("\"ok\":  true", report);

        // Negative: the same check on a copy whose shared compose file lost hr-contracts-web's HR.Core URL.
        var copy = Path.Combine(Path.GetTempPath(), "int037-wiring-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in Directory.GetFiles(RepoRoot, "docker-compose*.yml"))
                File.Copy(file, Path.Combine(copy, Path.GetFileName(file)).Also(_ => Directory.CreateDirectory(copy)));
            foreach (var rel in new[] { "deploy/wiring/service-wiring.json", "deploy/vps/.env.vps.example", "scripts/vps/compose-chains.ps1", "scripts/int037-wiring-check.ps1" })
            {
                var target = Path.Combine(copy, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(RepoRoot, rel.Replace('/', Path.DirectorySeparatorChar)), target);
            }

            var compose = Path.Combine(copy, "docker-compose.yml");
            var text = await File.ReadAllTextAsync(compose);
            var broken = Regex.Replace(text, @"(?m)^      Integration__HrCoreBaseUrl: http://hr-core-web:8080\r?\n(?=      HrGateway__InternalBaseUrl)", "");
            Assert.NotEqual(text, broken);
            await File.WriteAllTextAsync(compose, broken);
            var (badExit, badOutput) = await PowerShellAsync(Path.Combine(copy, "scripts", "int037-wiring-check.ps1"), $"-RepoRoot \"{copy}\" -SkipKubernetes");
            Assert.Equal(1, badExit);
            Assert.Contains("hr-contracts-web lacks Integration__HrCoreBaseUrl", badOutput);

            s.Steps("1. scripts/int037-wiring-check.ps1: production, single-host, prod-like, parity and the k8s overlays PASS.\n" +
                    "2. On a copy without hr-contracts-web's Integration__HrCoreBaseUrl it exits 1 naming the service and the key\n" +
                    "   (ProductionWiringMatrixTests.Checker_reports_a_missing_url_with_service_and_key is the same check in unit tests).");
            s.Checks(new { passed = Excerpt(output, "[PASS]"), negative = Excerpt(badOutput, "lacks Integration__HrCoreBaseUrl") });
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            if (Directory.Exists(copy)) Directory.Delete(copy, recursive: true);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The validation messages of a re-rendered form (empty when the post went through).</summary>
    private static string FormErrors(string html) => string.Join(" | ", Regex.Matches(html, @"(?s)class=""[^""]*(?:field-validation-error|validation-summary-errors|text-danger|alert-danger)[^""]*""[^>]*>(.*?)</(?:span|div|ul)>")
            .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]+>", " ").Trim())
            .Where(t => t.Length > 0)
            .Distinct());


    private static string Excerpt(string text, string marker)
    {
        var i = text.IndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? "" : text.Substring(Math.Max(0, i - 80), Math.Min(text.Length - Math.Max(0, i - 80), 260)).Replace("\r", "");
    }

    private static async Task<(HttpStatusCode, string)> HttpGetStatusAsync(string url)
    {
        using var http = new HttpClient();
        var resp = await http.GetAsync(url);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<(int Exit, string Output)> Docker(params string[] args)
    {
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await stdout + await stderr);
    }

    /// <summary>
    /// A one-off container of <paramref name="service"/> with extra environment (docker compose run --no-deps, the
    /// running service untouched). Returns its exit code and output; a container still running after the timeout is
    /// stopped and reported as exit 0 (it started).
    /// </summary>
    private static async Task<(int Exit, string Output)> OneOffAsync(string service, string[] environment, TimeSpan timeout)
    {
        var name = $"int037-{service}-{Guid.NewGuid():N}"[..40];
        var args = new List<string> { "compose", "-p", Project, "--project-directory", RepoRoot };
        foreach (var f in ComposeFiles) args.AddRange(["-f", Path.Combine(RepoRoot, f)]);
        args.AddRange(["--profile", "full-stack", "run", "-d", "--no-deps", "--name", name]);
        foreach (var e in environment) args.AddRange(["-e", e]);
        args.Add(service);
        var (startExit, startOut) = await Docker([.. args]);
        Assert.True(startExit == 0, $"docker compose run {service} failed: {startOut}");
        try
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                var (_, state) = await Docker("inspect", "-f", "{{.State.Status}} {{.State.ExitCode}}", name);
                var parts = state.Trim().Split(' ');
                if (parts[0] == "exited")
                {
                    var (_, logs) = await Docker("logs", name);
                    return (int.Parse(parts[1], CultureInfo.InvariantCulture), logs);
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            var (_, running) = await Docker("logs", name);
            return (0, running);
        }
        finally
        {
            await Docker("rm", "-f", name);
        }
    }

    private static async Task<(int Exit, string Output)> PowerShellAsync(string script, string arguments)
    {
        var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await stdout + await stderr);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcInt37Collection
{
    public const string Name = "AC-INT-37 (stops and runs containers)";
}

internal static class AcInt37Extensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
