using System.Globalization;
using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-67 on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>), with the AC-OPS-36 helpers. Opens the key pages (P-12) in Chromium,
/// signed in as <c>hradmin</c> (the portal anonymous), counts the Content-Security-Policy policies of each response and records
/// every <c>securitypolicyviolation</c> event, and takes screenshots at 1366×768 and 390×844. With
/// <c>UX067_VPS_LABEL=before</c> it only records (P0, the baseline); otherwise every page must carry one policy and raise no
/// violation. The other checks read the portal, the policy page, a destructive form and a reviewer's comments, and, after the
/// suites, the names the registry knows. What a check creates it removes. The demo users' password is read into memory only; the
/// session state lives in a temporary file deleted at the end; evidence goes under <c>artifacts/plan67/vps/&lt;label&gt;</c> and is
/// never committed.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Ux67Marker = "PLAN-67 server check";

    private static string Ux67Label => Environment.GetEnvironmentVariable("UX067_VPS_LABEL") is { Length: > 0 } label ? label : "after";

    private static string Ux67Out => Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan67", "vps", Ux67Label);

    private static EvidenceRecorder StartUx67(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"ux67-{key}-{Guid.NewGuid():N}", Ux67Out)
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "hradmin on the demo server (password read into memory only); the portal anonymous");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    /// <summary>One psql script in a transaction, as the administrator, with the tenant set for forced row security.</summary>
    private static string[] Ux67Sql(string tenant, string statements) =>
        Sql("hr_core_db", $"begin; select set_config('app.current_tenant', '{tenant}', true); {statements} commit;");

    /// <summary>Chromium signed in as hradmin; the session state is a temporary file the caller deletes.</summary>
    private static async Task<(IPlaywright Playwright, IBrowser Browser, string State)> Ux67BrowserAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var state = Path.Combine(Path.GetTempPath(), $"ux67-state-{Guid.NewGuid():N}.json");
        await Ux067PageProbe.SignInAndSaveStateAsync(browser, $"https://{AppDomain}", "hradmin", DemoPassword(), state);
        return (playwright, browser, state);
    }

    private static async Task Ux67CloseAsync((IPlaywright Playwright, IBrowser Browser, string State) chromium)
    {
        await chromium.Browser.DisposeAsync();
        chromium.Playwright.Dispose();
        if (File.Exists(chromium.State))
            File.Delete(chromium.State);
    }

    private static async Task Ux67PostAsync(IPage page, string selector)
    {
        await page.RunAndWaitForResponseAsync(() => page.ClickAsync(selector), r => r.Request.Method == "POST", new() { Timeout = 30_000 });
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    [SkippableFact]
    public async Task AC_UX_67_vps_every_key_page_carries_one_policy_and_raises_no_violation()
    {
        var recorder = StartUx67("csp", "Demo server: the key pages, one CSP policy each and no securitypolicyviolation, with screenshots at two sizes");
        var chromium = await Ux67BrowserAsync();
        var pages = Ux067PageProbe.KeyPages;
        string? request = null;
        string? draft = null;
        string? tenant = null;
        (HttpClient Client, System.Net.CookieContainer Cookies)? admin = null;
        try
        {
            // The demo holds no pending approval and no policy draft, so two key pages have nothing to open (P0). Outside the before
            // run, the check makes them for the pages' read and removes them after: a pending approval as a stated test-only
            // precondition (as the confirmation check's), and a draft through the template's own page, discarded through the editor's.
            if (!string.Equals(Ux67Label, "before", StringComparison.OrdinalIgnoreCase))
            {
                var template = Sql("hr_core_db",
                    "select t.\"Id\" || '|' || t.\"TenantId\" from \"PolicyTemplates\" t where t.\"RetiredAt\" is null and t.\"Domain\" <> 'Contracts.Leave' " +
                    "and not exists (select 1 from \"PolicyTemplateVersions\" v where v.\"PolicyTemplateId\" = t.\"Id\" and v.\"Status\" = 'Draft') " +
                    "order by t.\"Domain\" desc limit 1").Single().Split('|');
                tenant = template[1];
                request = Guid.NewGuid().ToString("D");
                Ux67Sql(tenant,
                    "insert into \"ApprovalRequests\" (\"Id\",\"TenantId\",\"EntityType\",\"EntityId\",\"Description\",\"RequestedByName\",\"RequestedAt\",\"Status\",\"CurrentLevel\",\"CreatedAt\",\"UpdatedAt\") " +
                    $"values ('{request}','{tenant}','Employee',gen_random_uuid(),'{Ux67Marker}','النظام',now(),0,1,now(),now()); " +
                    "insert into \"ApprovalSteps\" (\"Id\",\"TenantId\",\"ApprovalRequestId\",\"Level\",\"RoleRequired\",\"Status\",\"CreatedAt\",\"UpdatedAt\") " +
                    $"values (gen_random_uuid(),'{tenant}','{request}',1,'',0,now(),now());");
                admin = await SignInAsync("hradmin", DemoPassword(), "/ui/hr-core/PolicyTemplates");
                await Wf45PostRenderedAsync(admin.Value.Client, $"/ui/hr-core/PolicyTemplates/Details/{template[0]}", "CreateTemplateDraft");
                draft = Sql("hr_core_db", $"select \"Id\" from \"PolicyTemplateVersions\" where \"PolicyTemplateId\" = '{template[0]}' and \"Status\" = 'Draft'").SingleOrDefault();
                Assert.True(draft is not null, "The policy draft was not made through the template's page.");
                pages = pages.Select(p => p.Name switch
                {
                    "approval-review" => new Ux067KeyPage(p.Name, $"/ui/hr-core/Approvals/Review/{request}"),
                    "policy-rules" => new Ux067KeyPage(p.Name, $"/ui/hr-core/PolicyTemplates/EditRules?versionId={draft}"),
                    _ => p
                }).ToList();
            }

            var results = await Ux067PageProbe.ProbeAsync(
                chromium.Browser, $"https://{AppDomain}", chromium.State, pages, [(1366, 768), (390, 844)], Path.Combine(Ux67Out, "screenshots"));
            await File.WriteAllTextAsync(Path.Combine(Ux67Out, "csp-pages.json"), Ux067PageProbe.ToJson(results));

            recorder.WriteScenarioSteps(string.Join("\n", results.Select(r =>
                $"- {r.Name}: HTTP {r.Status}, {r.CspHeaders} CSP header(s), {r.CspPolicies} policies, {r.Violations.Count} violation(s)" +
                (r.Note is null ? "" : $" ({r.Note})"))));

            Assert.Equal(Ux067PageProbe.KeyPages.Count, results.Count);
            // The before run (P0) only records: the demo then carried two policies on every forwarded page.
            if (!string.Equals(Ux67Label, "before", StringComparison.OrdinalIgnoreCase))
            {
                var failures = results
                    .Where(r => !r.Passes)
                    .Select(r => $"{r.Name}: {r.CspPolicies} policies, {r.Violations.Count} violations {string.Join("; ", r.Violations.Select(v => $"{v.Directive} {v.BlockedUri} {v.Source}"))} {r.Note}")
                    .ToList();
                Assert.True(failures.Count == 0, "Key pages failing the CSP check:\n" + string.Join("\n", failures));
            }

            recorder.WriteResult("Pass");
        }
        finally
        {
            if (request is not null && tenant is not null)
                Ux67Sql(tenant, $"delete from \"ApprovalSteps\" where \"ApprovalRequestId\" = '{request}'; delete from \"ApprovalRequests\" where \"Id\" = '{request}';");
            if (draft is not null && admin is not null)
            {
                await Wf45PostRenderedAsync(admin.Value.Client, $"/ui/hr-core/PolicyTemplates/EditRules?versionId={draft}", "DiscardDraft");
                Assert.Empty(Sql("hr_core_db", $"select \"Id\" from \"PolicyTemplateVersions\" where \"Id\" = '{draft}' and \"Status\" = 'Draft'"));
            }

            admin?.Client.Dispose();
            await Ux67CloseAsync(chromium);
        }
    }

    [SkippableFact]
    public async Task AC_UX_67_vps_the_portal_is_styled_at_careers_and_the_module_root_opens_the_dashboard()
    {
        var recorder = StartUx67("portal", "Demo server: /careers styled (computed styles), no server-check text, its links answer; /ui/hr-core/ opens the dashboard for hradmin");
        var chromium = await Ux67BrowserAsync();
        try
        {
            var baseUrl = $"https://{AppDomain}";
            string[] styles;
            var links = new List<string>();
            await using (var context = await Ux067PageProbe.NewContextAsync(chromium.Browser, baseUrl, 1366, 768))
            {
                var page = await context.NewPageAsync();
                var response = await page.GotoAsync("/careers");
                Assert.Equal(200, response!.Status);
                styles = await page.EvaluateAsync<string[]>(
                    "() => ['.careers-navbar', '.careers-card', '.careers-btn-primary'].map(s => { const e = document.querySelector(s); if (!e) return 'missing'; const c = getComputedStyle(e); return c.backgroundImage !== 'none' ? c.backgroundImage : c.backgroundColor; })");
                Assert.All(styles.Take(2), s => Assert.True(s is not "missing" and not "rgba(0, 0, 0, 0)" and not "transparent", $"computed background: {s}"));
                var text = await page.InnerTextAsync("body");
                Assert.DoesNotContain("server check", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotMatch(@"\b\d{2}-\d{2}-\d{4}\b", text);
                Assert.Empty(await Ux067PageProbe.ReadViolationsAsync(page));

                // Every link of the portal to itself still answers (a posting, tracking, results).
                var hrefs = await page.EvalOnSelectorAllAsync<string[]>("a[href]", "els => els.map(e => e.href)");
                foreach (var href in hrefs.Where(h => new Uri(h).Host == AppDomain && new Uri(h).AbsolutePath.StartsWith("/careers", StringComparison.Ordinal)).Distinct().Take(20))
                {
                    var answer = await context.APIRequest.GetAsync(href, new() { MaxRedirects = 0 });
                    links.Add($"{new Uri(href).PathAndQuery} {answer.Status}");
                    Assert.True(answer.Status is >= 200 and < 400, $"{href} answered {answer.Status}");
                }
            }

            await using (var context = await Ux067PageProbe.NewContextAsync(chromium.Browser, baseUrl, 1366, 768, chromium.State))
            {
                var page = await context.NewPageAsync();
                var response = await page.GotoAsync("/ui/hr-core/");
                Assert.Equal(200, response!.Status);
                Assert.Equal(0, await page.Locator(".careers-navbar").CountAsync());
                Assert.True(await page.Locator(".app-sidebar").CountAsync() > 0, "/ui/hr-core/ did not open the dashboard.");
            }

            recorder.WriteScenarioDbChecks(new { navbar = styles[0], card = styles[1], button = styles[2], links });
            recorder.WriteResult("Pass");
        }
        finally
        {
            await Ux67CloseAsync(chromium);
        }
    }

    [SkippableFact]
    public async Task AC_UX_67_vps_the_policy_page_reads_in_arabic_with_sentences_and_damascus_times()
    {
        var recorder = StartUx67("policy", "Demo server: the Core.Establishment page shows Arabic labels, a sentence per rule, the JSON under «عرض تقني» for hradmin, and Damascus times");
        var template = Sql("hr_core_db", "select \"Id\" from \"PolicyTemplates\" where \"Domain\" = 'Core.Establishment' and \"Scope\" = 'National' order by \"CreatedAt\" limit 1");
        AcceptanceGate.RequireData(recorder, template.Length == 1, "لا قالب سياسة للملاك", "No national Core.Establishment template.");
        var rules = int.Parse(Sql("hr_core_db",
            $"select count(*) from \"PolicyRules\" r join \"PolicyTemplateVersions\" v on v.\"Id\" = r.\"PolicyTemplateVersionId\" where v.\"PolicyTemplateId\" = '{template[0]}'").Single(), CultureInfo.InvariantCulture);
        var chromium = await Ux67BrowserAsync();
        try
        {
            await using var context = await Ux067PageProbe.NewContextAsync(chromium.Browser, $"https://{AppDomain}", 1366, 768, chromium.State);
            var page = await context.NewPageAsync();
            await page.GotoAsync($"/ui/hr-core/PolicyTemplates/Details/{template[0]}");
            var text = await page.InnerTextAsync(".app-content");
            foreach (var label in new[] { "المجال", "نوع السجل", "النطاق", "القاعدة", "الأولوية", "نص القاعدة" })
                Assert.Contains(label, text, StringComparison.Ordinal);

            var sentences = await page.Locator("[data-testid='rule-sentence']").AllInnerTextsAsync();
            Assert.Equal(rules, sentences.Count);
            Assert.All(sentences, s =>
            {
                Assert.StartsWith("إذا ", s, StringComparison.Ordinal);
                Assert.DoesNotContain("انظر العرض التقني", s, StringComparison.Ordinal);
            });
            Assert.Equal(2 * rules, await page.Locator("details[data-testid='technical-json'] pre").CountAsync());

            var times = await page.EvaluateAsync<string[][]>("() => [...document.querySelectorAll('.app-content time[datetime]')].map(t => [t.getAttribute('datetime'), t.innerText.trim(), t.getAttribute('title') || ''])");
            Assert.NotEmpty(times);
            foreach (var t in times)
            {
                var utc = DateTime.ParseExact(t[0], "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                Assert.Equal(utc.AddHours(3).ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture), t[1]);
                Assert.Equal("بتوقيت دمشق", t[2]);
            }

            recorder.WriteScenarioDbChecks(new { template = template[0], rules, sentences, times = times.Length });
            recorder.WriteResult("Pass");
        }
        finally
        {
            await Ux67CloseAsync(chromium);
        }
    }

    [SkippableFact]
    public async Task AC_UX_67_vps_a_destructive_form_asks_first_and_the_reviewers_comments_reach_the_record()
    {
        var recorder = StartUx67("confirm", "Demo server: a requisition draft's delete asks first, cancel changes nothing, confirm deletes it; a reviewer's rejection comments reach the record; both records removed");
        var position = Sql("hr_core_db",
            "select p.\"Id\", p.\"OrgUnitId\", p.\"TenantId\" from \"FundedPositions\" p where p.\"Status\" = 'Vacant' and not exists " +
            "(select 1 from \"RecruitmentRequisitions\" r where r.\"FundedPositionId\" = p.\"Id\" and r.\"Status\" in ('Draft', 'PendingApproval', 'Approved', 'Published')) " +
            "order by p.\"PositionCode\" limit 1");
        AcceptanceGate.RequireData(recorder, position.Length == 1, "لا وظيفة شاغرة", "No vacant funded position without a requisition.");
        var (positionId, orgUnit, tenant) = (position[0].Split('|')[0], position[0].Split('|')[1], position[0].Split('|')[2]);

        // The draft, through its form, as AC-WF-43's check makes one (the demo's counter may collide: each attempt moves it).
        var (creator, _) = await SignInAsync("hradmin", DemoPassword());
        string draft;
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
                    ["JobTitle"] = "محلل بيانات",
                    ["JustificationNotes"] = Ux67Marker,
                    ["LegalSourceId"] = "SYR-LAW-50-2004",
                    ["BudgetYear"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
                    ["PublishingChannel"] = "PublicOnly"
                });
                rows = Sql("hr_core_db", $"select \"Id\" from \"RecruitmentRequisitions\" where \"FundedPositionId\" = '{positionId}' and \"Status\" = 'Draft'");
            }

            Assert.True(rows.Length == 1, $"create answered {status}");
            draft = rows[0];
        }

        var request = Guid.NewGuid().ToString("D");
        var chromium = await Ux67BrowserAsync();
        try
        {
            await using (var context = await Ux067PageProbe.NewContextAsync(chromium.Browser, $"https://{AppDomain}", 1366, 768, chromium.State))
            {
                var page = await context.NewPageAsync();
                await page.GotoAsync($"/ui/hr-core/RecruitmentRequisitions/Details/{draft}");
                const string delete = "form[data-testid='requisition-delete-form'] button[type='submit']";
                await page.ClickAsync(delete);
                await page.WaitForSelectorAsync("#hrConfirmModal.show #hrConfirmCancel", new() { Timeout = 10_000 });
                Assert.Contains("حذف مسودة طلب الشاغر؟", await page.InnerTextAsync("#hrConfirmMessage"), StringComparison.Ordinal);
                await page.ClickAsync("#hrConfirmCancel");
                await page.WaitForSelectorAsync("#hrConfirmModal", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
                Assert.Single(Sql("hr_core_db", $"select \"Id\" from \"RecruitmentRequisitions\" where \"Id\" = '{draft}' and \"Status\" = 'Draft'"));

                await page.ClickAsync(delete);
                await page.WaitForSelectorAsync("#hrConfirmModal.show #hrConfirmOk", new() { Timeout = 10_000 });
                await Ux67PostAsync(page, "#hrConfirmOk");
                Assert.Empty(Sql("hr_core_db", $"select \"Id\" from \"RecruitmentRequisitions\" where \"Id\" = '{draft}'"));
            }

            // A pending approval made for the check (test-only precondition), rejected with comments in the browser, then removed.
            Ux67Sql(tenant,
                "insert into \"ApprovalRequests\" (\"Id\",\"TenantId\",\"EntityType\",\"EntityId\",\"Description\",\"RequestedByName\",\"RequestedAt\",\"Status\",\"CurrentLevel\",\"CreatedAt\",\"UpdatedAt\") " +
                $"values ('{request}','{tenant}','Employee',gen_random_uuid(),'{Ux67Marker}','النظام',now(),0,1,now(),now()); " +
                "insert into \"ApprovalSteps\" (\"Id\",\"TenantId\",\"ApprovalRequestId\",\"Level\",\"RoleRequired\",\"Status\",\"CreatedAt\",\"UpdatedAt\") " +
                $"values (gen_random_uuid(),'{tenant}','{request}',1,'',0,now(),now());");
            const string comments = "المستندات ناقصة، يُعاد بعد استكمالها";
            await using (var context = await Ux067PageProbe.NewContextAsync(chromium.Browser, $"https://{AppDomain}", 1366, 768, chromium.State))
            {
                var page = await context.NewPageAsync();
                await page.GotoAsync($"/ui/hr-core/Approvals/Review/{request}");
                await page.FillAsync("#reviewComments", comments);
                await Ux67PostAsync(page, "[data-testid='approval-reject']");
            }

            var stored = Sql("hr_core_db", $"select \"Status\" || '|' || coalesce(\"ReviewComments\", '') from \"ApprovalRequests\" where \"Id\" = '{request}'");
            Assert.Equal($"2|{comments}", Assert.Single(stored));
            recorder.WriteScenarioSteps(
                "1. hradmin makes a requisition draft through its form; its delete asks first; cancel: the draft stays (psql); confirm: it is deleted (psql).\n" +
                "2. A pending approval made for the check, rejected in the browser with comments: the record holds them (psql); it is removed.");
            recorder.WriteResult("Pass");
        }
        finally
        {
            Ux67Sql(tenant, $"delete from \"ApprovalSteps\" where \"ApprovalRequestId\" = '{request}'; delete from \"ApprovalRequests\" where \"Id\" = '{request}';");
            // A draft left by a failed step is deleted through the application, as the check would have.
            if (Sql("hr_core_db", $"select \"Id\" from \"RecruitmentRequisitions\" where \"Id\" = '{draft}' and \"Status\" = 'Draft'").Length == 1)
            {
                var details = $"/ui/hr-core/RecruitmentRequisitions/Details/{draft}";
                await PostFormAsync(creator, $"/ui/hr-core/RecruitmentRequisitions/Delete/{draft}", new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(creator, details)
                });
            }

            creator.Dispose();
            await Ux67CloseAsync(chromium);
        }
    }

    /// <summary>
    /// D-67-6 (P9 step 4), run once on purpose (<c>UX067_CLOSE_RESIDUE=true</c>, skipped otherwise): every public posting the portal
    /// shows whose posting or requisition carries a server check's marker is closed as <c>hradmin</c> through the requisition's own
    /// «إغلاق» action (the same POST its form sends), which hides the posting. Nothing is deleted. The application has no reopen
    /// action, so each close's undo is the backup taken before the deploy; the ids are recorded.
    /// </summary>
    [SkippableFact]
    public async Task AC_UX_67_vps_close_the_public_postings_of_the_server_checks()
    {
        Skip.IfNot(string.Equals(Environment.GetEnvironmentVariable("UX067_CLOSE_RESIDUE"), "true", StringComparison.OrdinalIgnoreCase),
            "Closes demo data: run once on purpose with UX067_CLOSE_RESIDUE=true.");
        var recorder = StartUx67("residue", "Demo server: the server checks' public postings closed through the requisition's own action");
        const string marked =
            "select r.\"Id\" || '|' || p.\"Id\" || '|' || r.\"RequisitionNumber\" from \"PublicJobPostings\" p join \"RecruitmentRequisitions\" r on r.\"Id\" = p.\"RequisitionId\" " +
            "where p.\"IsPublished\" and r.\"Status\" = 'Published' and (p.\"Description\" ilike '%server check%' or r.\"JustificationNotes\" ilike '%server check%')";
        var rows = Sql("hr_core_db", marked);
        var (admin, _) = await SignInAsync("hradmin", DemoPassword());
        var closed = new List<object>();
        try
        {
            foreach (var row in rows)
            {
                var parts = row.Split('|');
                var details = $"/ui/hr-core/RecruitmentRequisitions/Details/{parts[0]}";
                var (status, _, _) = await PostFormAsync(admin, $"/ui/hr-core/RecruitmentRequisitions/Close/{parts[0]}", new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = await TokenAsync(admin, details)
                });
                var after = Sql("hr_core_db",
                    $"select r.\"Status\" || '|' || p.\"IsPublished\" from \"RecruitmentRequisitions\" r join \"PublicJobPostings\" p on p.\"RequisitionId\" = r.\"Id\" where r.\"Id\" = '{parts[0]}'");
                Assert.Equal("Closed|false", Assert.Single(after));
                closed.Add(new { requisition = parts[0], number = parts[2], posting = parts[1], status, undo = "the backup taken before the deploy (no reopen action)" });
            }
        }
        finally
        {
            admin.Dispose();
        }

        recorder.WriteScenarioDbChecks(new { found = rows.Length, closed });
        Assert.Empty(Sql("hr_core_db", marked));
        recorder.WriteResult("Pass");
    }

    /// <summary>
    /// After the server suites (P9 5.8): how many users the registry knows, how many people that pages print stay unknown to it, and
    /// that no published public posting carries a server check's marker. The people counted are the users in the columns that the
    /// policy, approval, requisition and establishment pages print.
    /// </summary>
    [SkippableFact]
    public void AC_UX_67_vps_after_the_suites_the_registry_names_the_users_and_no_public_residue_is_left()
    {
        var recorder = StartUx67("names", "Demo server, after the suites: users the registry knows, people still unknown, and no public residue");
        var known = Sql("gateway_audit_db", "select lower(\"Sub\") from gateway_user_names").ToHashSet(StringComparer.Ordinal);
        var printed = Sql("hr_core_db",
            "select distinct lower(x) from (" +
            "select \"MakerUserId\"::text as x from \"PolicyPublications\" union all select \"CheckerUserId\"::text from \"PolicyPublications\" " +
            "union all select \"RequestedBy\"::text from \"ApprovalRequests\" union all select \"ReviewedBy\"::text from \"ApprovalRequests\" " +
            "union all select \"CreatedBy\"::text from \"RecruitmentRequisitions\" union all select \"CancelledBy\"::text from \"RecruitmentRequisitions\" " +
            "union all select \"CreatedBy\"::text from \"EstablishmentChangeRequests\" union all select \"AppliedBy\"::text from \"EstablishmentChangeRequests\"" +
            ") s where x is not null and x ~ '^[0-9a-fA-F-]{36}$' and x <> '00000000-0000-0000-0000-000000000000'");
        var unknown = printed.Where(p => !known.Contains(p)).ToList();
        var residue = Sql("hr_core_db",
            "select p.\"Id\" from \"PublicJobPostings\" p left join \"RecruitmentRequisitions\" r on r.\"Id\" = p.\"RequisitionId\" " +
            "where p.\"IsPublished\" and (p.\"Description\" ilike '%server check%' or r.\"JustificationNotes\" ilike '%server check%')");

        recorder.WriteScenarioDbChecks(new { registryUsers = known.Count, printedPeople = printed.Length, unknownPeople = unknown.Count, unknown, publicResidue = residue });
        Assert.NotEmpty(known);
        Assert.Empty(residue);
        recorder.WriteResult("Pass");
    }
}
