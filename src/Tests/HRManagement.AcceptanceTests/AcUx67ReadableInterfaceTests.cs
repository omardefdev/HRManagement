using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-67 (UX-A, AC-UX-67a..f) on the prod-like stack, which enforces its Content-Security-Policy as Production does. A real Chromium
/// (installed by the stage job: <c>"browser": true</c> in <c>tests/acceptance/ci-scope.json</c>) opens the pages with a
/// <c>securitypolicyviolation</c> listener registered before each page's markup is parsed, counts the policies of each response, reads
/// computed styles and takes the screenshots. Nothing here stops or recreates a container, and what a scenario creates it removes or
/// cancels through the application.
/// </summary>
[RequiredAcceptance]
public class AcUx67ReadableInterfaceTests
{
    private const string Ac = "AC-UX-67";
    private const string Core = "hr_core_db";
    private const string Gateway = "gateway_audit_db";

    private static readonly string AgencyA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly string Br1Scope = AcceptanceSeedIds.Br1OrgUnitId.ToString("D");
    private static readonly string BaseUrl = (Environment.GetEnvironmentVariable("GATEWAY_BASE_URL") ?? "http://localhost:6200").TrimEnd('/');
    private static readonly Regex Guid36 = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    /// <summary>The abbreviations a page may show beside their Arabic name (D-67-7, the interface guard's list).</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        "SKU", "COI", "FTE", "JSON", "PDF", "CSV", "XLSX", "QR", "OTP", "IBAN", "SMS", "KPI", "API", "URL", "IP", "SLA", "ISO", "HTTP", "UTC",
        "SHA", "ID", "HR", "IT", "TOTP", "MFA", "SSO", "OIDC", "CSP", "NFC", "RFID", "PIN", "VAT", "GPS", "ZIP", "SoD", "Keycloak", "Excel",
    };

    // ApprovalStatus is stored as its number.
    private const int ApprovalPending = 0;
    private const int ApprovalRejected = 2;

    private static async Task<string> Db(string database, string sql) =>
        (await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql)).Trim();

    private static EvidenceRecorder Start(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, $"ux67-{key}-{Guid.NewGuid():N}") { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        AcceptanceGate.RequireEnabled(recorder);
        return recorder;
    }

    /// <summary>One Chromium per scenario, with the personas' session states in temporary files deleted at the end.</summary>
    private sealed class Chromium : IAsyncDisposable
    {
        private readonly IPlaywright _playwright;
        private readonly List<string> _states = [];

        private Chromium(IPlaywright playwright, IBrowser browser) => (_playwright, Browser) = (playwright, browser);

        public IBrowser Browser { get; }

        public static async Task<Chromium> LaunchAsync()
        {
            var playwright = await Playwright.CreateAsync();
            return new Chromium(playwright, await playwright.Chromium.LaunchAsync(new() { Headless = true }));
        }

        public async Task<string> SignInAsync(string personaId)
        {
            var persona = AcceptanceHarness.RequirePersona(personaId);
            var state = Path.Combine(Path.GetTempPath(), $"ux67-{personaId}-{Guid.NewGuid():N}.json");
            _states.Add(state);
            return await Ux067PageProbe.SignInAndSaveStateAsync(Browser, BaseUrl, persona.Username, AcceptanceHarness.ResolvePassword(persona), state);
        }

        public async Task<(IBrowserContext Context, IPage Page)> PageAsync(string? state, int width = 1366, int height = 768)
        {
            var context = await Ux067PageProbe.NewContextAsync(Browser, BaseUrl, width, height, state);
            return (context, await context.NewPageAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            _playwright.Dispose();
            foreach (var state in _states.Where(File.Exists))
                File.Delete(state);
        }
    }

    /// <summary>Clicks what submits a form and waits for the POST's answer (the server has done its work), then the next document.</summary>
    private static async Task PostAsync(IPage page, string selector)
    {
        await page.RunAndWaitForResponseAsync(() => page.ClickAsync(selector), r => r.Request.Method == "POST", new() { Timeout = 30_000 });
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    /// <summary>Submits a form whose button asks first (<c>data-hr-confirm</c>), and confirms in the shared dialog.</summary>
    private static async Task SubmitConfirmedAsync(IPage page, string submitSelector)
    {
        await page.ClickAsync(submitSelector);
        await page.WaitForSelectorAsync("#hrConfirmModal.show #hrConfirmOk", new() { Timeout = 10_000 });
        await PostAsync(page, "#hrConfirmOk");
    }

    /// <summary>The visible text of the page's content, without internal codes, the technical view, code blocks and hidden text.</summary>
    private static Task<string> ContentTextAsync(IPage page) =>
        page.EvaluateAsync<string>(
            """
            () => {
                const root = (document.querySelector('.app-content') || document.body).cloneNode(true);
                root.querySelectorAll('.hr-internal-code, details.hr-technical-json, code, pre, script, style, noscript, [hidden], .visually-hidden, .d-none').forEach(e => e.remove());
                return root.innerText || '';
            }
            """);

    private static IReadOnlyList<string> EnglishWords(string text) =>
        Regex.Matches(text, @"\b[A-Za-z][A-Za-z]+\b").Select(m => m.Value).Where(w => !Abbreviations.Contains(w)).Distinct().ToList();

    private static Task<string> CoreEstablishmentTemplateAsync() =>
        Db(Core, "SELECT \"Id\" FROM \"PolicyTemplates\" WHERE \"Domain\" = 'Core.Establishment' AND \"Scope\" = 'National' ORDER BY \"CreatedAt\" LIMIT 1;");

    /// <summary>
    /// The key pages, with the hire page opened directly: the list links do not always reach the one application of agency A whose
    /// offer is accepted. Without one, an application with an active offer is shown as accepted while the pages are read (a stated
    /// test-only precondition, its two statuses put back after); with no offer at all, the hire page stays optional and is said so.
    /// </summary>
    private static async Task<(IReadOnlyList<Ux067KeyPage> Pages, string? Restore, string Note)> KeyPagesWithHireAsync()
    {
        var hireable = await Db(Core,
            "SELECT a.\"Id\" FROM \"RecruitmentApplications\" a JOIN \"OfferLetters\" o ON o.\"Id\" = a.\"ActiveOfferLetterId\" " +
            $"WHERE a.\"TenantId\" = '{AgencyA}' AND a.\"Status\" = {HireReadyApplication} AND o.\"Status\" = {AcceptedOffer} AND NOT o.\"IsConvertedToEmployee\" LIMIT 1;");
        string? restore = null;
        var note = "an application of agency A with its offer accepted";
        if (!Guid.TryParse(hireable, out _))
        {
            var row = (await Db(Core,
                "SELECT a.\"Id\" || '|' || a.\"Status\" || '|' || o.\"Id\" || '|' || o.\"Status\" FROM \"RecruitmentApplications\" a " +
                "JOIN \"OfferLetters\" o ON o.\"Id\" = a.\"ActiveOfferLetterId\" " +
                $"WHERE a.\"TenantId\" = '{AgencyA}' AND a.\"Status\" <> 4 AND NOT o.\"IsConvertedToEmployee\" LIMIT 1;")).Split('|');
            if (row.Length != 4)
                return (Ux067PageProbe.KeyPages, null, "no application of agency A has an offer: the hire page stays optional");
            hireable = row[0];
            await Db(Core, $"UPDATE \"RecruitmentApplications\" SET \"Status\" = {HireReadyApplication} WHERE \"Id\" = '{row[0]}'; UPDATE \"OfferLetters\" SET \"Status\" = {AcceptedOffer} WHERE \"Id\" = '{row[2]}';");
            restore = $"UPDATE \"RecruitmentApplications\" SET \"Status\" = {row[1]} WHERE \"Id\" = '{row[0]}'; UPDATE \"OfferLetters\" SET \"Status\" = {row[3]} WHERE \"Id\" = '{row[2]}';";
            note = "an application of agency A shown with its offer accepted while the pages are read (statuses put back after)";
        }

        var pages = Ux067PageProbe.KeyPages
            .Select(p => p.Name == "hire" ? new Ux067KeyPage("hire", $"/ui/hr-core/RecruitmentApplications/Hire/{hireable}") : p)
            .ToList();
        return (pages, restore, note);
    }

    /// <summary>
    /// PLAN-57's record pages, opened directly when agency A holds such a record (the lists' first page does not always reach it):
    /// a correction's form for a day of a closed period, a correction's page, an attendance record's page, and the edit form of a
    /// record that is not locked and not leave-made. Without a record (AC-LVA-57 has not run on this stack yet) a page stays optional.
    /// </summary>
    private static async Task<IReadOnlyList<Ux067KeyPage>> WithAttendanceRecordsAsync(IReadOnlyList<Ux067KeyPage> pages)
    {
        const string covered =
            "EXISTS (SELECT 1 FROM \"AttendanceEmployeeSummaries\" s JOIN \"AttendancePeriods\" p ON p.\"Id\" = s.\"AttendancePeriodId\" " +
            "WHERE s.\"EmployeeId\" = r.\"EmployeeId\" AND r.\"AttendanceDate\" BETWEEN p.\"PeriodStart\" AND p.\"PeriodEnd\")";
        var records = $"FROM \"DailyAttendanceRecords\" r WHERE r.\"TenantId\" = '{AgencyA}'";
        var locked = (await Db(Core, $"SELECT COALESCE((SELECT r.\"EmployeeId\"::text || '|' || to_char(r.\"AttendanceDate\", 'YYYY-MM-DD') {records} AND r.\"Source\" <> 2 AND {covered} LIMIT 1), '');")).Split('|');
        var correction = await Db(Core, $"SELECT COALESCE((SELECT \"Id\"::text FROM \"AttendanceCorrections\" WHERE \"TenantId\" = '{AgencyA}' ORDER BY \"Status\", \"CreatedAt\" DESC LIMIT 1), '');");
        var record = await Db(Core, $"SELECT COALESCE((SELECT r.\"Id\"::text {records} LIMIT 1), '');");
        var editable = await Db(Core, $"SELECT COALESCE((SELECT r.\"Id\"::text {records} AND r.\"Source\" <> 2 AND NOT {covered} LIMIT 1), '');");
        return pages.Select(p => p.Name switch
        {
            "attendance-correction-form" when locked.Length == 2 =>
                new Ux067KeyPage(p.Name, $"/ui/hr-core/AttendanceCorrections/Create?employeeId={locked[0]}&date={locked[1]}"),
            "attendance-correction-details" when Guid.TryParse(correction, out _) => new Ux067KeyPage(p.Name, $"/ui/hr-core/AttendanceCorrections/Details/{correction}"),
            "attendance-record-details" when Guid.TryParse(record, out _) => new Ux067KeyPage(p.Name, $"/ui/hr-core/DailyAttendanceRecords/Details/{record}"),
            "attendance-record-edit" when Guid.TryParse(editable, out _) => new Ux067KeyPage(p.Name, $"/ui/hr-core/DailyAttendanceRecords/Edit/{editable}"),
            _ => p
        }).ToList();
    }

    /// <summary>
    /// The rules editor opens a draft only, and an earlier class may have published or discarded the seed's. The page is opened
    /// directly on a draft of agency A; without one, the administrator makes one through the template's own page (a copy of the version
    /// in force), and <see cref="DiscardPolicyDraftAsync"/> discards it through the editor's own form after the pages are read.
    /// </summary>
    private static async Task<(IReadOnlyList<Ux067KeyPage> Pages, string? Created, string Note)> WithPolicyDraftAsync(
        Chromium chromium, string admin, IReadOnlyList<Ux067KeyPage> pages)
    {
        var draft = await Db(Core,
            "SELECT v.\"Id\" FROM \"PolicyTemplateVersions\" v JOIN \"PolicyTemplates\" t ON t.\"Id\" = v.\"PolicyTemplateId\" " +
            $"WHERE t.\"TenantId\" = '{AgencyA}' AND v.\"Status\" = 'Draft' LIMIT 1;");
        string? created = null;
        var note = "a draft of agency A";
        if (!Guid.TryParse(draft, out _))
        {
            var template = await Db(Core,
                $"SELECT t.\"Id\" FROM \"PolicyTemplates\" t WHERE t.\"TenantId\" = '{AgencyA}' AND t.\"Scope\" = 'TenantOverride' AND t.\"RetiredAt\" IS NULL " +
                "AND NOT EXISTS (SELECT 1 FROM \"PolicyTemplateVersions\" v WHERE v.\"PolicyTemplateId\" = t.\"Id\" AND v.\"Status\" = 'Draft') ORDER BY t.\"Domain\" LIMIT 1;");
            if (!Guid.TryParse(template, out _))
                return (pages, null, "no policy template of agency A: the rules editor is found through its links");
            var (context, page) = await chromium.PageAsync(admin);
            await using (context)
            {
                await page.GotoAsync($"/ui/hr-core/PolicyTemplates/Details/{template}");
                await PostAsync(page, "form[action*='CreateTemplateDraft'] button[type='submit']");
            }

            draft = await Db(Core, $"SELECT \"Id\" FROM \"PolicyTemplateVersions\" WHERE \"PolicyTemplateId\" = '{template}' AND \"Status\" = 'Draft' LIMIT 1;");
            Assert.True(Guid.TryParse(draft, out _), "The policy draft was not created through the template's page.");
            created = draft;
            note = "a draft of agency A made through the template's page for the check, discarded after";
        }

        var withDraft = pages
            .Select(p => p.Name == "policy-rules" ? new Ux067KeyPage("policy-rules", $"/ui/hr-core/PolicyTemplates/EditRules?versionId={draft}") : p)
            .ToList();
        return (withDraft, created, note);
    }

    private static async Task DiscardPolicyDraftAsync(Chromium chromium, string admin, string? created)
    {
        if (created is null)
            return;
        var (context, page) = await chromium.PageAsync(admin);
        await using (context)
        {
            await page.GotoAsync($"/ui/hr-core/PolicyTemplates/EditRules?versionId={created}");
            await SubmitConfirmedAsync(page, "form[action*='DiscardDraft'] button[type='submit']");
        }

        Assert.Equal("0", await Db(Core, $"SELECT count(*) FROM \"PolicyTemplateVersions\" WHERE \"Id\" = '{created}' AND \"Status\" = 'Draft';"));
    }

    // RecruitmentApplicationStatus.OfferAccepted and OfferStatus.Accepted, stored as numbers.
    private const int HireReadyApplication = 9;
    private const int AcceptedOffer = 3;

    // ------------------------------------------------------------------ AC-UX-67a

    [SkippableFact]
    public async Task Ac67a_every_key_page_carries_one_policy_and_no_violation_and_a_planted_inline_handler_is_caught()
    {
        var r = Start("67a", "AC-UX-67a: the key pages carry one CSP policy each and raise no securitypolicyviolation; a planted inline handler is caught", "a-directory-checker, central-a, auditor");
        await using var chromium = await Chromium.LaunchAsync();
        var admin = await chromium.SignInAsync("a-directory-checker");
        var central = await chromium.SignInAsync("central-a");

        // The establishment change page needs a change: a freeze request through the position's own form (it asks first).
        var change = await CreateFreezeRequestAsync(chromium, central, r);
        var (hirePages, restoreHire, hireNote) = await KeyPagesWithHireAsync();
        var (draftPages, createdDraft, draftNote) = await WithPolicyDraftAsync(chromium, admin, hirePages);
        var pages = await WithAttendanceRecordsAsync(draftPages);
        try
        {
            var adminResults = await Ux067PageProbe.ProbeAsync(chromium.Browser, BaseUrl, admin, pages, [(1366, 768)],
                Path.Combine(r.Folder, "csp-pages"));
            // PLAN-58: the review records' pages, as the auditor (the only role the gateway lets reach them).
            var auditor = await chromium.SignInAsync("auditor");
            var auditorResults = await Ux067PageProbe.ProbeAsync(chromium.Browser, BaseUrl, auditor, Ux067PageProbe.AuditorPages, [(1366, 768)],
                Path.Combine(r.Folder, "csp-pages-auditor"));
            var results = adminResults.Concat(auditorResults).ToList();
            await File.WriteAllTextAsync(Path.Combine(r.Folder, "csp-pages.json"), Ux067PageProbe.ToJson(results));
            r.WriteScenarioSteps(string.Join("\n", results.Select(x => x.Resolved
                ? $"- {x.Name}: HTTP {x.Status}, {x.CspHeaders} header(s), {x.CspPolicies} policy, {x.ReportOnlyPolicies} report-only, {x.Violations.Count} violation(s){(x.Note is null ? "" : $" ({x.Note})")}"
                : $"- {x.Name}: no record of this kind on the stack{(x.Optional ? " (optional)" : "")} — {x.Note}")));

            var failures = results.Where(x => !x.Passes).Select(x =>
                $"{x.Name}: {x.CspPolicies} policies, {x.Violations.Count} violations ({string.Join("; ", x.Violations.Select(v => $"{v.Directive} {v.BlockedUri} {v.Source}"))}) {x.Note}").ToList();
            Assert.True(failures.Count == 0, "Key pages failing the CSP check:\n" + string.Join("\n", failures));
            Assert.All(results.Where(x => x.Resolved), x => Assert.Equal(0, x.ReportOnlyPolicies));
            Assert.Contains(results, x => x.Name == "establishment-change" && x.Resolved);
            Assert.Contains(results, x => x.Name == "payroll-dual-control-inbox" && x.Resolved);
            Assert.Contains(results, x => x.Name == "payroll-audit-records" && x.Resolved);
            r.WriteScenarioDbChecks(new { hire = hireNote, policyRules = draftNote });
        }
        finally
        {
            if (restoreHire is not null)
                await Db(Core, restoreHire);
            await DiscardPolicyDraftAsync(chromium, admin, createdDraft);
            await CancelChangeAsync(chromium, central, change);
        }

        // Negative: the same listener reports an inline handler under a policy that forbids it, so the check above can fail.
        var (context, page) = await chromium.PageAsync(null);
        await using (context)
        {
            await page.SetContentAsync("""
                <html><head><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'self'"></head>
                <body><button id="planted" onclick="document.title='ran'">زر مزروع</button></body></html>
                """);
            await page.EvaluateAsync(Ux067PageProbe.ViolationInitScript);
            await page.ClickAsync("#planted");
            await page.WaitForTimeoutAsync(300);
            var planted = await Ux067PageProbe.ReadViolationsAsync(page);
            Assert.Contains(planted, v => v.Directive.StartsWith("script-src", StringComparison.Ordinal));
            Assert.NotEqual("ran", await page.TitleAsync());
        }

        r.WriteResult("Pass");
    }

    private static async Task<string?> CreateFreezeRequestAsync(Chromium chromium, string state, EvidenceRecorder r)
    {
        var position = await Db(Core,
            $"SELECT p.\"Id\" FROM \"FundedPositions\" p WHERE p.\"Status\" = 'Vacant' AND p.\"TenantId\" = '{AgencyA}' " +
            "AND NOT EXISTS (SELECT 1 FROM \"EstablishmentChangeRequests\" c WHERE c.\"TargetEntityId\" = p.\"Id\" AND c.\"Status\" IN ('Draft','PendingApproval','Approved')) " +
            "AND NOT EXISTS (SELECT 1 FROM \"RecruitmentRequisitions\" q WHERE q.\"FundedPositionId\" = p.\"Id\" AND q.\"Status\" IN ('Draft','PendingApproval','Approved','Published')) " +
            "ORDER BY p.\"PositionCode\" LIMIT 1;");
        AcceptanceGate.RequireData(r, Guid.TryParse(position, out _), "لا منصب شاغر لطلب تجميد", "No vacant funded position of agency A without an open change or requisition.");

        var (context, page) = await chromium.PageAsync(state);
        await using (context)
        {
            await page.GotoAsync($"/ui/hr-core/FundedPositions/Details/{position}");
            await page.FillAsync("form[action*='/Freeze/'] input[name='FreezeReason']", "تجميد مؤقت لمراجعة الموازنة");
            await SubmitConfirmedAsync(page, "form[action*='/Freeze/'] button[type='submit']");
        }

        var id = await Db(Core, $"SELECT \"Id\" FROM \"EstablishmentChangeRequests\" WHERE \"TargetEntityId\" = '{position}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(id, out _), $"The freeze form made no change request for position {position}.");
        return id;
    }

    private static async Task CancelChangeAsync(Chromium chromium, string state, string? change)
    {
        if (change is null)
            return;
        var (context, page) = await chromium.PageAsync(state);
        await using (context)
        {
            await page.GotoAsync($"/ui/hr-core/EstablishmentChanges/Details/{change}");
            if (await page.Locator("form[action*='/Cancel/'] button[type='submit']").CountAsync() > 0)
                await SubmitConfirmedAsync(page, "form[action*='/Cancel/'] button[type='submit']");
        }

        Assert.Equal("Cancelled", await Db(Core, $"SELECT \"Status\" FROM \"EstablishmentChangeRequests\" WHERE \"Id\" = '{change}';"));
    }

    // ------------------------------------------------------------------ AC-UX-67a (the shared confirmation, the reviewer's comments)

    [SkippableFact]
    public async Task Ac67a_a_destructive_form_asks_first_and_the_reviewers_comments_reach_the_record()
    {
        var r = Start("67a-confirm", "AC-UX-67a: deleting a requisition draft asks first and cancelling changes nothing; the reviewer's comments reach the approval record", "central-a, a-directory-checker");
        await using var chromium = await Chromium.LaunchAsync();
        var central = await chromium.SignInAsync("central-a");

        // A requisition draft, through its own form, on a free position of the first branch (as AC-HCM-26 makes one).
        string draft;
        var (context, page) = await chromium.PageAsync(central);
        await using (context)
        {
            await page.GotoAsync("/ui/hr-core/RecruitmentRequisitions/Create");
            var vacant = await context.APIRequest.GetAsync($"/ui/hr-core/api/recruitment/vacant-positions?orgUnitId={Br1Scope}");
            Assert.Equal(200, vacant.Status);
            string? position = null;
            foreach (var item in (await vacant.JsonAsync())!.Value.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                if (await Db(Core, $"SELECT count(*) FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\" = '{id}' AND \"Status\" IN ('Draft','PendingApproval','Approved','Published');") == "0")
                {
                    position = id;
                    break;
                }
            }

            AcceptanceGate.RequireData(r, position is not null, "لا منصب شاغر في الفرع الأول دون طلب شاغر", "No vacant BR1 funded position of agency A without a blocking requisition.");
            var form = context.APIRequest.CreateFormData();
            form.Set("__RequestVerificationToken", (await page.GetAttributeAsync("form input[name='__RequestVerificationToken']", "value"))!);
            form.Set("ScopeOrgUnitId", Br1Scope);
            form.Set("FundedPositionId", position!);
            form.Set("JobTitle", "محلل بيانات");
            form.Set("JustificationNotes", "تحقق القبول AC-UX-67 من نافذة التأكيد");
            form.Set("LegalSourceId", Syr001LegislationIds.Law50_2004);
            form.Set("BudgetYear", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture));
            form.Set("PublishingChannel", "PublicOnly");
            var created = await context.APIRequest.PostAsync("/ui/hr-core/RecruitmentRequisitions/Create", new() { Form = form, MaxRedirects = 0 });
            Assert.True(created.Status is 200 or 302, $"The requisition form answered {created.Status}.");
            draft = await Db(Core, $"SELECT \"Id\" FROM \"RecruitmentRequisitions\" WHERE \"FundedPositionId\" = '{position}' AND \"Status\" = 'Draft' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
            Assert.True(Guid.TryParse(draft, out _), $"The requisition draft was not created on position {position}.");
        }

        (context, page) = await chromium.PageAsync(central);
        await using (context)
        {
            const string delete = "form[data-testid='requisition-delete-form'] button[type='submit']";
            try
            {
                await page.GotoAsync($"/ui/hr-core/RecruitmentRequisitions/Details/{draft}");
                await page.ClickAsync(delete);
                await page.WaitForSelectorAsync("#hrConfirmModal.show #hrConfirmCancel", new() { Timeout = 10_000 });
                Assert.Contains("حذف مسودة طلب الشاغر؟", await page.InnerTextAsync("#hrConfirmMessage"), StringComparison.Ordinal);

                // Cancel: the dialog closes and nothing changes.
                await page.ClickAsync("#hrConfirmCancel");
                await page.WaitForSelectorAsync("#hrConfirmModal", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
                Assert.EndsWith($"/RecruitmentRequisitions/Details/{draft}", new Uri(page.Url).AbsolutePath, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("1", await Db(Core, $"SELECT count(*) FROM \"RecruitmentRequisitions\" WHERE \"Id\" = '{draft}' AND \"Status\" = 'Draft';"));

                // Confirm: the draft goes, and the scenario leaves nothing behind.
                await SubmitConfirmedAsync(page, delete);
                Assert.Equal("0", await Db(Core, $"SELECT count(*) FROM \"RecruitmentRequisitions\" WHERE \"Id\" = '{draft}';"));
            }
            finally
            {
                // A step that failed leaves the draft: it is deleted through its own form, as the scenario would have.
                if (await Db(Core, $"SELECT count(*) FROM \"RecruitmentRequisitions\" WHERE \"Id\" = '{draft}' AND \"Status\" = 'Draft';") == "1")
                {
                    await page.GotoAsync($"/ui/hr-core/RecruitmentRequisitions/Details/{draft}");
                    var form = context.APIRequest.CreateFormData();
                    form.Set("__RequestVerificationToken", (await page.GetAttributeAsync("form[data-testid='requisition-delete-form'] input[name='__RequestVerificationToken']", "value"))!);
                    await context.APIRequest.PostAsync($"/ui/hr-core/RecruitmentRequisitions/Delete/{draft}", new() { Form = form, MaxRedirects = 0 });
                }
            }
        }

        // The reviewer's comments: a pending approval request made for this check (a stated test-only precondition, as AC-WF-44's),
        // rejected in the browser with comments, then removed.
        var admin = await chromium.SignInAsync("a-directory-checker");
        var request = Guid.NewGuid().ToString("D");
        await Db(Core,
            "INSERT INTO \"ApprovalRequests\" (\"Id\",\"TenantId\",\"EntityType\",\"EntityId\",\"Description\",\"RequestedByName\",\"RequestedAt\",\"Status\",\"CurrentLevel\",\"CreatedAt\",\"UpdatedAt\") " +
            $"VALUES ('{request}','{AgencyA}','Employee',gen_random_uuid(),'تحقق القبول AC-UX-67 من ملاحظات المراجعة','النظام',now(),{ApprovalPending},1,now(),now()); " +
            "INSERT INTO \"ApprovalSteps\" (\"Id\",\"TenantId\",\"ApprovalRequestId\",\"Level\",\"RoleRequired\",\"Status\",\"CreatedAt\",\"UpdatedAt\") " +
            $"VALUES (gen_random_uuid(),'{AgencyA}','{request}',1,'',{ApprovalPending},now(),now());");
        try
        {
            (context, page) = await chromium.PageAsync(admin);
            await using (context)
            {
                await page.GotoAsync($"/ui/hr-core/Approvals/Review/{request}");
                const string comments = "المستندات ناقصة، يُعاد بعد استكمالها";
                await page.FillAsync("#reviewComments", comments);
                await PostAsync(page, "[data-testid='approval-reject']");
                Assert.Equal($"{ApprovalRejected}|{comments}",
                    await Db(Core, $"SELECT \"Status\" || '|' || coalesce(\"ReviewComments\", '') FROM \"ApprovalRequests\" WHERE \"Id\" = '{request}';"));
            }
        }
        finally
        {
            await Db(Core, $"DELETE FROM \"ApprovalSteps\" WHERE \"ApprovalRequestId\" = '{request}'; DELETE FROM \"ApprovalRequests\" WHERE \"Id\" = '{request}';");
        }

        r.WriteScenarioSteps(
            "1. A requisition draft through its own form; its delete button opens the shared confirmation with its question.\n" +
            "2. Cancel: the dialog closes, the page stays, the draft stays (psql). Confirm: the draft is deleted (psql).\n" +
            "3. A pending approval made for the check, rejected in the browser with comments: the record holds the comments (psql); it is removed.");
        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------ AC-UX-67b

    [SkippableFact]
    public async Task Ac67b_the_template_page_reads_in_arabic_with_names_and_damascus_times()
    {
        var r = Start("67b", "AC-UX-67b: the policy template page — Arabic labels, people by name, times in Damascus = stored + 3 h in one format; no id in a person's place and no English word outside the abbreviations; the registry names a user to staff only", "a-directory-checker, employee-a");
        await using var chromium = await Chromium.LaunchAsync();
        var admin = await chromium.SignInAsync("a-directory-checker");
        var template = await CoreEstablishmentTemplateAsync();
        AcceptanceGate.RequireData(r, Guid.TryParse(template, out _), "لا قالب سياسة للملاك", "No national Core.Establishment template.");

        var (context, page) = await chromium.PageAsync(admin);
        await using (context)
        {
            await page.GotoAsync($"/ui/hr-core/PolicyTemplates/Details/{template}");
            var text = await ContentTextAsync(page);
            foreach (var label in new[] { "المجال", "نوع السجل", "النطاق", "القاعدة", "الأولوية", "نص القاعدة", "المحرّر", "الناشر", "النشر" })
                Assert.Contains(label, text, StringComparison.Ordinal);

            // People: the maker and the publisher are named (a seed's are «النظام»), never an id.
            var publications = await page.Locator(".card-body.border-bottom.small").AllInnerTextsAsync();
            var people = publications.Where(p => p.Contains("المحرّر", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(people);
            Assert.All(people, p => Assert.DoesNotMatch(Guid36, p));

            // Times: each <time> shows its UTC value on the Damascus clock, in one format, with the hint.
            var times = await page.EvaluateAsync<string[][]>("() => [...document.querySelectorAll('.app-content time[datetime]')].map(t => [t.getAttribute('datetime'), t.innerText.trim(), t.getAttribute('title') || ''])");
            Assert.NotEmpty(times);
            var damascus = TimeZoneInfo.FindSystemTimeZoneById("Asia/Damascus");
            foreach (var t in times)
            {
                var utc = DateTime.ParseExact(t[0], "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                Assert.Equal(utc.AddHours(3), TimeZoneInfo.ConvertTimeFromUtc(utc, damascus));
                Assert.Equal(utc.AddHours(3).ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture), t[1]);
                Assert.Equal("بتوقيت دمشق", t[2]);
            }

            var stored = await Db(Core,
                "SELECT to_char(p.\"PublishedAt\" AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"') FROM \"PolicyPublications\" p " +
                $"JOIN \"PolicyTemplateVersions\" v ON v.\"Id\" = p.\"PolicyTemplateVersionId\" WHERE v.\"PolicyTemplateId\" = '{template}' ORDER BY p.\"PublishedAt\" DESC LIMIT 1;");
            Assert.Contains(times, t => t[0] == stored);

            // Negative: no English word outside the abbreviations.
            var english = EnglishWords(text);
            Assert.True(english.Count == 0, "English words on the template page: " + string.Join(", ", english));
            r.WriteScenarioDbChecks(new { template, stored, times = times.Select(t => new { iso = t[0], shown = t[1] }), people });
        }

        // The registry recorded the signed-in user; its lookup names a user of the agency to staff and refuses an employee.
        var known = (await Db(Gateway, $"SELECT \"Sub\" || '|' || \"DisplayName\" FROM gateway_user_names WHERE \"AgencyId\" = '{AgencyA}' ORDER BY \"LastSeenAtUtc\" DESC LIMIT 1;")).Split('|');
        Assert.True(known.Length == 2, "The registry recorded no signed-in user of agency A.");
        const string lookup =
            """
            async (id) => {
                const t = await fetch('/api/user-names/token', { credentials: 'same-origin', headers: { Accept: 'application/json' } });
                const token = t.ok ? (await t.json()).token : '';
                const post = (headers) => fetch('/api/user-names/by-ids', { method: 'POST', credentials: 'same-origin', headers: Object.assign({ 'Content-Type': 'application/json' }, headers), body: JSON.stringify({ ids: [id] }) });
                const withToken = await post({ RequestVerificationToken: token });
                const withoutToken = await post({});
                return [String(t.status), String(withToken.status), await withToken.text(), String(withoutToken.status)];
            }
            """;
        (context, page) = await chromium.PageAsync(admin);
        await using (context)
        {
            await page.GotoAsync("/ui/hr-core/Dashboard");
            var answer = await page.EvaluateAsync<string[]>(lookup, known[0]);
            Assert.Equal("200", answer[0]);
            Assert.Equal("200", answer[1]);
            using var body = JsonDocument.Parse(answer[2]);
            Assert.Equal(known[1], body.RootElement.GetProperty("items")[0].GetProperty("label").GetString());
            Assert.Equal("400", answer[3]);
        }

        // The page's own script names a person: the dashboard's HTML gets, in the browser only, the span _UserDisplayName renders for
        // this user and one for an id nobody knows; display-names.js fills the first from the registry and leaves the second unknown.
        (context, page) = await chromium.PageAsync(admin);
        await using (context)
        {
            var stranger = Guid.NewGuid().ToString("D");
            await page.RouteAsync("**/ui/hr-core/Dashboard", async route =>
            {
                var response = await route.FetchAsync();
                var html = await response.TextAsync();
                var spans = $"<span id=\"ux67-known\" data-hr-user-id=\"{known[0]}\" title=\"{known[0]}\">مستخدم غير معروف</span>" +
                            $"<span id=\"ux67-stranger\" data-hr-user-id=\"{stranger}\" title=\"{stranger}\">مستخدم غير معروف</span>";
                await route.FulfillAsync(new() { Response = response, Body = html.Replace("</main>", spans + "</main>", StringComparison.Ordinal) });
            });
            await page.GotoAsync("/ui/hr-core/Dashboard");
            // Polled from here: WaitForFunction evaluates a string in the page, which the enforced policy refuses (no 'unsafe-eval').
            var shown = "";
            for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline; await Task.Delay(250))
            {
                shown = (await page.TextContentAsync("#ux67-known"))?.Trim() ?? "";
                if (shown != "مستخدم غير معروف")
                    break;
            }

            Assert.Equal(known[1], shown);
            Assert.Equal("مستخدم غير معروف", (await page.TextContentAsync("#ux67-stranger"))!.Trim());
            Assert.Empty(await Ux067PageProbe.ReadViolationsAsync(page));
        }

        var employee = await chromium.SignInAsync("employee-a");
        (context, page) = await chromium.PageAsync(employee);
        await using (context)
        {
            await page.GotoAsync("/");
            var answer = await page.EvaluateAsync<string[]>(lookup, known[0]);
            Assert.Equal("403", answer[0]);
            Assert.Equal("403", answer[1]);
        }

        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------ AC-UX-67c

    [SkippableFact]
    public async Task Ac67c_every_rule_reads_as_a_sentence_and_its_json_shows_to_an_administrator_not_to_a_reader()
    {
        var r = Start("67c", "AC-UX-67c: each rule of the template is an Arabic sentence; the JSON shows under «عرض تقني» to an administrator, not to a reader without a policy role", "a-directory-checker, a-supervisory-reader");
        await using var chromium = await Chromium.LaunchAsync();
        var template = await CoreEstablishmentTemplateAsync();
        AcceptanceGate.RequireData(r, Guid.TryParse(template, out _), "لا قالب سياسة للملاك", "No national Core.Establishment template.");
        var rules = int.Parse(await Db(Core,
            $"SELECT count(*) FROM \"PolicyRules\" r JOIN \"PolicyTemplateVersions\" v ON v.\"Id\" = r.\"PolicyTemplateVersionId\" WHERE v.\"PolicyTemplateId\" = '{template}';"), CultureInfo.InvariantCulture);
        AcceptanceGate.RequireData(r, rules > 0, "لا قواعد في قالب الملاك", "The Core.Establishment template holds no rule.");

        var admin = await chromium.SignInAsync("a-directory-checker");
        var (context, page) = await chromium.PageAsync(admin);
        await using (context)
        {
            await page.GotoAsync($"/ui/hr-core/PolicyTemplates/Details/{template}");
            var sentences = await page.Locator("[data-testid='rule-sentence']").AllInnerTextsAsync();
            Assert.Equal(rules, sentences.Count);
            Assert.All(sentences, s =>
            {
                Assert.StartsWith("إذا ", s, StringComparison.Ordinal);
                Assert.DoesNotContain("انظر العرض التقني", s, StringComparison.Ordinal);
                Assert.Empty(EnglishWords(s));
            });
            Assert.Contains(sentences, s => s.Contains("يُرفض الإجراء", StringComparison.Ordinal));
            Assert.Contains(sentences, s => s.Contains("يُطلق المسار", StringComparison.Ordinal));
            var technical = await page.Locator("details[data-testid='technical-json'] pre").AllTextContentsAsync();
            Assert.Equal(2 * rules, technical.Count);
            Assert.Contains(technical, t => t.Contains("\"field\"", StringComparison.Ordinal));
            r.WriteScenarioDbChecks(new { template, rules, sentences });
        }

        var reader = await chromium.SignInAsync("a-supervisory-reader");
        (context, page) = await chromium.PageAsync(reader);
        await using (context)
        {
            var response = await page.GotoAsync($"/ui/hr-core/PolicyTemplates/Details/{template}");
            Assert.Equal(200, response!.Status);
            Assert.Equal(rules, await page.Locator("[data-testid='rule-sentence']").CountAsync());
            Assert.Equal(0, await page.Locator("details[data-testid='technical-json']").CountAsync());
            Assert.DoesNotContain("\"field\"", await page.ContentAsync(), StringComparison.Ordinal);
        }

        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------ AC-UX-67d

    [SkippableFact]
    public async Task Ac67d_the_portal_is_styled_at_careers_and_the_module_root_opens_the_dashboard()
    {
        var r = Start("67d", "AC-UX-67d: /careers styled (computed styles), its dates yyyy/MM/dd, no «server check» text; /ui/hr-core/ opens the dashboard for a signed-in employee", "anonymous, central-a");
        await using var chromium = await Chromium.LaunchAsync();
        var (context, page) = await chromium.PageAsync(null);
        await using (context)
        {
            var response = await page.GotoAsync("/careers");
            Assert.Equal(200, response!.Status);
            var styles = await page.EvaluateAsync<string[]>(
                """
                () => {
                    // A gradient is a background image; a plain colour is the background colour.
                    const bg = s => { const e = document.querySelector(s); if (!e) return 'missing'; const c = getComputedStyle(e); return c.backgroundImage !== 'none' ? c.backgroundImage : c.backgroundColor; };
                    return [bg('.careers-navbar'), bg('.careers-card'), bg('.careers-btn-primary')];
                }
                """);
            r.WriteScenarioDbChecks(new { navbar = styles[0], card = styles[1], button = styles[2] });
            Assert.All(styles.Take(2), s => Assert.True(s is not "missing" and not "rgba(0, 0, 0, 0)" and not "transparent", $"computed background: {s}"));
            if (styles[2] != "missing")
                Assert.NotEqual("rgba(0, 0, 0, 0)", styles[2]);

            var text = await page.InnerTextAsync("body");
            Assert.DoesNotContain("server check", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("فحص الخادم", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\b\d{2}-\d{2}-\d{4}\b", text);
            foreach (var closing in await page.Locator(".careers-card .careers-accent bdi").AllInnerTextsAsync())
                Assert.Matches(@"^\d{4}/\d{2}/\d{2}$", closing.Trim());
            Assert.Empty(await Ux067PageProbe.ReadViolationsAsync(page));
        }

        var central = await chromium.SignInAsync("central-a");
        (context, page) = await chromium.PageAsync(central);
        await using (context)
        {
            var response = await page.GotoAsync("/ui/hr-core/");
            Assert.Equal(200, response!.Status);
            Assert.Equal(0, await page.Locator(".careers-navbar").CountAsync());
            Assert.True(await page.Locator(".app-sidebar").CountAsync() > 0, "/ui/hr-core/ did not open the dashboard.");
        }

        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------ AC-UX-67e

    [SkippableFact]
    public async Task Ac67e_the_simulation_form_gives_the_decision_of_the_json_and_an_unknown_fact_is_refused()
    {
        var r = Start("67e", "AC-UX-67e: the simulation by form gives the JSON's decision; an unknown fact is refused as before", "a-directory-checker");
        await using var chromium = await Chromium.LaunchAsync();
        var admin = await chromium.SignInAsync("a-directory-checker");
        var version = await Db(Core,
            "SELECT v.\"Id\" FROM \"PolicyTemplateVersions\" v JOIN \"PolicyTemplates\" t ON t.\"Id\" = v.\"PolicyTemplateId\" " +
            "WHERE t.\"Domain\" = 'Core.Establishment' AND t.\"Scope\" = 'National' AND v.\"Status\" = 'Published' ORDER BY v.\"VersionNumber\" DESC LIMIT 1;");
        AcceptanceGate.RequireData(r, Guid.TryParse(version, out _), "لا إصدار منشور للملاك", "No published Core.Establishment version.");

        static async Task<(string Allowed, string Effects, string Text)> DecisionAsync(IPage page) =>
            (await page.GetAttributeAsync("[data-testid='decision-allowed']", "data-allowed") ?? "",
             await page.GetAttributeAsync("[data-testid='decision-effects']", "data-effects") ?? "",
             await page.InnerTextAsync("[data-testid='simulate-decision']"));

        async Task<(string Allowed, string Effects, string Text)> ByJsonAsync(IPage page, string json)
        {
            await page.GotoAsync($"/ui/hr-core/PolicyTemplates/Simulate?versionId={version}");
            await page.ClickAsync("details[data-testid='simulate-json'] summary");
            await page.FillAsync("#factsJsonInput", json);
            await PostAsync(page, "details[data-testid='simulate-json'] button[type='submit']");
            await page.WaitForSelectorAsync("[data-testid='simulate-decision']");
            return await DecisionAsync(page);
        }

        var (context, page) = await chromium.PageAsync(admin);
        await using (context)
        {
            var steps = new List<string>();
            var decisions = new HashSet<string>();
            foreach (var scope in new[] { "true", "false" })
            {
                await page.GotoAsync($"/ui/hr-core/PolicyTemplates/Simulate?versionId={version}");
                await page.SelectOptionAsync("select[name='fact[action]']", "SubmitForApproval");
                await page.SelectOptionAsync("select[name='fact[hasOrgScope]']", scope);
                await PostAsync(page, "form[data-testid='simulate-form'] button[type='submit']");
                await page.WaitForSelectorAsync("[data-testid='simulate-decision']");
                var byForm = await DecisionAsync(page);
                var byJson = await ByJsonAsync(page, $$"""{"action":"SubmitForApproval","hasOrgScope":"{{scope}}"}""");

                Assert.Equal((byJson.Allowed, byJson.Effects), (byForm.Allowed, byForm.Effects));
                decisions.Add(byForm.Allowed);
                steps.Add($"hasOrgScope={scope}: form {byForm.Allowed} [{byForm.Effects}] = JSON {byJson.Allowed} [{byJson.Effects}]");
            }

            // The branch-scope rule denies and the central one allows: the two cases really differ.
            Assert.Equal(2, decisions.Count);

            var unknown = await ByJsonAsync(page, """{"action":"SubmitForApproval","environment":"production"}""");
            Assert.Equal("false", unknown.Allowed);
            Assert.Contains("حقل غير معروف في مخطط الحقائق", unknown.Text, StringComparison.Ordinal);
            r.WriteScenarioSteps(string.Join("\n", steps) + "\nunknown fact «environment»: refused");
        }

        r.WriteResult("Pass");
    }

    // ------------------------------------------------------------------ AC-UX-67f

    [SkippableFact]
    public async Task Ac67f_the_key_pages_screenshotted_at_both_sizes_raise_no_violation()
    {
        var r = Start("67f", "AC-UX-67f: the key pages at 1366×768 and 390×844 (deviceScaleFactor 1), with no securitypolicyviolation", "a-directory-checker, central-a");
        await using var chromium = await Chromium.LaunchAsync();
        var admin = await chromium.SignInAsync("a-directory-checker");
        var central = await chromium.SignInAsync("central-a");
        var change = await CreateFreezeRequestAsync(chromium, central, r);
        var (hirePages, restoreHire, hireNote) = await KeyPagesWithHireAsync();
        var (draftPages, createdDraft, draftNote) = await WithPolicyDraftAsync(chromium, admin, hirePages);
        var pages = await WithAttendanceRecordsAsync(draftPages);
        try
        {
            var folder = Path.Combine(r.Folder, "screenshots");
            var results = await Ux067PageProbe.ProbeAsync(chromium.Browser, BaseUrl, admin, pages, [(1366, 768), (390, 844)], folder);
            await File.WriteAllTextAsync(Path.Combine(r.Folder, "screenshots.json"), Ux067PageProbe.ToJson(results));
            var failures = results.Where(x => !x.Passes).Select(x => $"{x.Name}: {x.CspPolicies} policies, {x.Violations.Count} violations {x.Note}").ToList();
            Assert.True(failures.Count == 0, string.Join("\n", failures));
            Assert.All(results.Where(x => x.Resolved), x => Assert.Equal(2, x.Screenshots.Count));
            r.WriteScenarioSteps($"{results.Sum(x => x.Screenshots.Count)} screenshots of {results.Count(x => x.Resolved)} pages in screenshots/ (not committed); the hire page: {hireNote}; the rules editor: {draftNote}");
        }
        finally
        {
            if (restoreHire is not null)
                await Db(Core, restoreHire);
            await DiscardPolicyDraftAsync(chromium, admin, createdDraft);
            await CancelChangeAsync(chromium, central, change);
        }

        r.WriteResult("Pass");
    }
}
