using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HRManagement.SharedKernel.Policy;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-POL-49f and the start-up refusal of AC-POL-49a (PLAN-49, G-POL-09 and G-POL-02), on the prod-like stack with
/// docker-compose.acceptance-pol49.yml: hr-core-web runs Core.Recruitment in Enforce, and the acceptance seed has added one rule
/// to the national recruitment policy, at the first priority: every offer that is issued fires the ordinary offer approval
/// (ACC-POL49-WEAK-OFFER). An ordinary offer gets the workflow it asked for, started by the policy; an exception offer, which asks
/// for the override approval, is refused, and the instance the policy started for it is cancelled. The class asserts the container's
/// own mode and the rule before it relies on them. It runs alone, in the pol49 stage of tests/acceptance/ci-scope.json, on a stack
/// started for it; it stops and recreates no container of the stack (the start-up refusal uses one more container, removed after).
/// </summary>
[RequiredAcceptance]
public class AcPol49EnforceOverlayTests
{
    private const string WeakRule = "ACC-POL49-WEAK-OFFER";
    private const string OfferCode = "RECRUITMENT-OFFER-APPROVAL";
    private const string OverrideCode = "RECRUITMENT-OFFER-OVERRIDE-APPROVAL";
    private const string RefusedMessage = "السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء";
    private const string CancelAuditAction = "PolicyWorkflowCodeRefused";
    private const string OverrideReason = "المرشح الأول طلب تأجيل المباشرة ستة أشهر وأوصت اللجنة بالمرشح التالي في الترتيب";
    private static readonly string[] Personas = ["central-a", "br1-mgr", "a-management-approver"];
    private static readonly Guid TenantA = PolicyScopeJourney.TenantA;

    private static string RepoRoot => AcceptanceHarness.RepoRoot;

    private static async Task<string> ContainerEnvironmentAsync(string service, string variable)
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(service);
        var (_, environment) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "inspect", "-f", "{{range .Config.Env}}{{println .}}{{end}}", container);
        return Regex.Match(environment, $@"(?m)^{Regex.Escape(variable)}=(\S*)").Groups[1].Value.Trim();
    }

    private static async Task<string> ContainerIdentityAsync(string service)
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(service);
        var (_, identity) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "inspect", "-f", "{{.Id}} {{.State.StartedAt}} {{.RestartCount}}", container);
        return identity.Trim();
    }

    /// <summary>The stage has just recreated hr-core-web: it is ready when it answers, and its seed has run by then.</summary>
    private static async Task HrCoreReadyAsync()
    {
        var url = (Environment.GetEnvironmentVariable("HR_CORE_DIRECT_URL") ?? "http://localhost:6210").TrimEnd('/') + "/health/ready";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddMinutes(5);
        do
        {
            try
            {
                using var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Still starting.
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        } while (DateTime.UtcNow < deadline);

        Assert.Fail("hr-core-web is not ready.");
    }

    private static async Task<string> RankedApplicationAsync(RecruitmentSession s, string step, string requisition, int rank)
    {
        var application = await CreateApplicationAsync(s, step, requisition);
        // Test-only precondition, as AC-HCM-25b: the ranking itself is AC-HCM-26's.
        await Sql(
            $"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppRanked}, \"Rank\"={rank}, \"AssessmentScore\"={100 - (rank * 5)}, \"RankedAt\"=now(), " +
            $"\"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{application}';");
        return application;
    }

    private static async Task<string> DraftOfferAsync(RecruitmentSession s, string step, string application, decimal salary, bool meritOverride)
    {
        var form = OfferForm(application, salary);
        if (meritOverride)
        {
            form.Add(new("IsMeritOverride", "true"));
            form.Add(new("MeritOverrideReason", OverrideReason));
        }

        var page = await s["central-a"].PostAsync(step, "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(application), form);
        var offer = await Sql($"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{application}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offer, out _), $"{step}: the offer was not created: {AcS2s42TrustBoundaryTests.Alerts(page)}");
        return offer;
    }

    private static Task<string> InstancesOfAsync(string entity) =>
        Sql("SELECT COALESCE(string_agg(i.\"Status\" || ':' || d.\"Code\", ',' ORDER BY i.\"StartedAt\", i.\"Id\"), '') " +
            "FROM \"WorkflowInstances\" i JOIN \"WorkflowDefinitions\" d ON d.\"Id\"=i.\"DefinitionId\" " +
            $"WHERE i.\"EntityId\"='{entity}';");

    // ------------------------------------------------------------------------------------------------ 49f

    [SkippableFact]
    public async Task Ac49f_In_Enforce_a_policy_may_start_only_the_workflow_the_service_asked_for()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(PolicyScopeJourney.Ac, "49f",
            "Core.Recruitment in Enforce with a weak offer rule: an ordinary offer takes the policy's instance; an exception offer is refused and the policy's instance cancelled",
            Personas);
        await HrCoreReadyAsync();

        // The two things the stage sets up, asserted on the stack itself: the mode of the running container, and the rule.
        var mode = await ContainerEnvironmentAsync("hr-core-web", "PolicyEngine__Core.Recruitment");
        AcceptanceGate.RequireData(s.Recorder, mode == "Enforce",
            "hr-core-web لا يعمل بوضع Enforce لمجال Core.Recruitment: هذا الصنف يعمل في مرحلة pol49 فقط",
            $"hr-core-web runs Core.Recruitment in '{mode}', not Enforce: this class runs in the pol49 stage (docker-compose.acceptance-pol49.yml).");
        var rules = await PolicyScopeJourney.EffectiveNationalRulesAsync(PolicyDomains.CoreRecruitment);
        AcceptanceGate.RequireData(s.Recorder, rules.Contains(WeakRule),
            "قاعدة القبول ACC-POL49-WEAK-OFFER غير منشورة في السياسة الوطنية للتوظيف",
            "The acceptance rule ACC-POL49-WEAK-OFFER is not in the effective national recruitment version (Seed__Pol49WeakOfferRule).");
        var weak = await Sql($$"""
            SELECT r."Priority" || '|' || r."ThenJson" FROM "PolicyRules" r JOIN "PolicyTemplateVersions" v ON v."Id" = r."PolicyTemplateVersionId"
            WHERE r."RuleKey" = '{{WeakRule}}' AND v."Status" = 'Published' ORDER BY v."VersionNumber" DESC LIMIT 1;
            """);
        Assert.StartsWith("1|", weak, StringComparison.Ordinal);
        Assert.Contains(OfferCode, weak, StringComparison.Ordinal);
        var page = await s["central-a"].GetAsync("policy-page", "ui/hr-core/PolicyTemplates");
        Assert.Matches(@"data-domain=""Core\.Recruitment"" data-status=""governed"" data-mode=""Enforce""", page);

        // ---- Two published requisitions: one with a single ranked candidate, one with two.
        var salary = await MidSalaryAsync(s);
        var ordinaryRequisition = await PublishRequisitionAsync(s, "49f-ordinary");
        var top = await RankedApplicationAsync(s, "49f-ordinary", ordinaryRequisition, 1);
        var exceptionRequisition = await PublishRequisitionAsync(s, "49f-exception");
        await RankedApplicationAsync(s, "49f-exception-first", exceptionRequisition, 1);
        var second = await RankedApplicationAsync(s, "49f-exception-second", exceptionRequisition, 2);

        // ---- Positive: an ordinary offer asks for the ordinary approval, and gets the instance the policy started.
        var ordinary = await DraftOfferAsync(s, "49f-ordinary-offer", top, salary, meritOverride: false);
        await s["central-a"].PostAsync("49f-ordinary-submit", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(ordinary), [new("id", ordinary)]);
        var ordinaryState = (await OfferStateAsync(ordinary)).Split('|');
        Assert.Equal([OfferPending.ToString(CultureInfo.InvariantCulture), "1"], [ordinaryState[0], ordinaryState[2]]);
        Assert.Equal($"InProgress:{OfferCode}", await InstancesOfAsync(ordinary));
        var ordinaryRow = Assert.Single(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{ordinary}'"""));
        Assert.Equal("Enforce", ordinaryRow.Mode);
        Assert.Null(ordinaryRow.Caller);
        Assert.Equal(TenantA, ordinaryRow.Tenant);
        Assert.Contains(WeakRule, ordinaryRow.Decision.MatchedRuleIds);
        Assert.Equal(OfferCode, ordinaryRow.Decision.GetWorkflowCode());
        // Started by the policy, and taken as it is: not started a second time by the service.
        Assert.Equal(Guid.Parse(ordinaryState[1]), ordinaryRow.Instance);

        // ---- Negative: an exception offer asks for the override approval; the weak rule would give it the ordinary one.
        var exception = await DraftOfferAsync(s, "49f-exception-offer", second, salary, meritOverride: true);
        Assert.Equal("true", await Sql($"SELECT \"IsMeritOverride\"::text FROM \"OfferLetters\" WHERE \"Id\"='{exception}';"));
        var refusedPage = await s["central-a"].PostAsync("49f-exception-submit", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(exception), [new("id", exception)]);

        var exceptionState = (await OfferStateAsync(exception)).Split('|');
        Assert.Equal(OfferDraft.ToString(CultureInfo.InvariantCulture), exceptionState[0]);
        Assert.Equal(string.Empty, exceptionState[1]);
        Assert.Equal($"Cancelled:{OfferCode}", await InstancesOfAsync(exception));
        var cancelled = await Sql($$"""
            SELECT i."Id" || '|' || (i."CompletedAt" IS NOT NULL)::text || '|' || (i."StepHistoryJson" LIKE '%(PLAN-49)%')::text
                   || '|' || (i."StepHistoryJson" LIKE '%{{OverrideCode}}%')::text || '|' || (i."TenantId" = '{{TenantA:D}}')::text
            FROM "WorkflowInstances" i WHERE i."EntityId" = '{{exception}}';
            """);
        var cancelledId = Guid.Parse(cancelled.Split('|')[0]);
        Assert.Equal("true|true|true|true", cancelled[(cancelled.IndexOf('|') + 1)..]);
        var exceptionRow = Assert.Single(await PolicyScopeJourney.LedgerAsync($"""l."EntityId" = '{exception}'"""));
        Assert.Equal("Enforce", exceptionRow.Mode);
        Assert.Contains(WeakRule, exceptionRow.Decision.MatchedRuleIds);
        Assert.Equal(OfferCode, exceptionRow.Decision.GetWorkflowCode());
        Assert.Equal(cancelledId, exceptionRow.Instance);
        var central = await PolicyScopeJourney.PersonaUserIdAsync("central-a");
        var audit = await Sql($"SELECT \"Action\" || '|' || \"EntityType\" || '|' || \"TenantId\" || '|' || COALESCE(\"UserId\"::text,'') FROM \"AuditLogs\" WHERE \"EntityId\"='{exception}' AND \"Action\"='{CancelAuditAction}';");
        Assert.Equal($"{CancelAuditAction}|OfferLetter|{TenantA:D}|{central:D}", audit);
        // The person is told why, on the page the submission comes back to.
        Assert.Contains(RefusedMessage, refusedPage, StringComparison.Ordinal);

        // Nobody is asked to approve the cancelled instance: the first step's holder does not find the offer in the inbox.
        var inbox = await InboxAsync(s["br1-mgr"], "49f-inbox");
        Assert.True(inbox.Has($"/OfferLetters/Details/{ordinary}"), "49f: the ordinary offer is not in br1-mgr's inbox.");
        Assert.False(inbox.Has($"/OfferLetters/Details/{exception}"), "49f: the refused exception offer is in br1-mgr's inbox.");
        Assert.DoesNotContain(cancelledId.ToString("D"), inbox.Html, StringComparison.OrdinalIgnoreCase);

        // A second attempt is refused the same way: the cancelled instance is not picked up again, and nothing runs for the offer.
        await s["central-a"].PostAsync("49f-exception-submit-again", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(exception), [new("id", exception)]);
        Assert.Equal(OfferDraft.ToString(CultureInfo.InvariantCulture), (await OfferStateAsync(exception)).Split('|')[0]);
        Assert.Equal($"Cancelled:{OfferCode},Cancelled:{OfferCode}", await InstancesOfAsync(exception));
        Assert.Equal(0, await Scalar($"SELECT count(*) FROM \"WorkflowInstances\" WHERE \"EntityId\"='{exception}' AND \"Status\"='InProgress';"));

        await s.AuditAsync(ordinary, exception);
        s.Steps(
            "Stage pol49: docker-compose.acceptance-pol49.yml on a fresh prod-like stack (hr-core-web recreated with PolicyEngine:Core.Recruitment=Enforce and Seed:Pol49WeakOfferRule=true).\n" +
            "0. Asserted first: the running hr-core-web has PolicyEngine__Core.Recruitment=Enforce (docker inspect), the policy page shows the domain in Enforce, and the effective national recruitment version holds ACC-POL49-WEAK-OFFER at priority 1 (every offer issue -> RECRUITMENT-OFFER-APPROVAL).\n" +
            "1. central-a publishes two requisitions through their approvals (in Enforce: the requisition rule starts the workflow the service asked for). Test-only SQL ranks the candidates (ranking is AC-HCM-26's).\n" +
            "2. Positive: an ordinary offer for the first candidate is submitted: PendingApproval with one RECRUITMENT-OFFER-APPROVAL instance, and that instance is the one the policy's ledger row names (Mode Enforce, the weak rule matched).\n" +
            "3. Negative: an exception offer (merit override, which asks for RECRUITMENT-OFFER-OVERRIDE-APPROVAL) for the second candidate of the other requisition is submitted: the page says «السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء»; the offer stays a draft with no instance linked; the one instance of the offer is the policy's RECRUITMENT-OFFER-APPROVAL, Cancelled, completed, with the reason and both codes in its history; the ledger row names it; an audit row PolicyWorkflowCodeRefused is kept in the agency's tenant.\n" +
            "4. br1-mgr's inbox lists the ordinary offer and not the refused one. A second submission is refused the same way: two cancelled instances, none in progress.");
        s.Checks(new
        {
            mode,
            weakRule = weak,
            ordinary = new { offer = ordinary, status = "PendingApproval", instance = ordinaryState[1], ledger = new { ordinaryRow.Mode, ordinaryRow.Instance, matched = ordinaryRow.Decision.MatchedRuleIds } },
            exception = new
            {
                offer = exception,
                status = "Draft",
                linkedInstance = exceptionState[1],
                instances = $"Cancelled:{OfferCode},Cancelled:{OfferCode}",
                firstCancelled = cancelledId,
                ledger = new { exceptionRow.Mode, exceptionRow.Instance, workflow = exceptionRow.Decision.GetWorkflowCode() },
                audit,
                refusalShownOnThePage = true
            }
        });
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ 49a (start-up)

    private static async Task<(int Exit, string Output)> PowerShellAsync(string script, string arguments)
    {
        var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }

    [SkippableFact]
    public async Task Ac49a_boot_A_mode_for_a_domain_out_of_scope_stops_the_service_at_start_up()
    {
        var r = PolicyScopeJourney.Recorder("49a-boot", "hr-core-web refuses to start with PolicyEngine:IT.Tickets=Shadow; the running service is untouched", "platform");
        AcceptanceGate.RequireEnabled(r);
        await HrCoreReadyAsync();
        var before = await ContainerIdentityAsync("hr-core-web");

        var script = Path.Combine(RepoRoot, "scripts", "pol049-outofscope-mode-boot-check.ps1");
        var (exit, output) = await PowerShellAsync(script, $"-RepoRoot \"{RepoRoot}\"");
        r.CaptureHttp("boot-check", "RUN", "scripts/pol049-outofscope-mode-boot-check.ps1", exit);

        Assert.True(exit == 0, output);
        Assert.Contains("pol049-outofscope-mode-boot-check: PASS", output, StringComparison.Ordinal);
        Assert.Contains("PolicyEngine:IT.Tickets must be Off", output, StringComparison.Ordinal);
        Assert.Contains("out of the policy engine's scope", output, StringComparison.Ordinal);
        var exitCode = Regex.Match(output, @"PASS \(exit code (-?\d+)\)").Groups[1].Value;
        Assert.NotEqual("0", exitCode);

        // The stack is as it was: the same container, never restarted, still ready; the one-off container is gone.
        Assert.Equal(before, await ContainerIdentityAsync("hr-core-web"));
        await HrCoreReadyAsync();
        var (_, leftovers) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "ps", "-a", "--filter", "name=pol049-boot-", "--format", "{{.Names}}");
        Assert.Equal(string.Empty, leftovers.Trim());

        // Positive: the same service runs with its governed domains set (Shadow, and Enforce in this stage): the check refuses
        // only what is out of scope.
        var governedMode = await ContainerEnvironmentAsync("hr-core-web", "PolicyEngine__Core.Establishment");
        Assert.Equal("Shadow", governedMode);

        r.WriteScenarioDbChecks(new
        {
            script = "scripts/pol049-outofscope-mode-boot-check.ps1",
            exit,
            oneOffExitCode = exitCode,
            message = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("must be Off", StringComparison.Ordinal)),
            runningContainerUnchanged = true,
            leftovers = 0,
            governedModeOfTheRunningService = governedMode
        });
        r.WriteScenarioSteps(
            "1. scripts/pol049-outofscope-mode-boot-check.ps1 starts one more hr-core-web container of the stack with PolicyEngine__IT.Tickets=Shadow (docker compose run --no-deps).\n" +
            "2. That container exits with a code other than 0, and its output says «PolicyEngine:IT.Tickets must be Off: the domain is out of the policy engine's scope». The check runs before the migrations and the seeds, so the database is not touched.\n" +
            "3. The running hr-core-web is the same container, not restarted, and ready; the one-off container is removed.\n" +
            "4. Positive: that running service has modes for its governed domains and started: only a domain out of scope is refused.");
        r.WriteResult("Pass");
    }
}
