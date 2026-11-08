using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-POL-47 (PLAN-47) on the prod-like stack: the policy rule lifecycle on agency A's custody template (TenantOverride, seeded
/// v1 archived, v2 published, v3 draft). central-a authors (hr-core), a-management-approver publishes. One ordered journey: a
/// draft copied from the effective version and published with four eyes (a), strict validation (b), a version that takes effect
/// later and evaluation as of a date (c), a rollback to v2 (d), published versions unchanged across two restarts of hr-core-web
/// and the trigger refusing SQL (e), the seed change sets recorded once (f), IPolicyPublished through the outbox (g).
/// Assets.Custody is Off on this stack, so the versions published here change no other class's decisions; d restores v2's rules.
/// </summary>
[RequiredAcceptance]
[Collection(AcMsg40Collection.Name)]
public class AcPol47RuleLifecycleTests
{
    private const string Ac = "AC-POL-47";
    private const string Domain = PolicyDomains.AssetsCustody;
    private static readonly Guid TenantA = AcceptanceSeedIds.AgencyAId;
    private static readonly Guid Template = AcceptanceSeedIds.PolicyTemplateId;
    private static readonly Guid V2 = AcceptanceSeedIds.PolicyVersion2Id;
    private static readonly Guid V3 = AcceptanceSeedIds.PolicyVersion3DraftId;
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(60);

    private const string CategoryWhen = """{"all":[{"field":"category","operator":"Equals","value":"أصول ثابتة"}]}""";
    private const string EvidenceThen = """[{"effect":"RequireEvidence"}]""";

    private static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);

    private static async Task<long> Count(string sql) => long.Parse((await Sql(sql)).Trim(), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<(HttpClient Http, Guid UserId)> ClientAsync(string personaId)
    {
        var persona = AcceptanceHarness.RequirePersona(personaId);
        var (token, correlation) = await AcceptanceHarness.LoginAsync(persona);
        var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlation);
        return (http, Guid.Parse(AcceptanceHarness.ParseJwt(token).Subject));
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpClient http, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement json = default;
        if (!string.IsNullOrWhiteSpace(text) && (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[')))
            json = JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, json);
    }

    private static object Rule(string key, string when, string then = EvidenceThen, int priority = 30) =>
        new { ruleKey = key, name = "AC-POL-47 " + key, priority, whenJson = when, thenJson = then };

    private static async Task<string> FingerprintAsync()
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "pol047-policy-fingerprint.sql"));
        return (await Sql(sql)).Trim();
    }

    private static Dictionary<string, string> ParseFingerprint(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("VersionId").GetString()!, e => e.GetProperty("RulesSha256").GetString()!);

    private static async Task<string> RulesPrintAsync(Guid versionId) =>
        (await Sql($"""
            SELECT string_agg("RuleKey" || ':' || "Priority" || ':' || "WhenJson" || ':' || "ThenJson", '|' ORDER BY "RuleKey")
            FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{versionId:D}';
            """)).Trim();

    private static async Task<Guid> OpenDraftAsync(HttpClient author)
    {
        var (status, body) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/templates/{Template:D}/drafts", new { });
        Assert.True(status == HttpStatusCode.OK, $"create draft -> {(int)status} {body}");
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> EvaluatedVersionAsync(HttpClient http, Dictionary<string, string> facts, DateTime? asOf = null)
    {
        var (status, body) = await SendAsync(http, HttpMethod.Post, "api/hr-core/api/policies/evaluate", new
        {
            domain = Domain,
            subjectType = "Asset",
            action = "Assign",
            facts,
            asOfUtc = asOf
        });
        Assert.True(status == HttpStatusCode.OK, $"evaluate -> {(int)status} {body}");
        return body.GetProperty("policyVersionId").GetGuid();
    }

    private static async Task<long> OutboxSequenceAsync() =>
        await Count("""SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM "OutboxMessage_SequenceNumber_seq";""");

    private static async Task RestartHrCoreAsync()
    {
        var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("hr-core-web");
        var (exit, output) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "restart", container);
        Assert.True(exit == 0, output);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if ((await http.GetAsync("http://localhost:6210/health/ready")).IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Still starting.
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.Fail("HR.Core did not become ready after the restart.");
    }

    [SkippableFact]
    public async Task Ac47_the_policy_rule_lifecycle_from_draft_to_rollback()
    {
        var a = Recorder("47a", "A draft copied from the effective version, published by an approver who did not edit it", "central-a, a-management-approver");
        AcceptanceGate.RequireEnabled(a);
        var (author, authorId) = await ClientAsync("central-a");
        var (approver, approverId) = await ClientAsync("a-management-approver");
        using var _ = author;
        using var __ = approver;

        // ---------------------------------------------------------------------------------------------- 47a
        // The seeded v3 draft (AC-10's, on the lab stack) holds the one draft this template may have: discarded first.
        var seededDraft = (await Sql($"""SELECT "Status" FROM "PolicyTemplateVersions" WHERE "Id" = '{V3:D}';""")).Trim();
        if (seededDraft == "Draft")
        {
            var (discard, discardBody) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{V3:D}/discard");
            Assert.True(discard == HttpStatusCode.OK, $"discard v3 -> {(int)discard} {discardBody}");
        }

        var v2Print = await RulesPrintAsync(V2);
        var draftA = await OpenDraftAsync(author);
        Assert.Equal(v2Print, await RulesPrintAsync(draftA));
        var (added, addedBody) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{draftA:D}/rules", Rule("ACC-P47-CAT", CategoryWhen));
        Assert.True(added == HttpStatusCode.OK, $"add rule -> {(int)added} {addedBody}");

        var (approverAdds, _) = await SendAsync(approver, HttpMethod.Post, $"api/hr-core/api/policies/versions/{draftA:D}/rules", Rule("ACC-P47-APPR", CategoryWhen));
        var (authorPublishes, _) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/templates/{draftA:D}/publish", new { evidenceRef = "AC-POL-47a" });
        Assert.Equal(HttpStatusCode.Forbidden, approverAdds);
        Assert.Equal(HttpStatusCode.Forbidden, authorPublishes);

        var sequenceBefore = await OutboxSequenceAsync();
        var (published, publishedBody) = await SendAsync(approver, HttpMethod.Post, $"api/hr-core/api/policies/templates/{draftA:D}/publish", new { evidenceRef = "AC-POL-47a" });
        Assert.True(published == HttpStatusCode.OK, $"publish -> {(int)published} {publishedBody}");
        var sequenceAfter = await OutboxSequenceAsync();

        var facts = new Dictionary<string, string> { ["category"] = "أصول ثابتة", ["isPrivileged"] = "false", ["action"] = "Assign" };
        var (evaluated, evaluatedBody) = await SendAsync(author, HttpMethod.Post, "api/hr-core/api/policies/evaluate",
            new { domain = Domain, subjectType = "Asset", action = "Assign", facts });
        Assert.Equal(HttpStatusCode.OK, evaluated);
        Assert.Equal(draftA, evaluatedBody.GetProperty("policyVersionId").GetGuid());
        Assert.Contains("ACC-P47-CAT", evaluatedBody.GetProperty("matchedRuleIds").EnumerateArray().Select(e => e.GetString()));
        var publication = (await Sql($"""
            SELECT "MakerUserId" || '|' || "CheckerUserId" || '|' || "Kind" || '|' || "MakerRole" || '|' || "CheckerRole"
            FROM "PolicyPublications" WHERE "PolicyTemplateVersionId" = '{draftA:D}';
            """)).Trim();
        Assert.Equal($"{authorId:D}|{approverId:D}|Manual|policy-author|hr-management-approver", publication);
        Assert.Equal("Published", (await Sql($"""SELECT "Status" FROM "PolicyTemplateVersions" WHERE "Id" = '{draftA:D}';""")).Trim());

        // Negative: the old override in the body is ignored; an approver who is an editor of the draft cannot publish it.
        var selfDraft = Guid.NewGuid();
        await Sql($$"""
            INSERT INTO "PolicyTemplateVersions" ("Id","PolicyTemplateId","VersionNumber","Status","TenantId","CreatedAt","UpdatedAt","CreatedBy","WithdrawsAllRules")
            VALUES ('{{selfDraft:D}}','{{Template:D}}',(SELECT max("VersionNumber") + 1 FROM "PolicyTemplateVersions" WHERE "PolicyTemplateId" = '{{Template:D}}'),
                    'Draft','{{TenantA:D}}',now(),now(),'{{approverId:D}}',false);
            INSERT INTO "PolicyRules" ("Id","PolicyTemplateVersionId","RuleKey","Name","Priority","WhenJson","ThenJson","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),'{{selfDraft:D}}','ACC-P47-SELF','self',40,'{{CategoryWhen}}','{{EvidenceThen}}','{{TenantA:D}}',now(),now());
            SELECT 1;
            """);
        var (selfPublish, selfBody) = await SendAsync(approver, HttpMethod.Post, $"api/hr-core/api/policies/templates/{selfDraft:D}/publish",
            new { evidenceRef = "AC-POL-47a-self", forceSameActorForLab = true });
        Assert.Equal(HttpStatusCode.Forbidden, selfPublish);
        Assert.Equal("Draft", (await Sql($"""SELECT "Status" FROM "PolicyTemplateVersions" WHERE "Id" = '{selfDraft:D}';""")).Trim());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{selfDraft:D}/discard")).Status);

        a.WriteScenarioDbChecks(new { discardedSeededDraft = seededDraft == "Draft", draftA, copiedRules = v2Print, approverAdds = (int)approverAdds, authorPublishes = (int)authorPublishes, publication, selfPublish = (int)selfPublish, selfBody = selfBody.ToString() });
        a.WriteScenarioSteps(
            "1. central-a discards the seeded v3 draft (one draft per template), creates a draft: its rules are v2's; adds ACC-P47-CAT (category Equals 'أصول ثابتة' -> RequireEvidence).\n" +
            "2. Negatives: a-management-approver adds a rule -> 403 (not an author); central-a publishes -> 403 (not an approver).\n" +
            "3. a-management-approver publishes: 200; POST api/policies/evaluate returns the new version and matches ACC-P47-CAT; the publication records maker central-a, checker a-management-approver, Manual, policy-author/hr-management-approver (the checker's real role).\n" +
            "4. Negative: a draft whose CreatedBy is the approver, published by the approver with forceSameActorForLab=true in the body -> 403 and it stays a draft (then discarded).");
        a.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 47g (the publication of 47a)
        var g = Recorder("47g", "IPolicyPublished in HR.Core's outbox, staged by the publication's own save, then delivered", "a-management-approver");
        var messageId = DeterministicMessageId.For<IPolicyPublished>(draftA);
        var pending = long.MaxValue;
        var deadline = DateTime.UtcNow.Add(DeliveryWindow);
        while (DateTime.UtcNow < deadline && (pending = await Count($"""SELECT count(*) FROM "OutboxMessage" WHERE "MessageId" = '{messageId:D}';""")) > 0)
            await Task.Delay(1000);
        Assert.True(sequenceAfter > sequenceBefore, "the publication wrote no outbox row");
        Assert.Equal(0, pending);
        g.WriteScenarioDbChecks(new { messageType = "IPolicyPublished", versionId = draftA, messageId, sequenceBefore, sequenceAfter, pendingAfterDelivery = pending });
        g.WriteScenarioSteps("1. The OutboxMessage sequence of hr_core_db moved with the publication of 47a (its own save).\n2. No row is left for its deterministic MessageId within 60 s: delivered (every HR.Core instance clears its cache on it).");
        g.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 47b
        var b = Recorder("47b", "Strict validation: an inclusive number rule accepted and matched; malformed rules refused with an Arabic message and path", "central-a");
        var draftB = await OpenDraftAsync(author);
        var (le, leBody) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{draftB:D}/rules",
            Rule("ACC-P47-LE", """{"all":[{"field":"value","operator":"LessThanOrEqual","value":"10000.5"}]}"""));
        Assert.True(le == HttpStatusCode.OK, $"LessThanOrEqual rule -> {(int)le} {leBody}");
        async Task<IEnumerable<string>> SimulateAsync(string value)
        {
            var (status, body) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/templates/{draftB:D}/simulate",
                new Dictionary<string, string> { ["value"] = value, ["action"] = "Assign" });
            Assert.True(status == HttpStatusCode.OK, $"simulate -> {(int)status} {body}");
            return body.GetProperty("matchedRuleIds").EnumerateArray().Select(e => e.GetString()!);
        }
        Assert.Contains("ACC-P47-LE", await SimulateAsync("10000.5"));
        Assert.DoesNotContain("ACC-P47-LE", await SimulateAsync("10001"));

        var rulesBefore = await Count($"""SELECT count(*) FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{draftB:D}';""");
        var refusals = new List<object>();
        foreach (var (key, when, then, path) in new[]
                 {
                     ("ACC-P47-B1", """{"when":{"all":[{"field":"category","operator":"Equals","value":"x"}]}}""", EvidenceThen, "$.when.when"),
                     ("ACC-P47-B2", """{"all":[{"field":"value","operator":"LessThanOrEqualz","value":"1"}]}""", EvidenceThen, "$.when.all[0].operator"),
                     ("ACC-P47-B3", """{"all":[{"field":"employeeName","operator":"Equals","value":"x"}]}""", EvidenceThen, "$.when.all[0].field"),
                     ("ACC-P47-B4", CategoryWhen, """[{"effect":"TriggerWorkflow","params":{"workflowCode":"NOPE"}}]""", "$.then[0].params.workflowCode"),
                     ("ACC-P47-B5", CategoryWhen, "[]", "$.then")
                 })
        {
            var (status, body) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{draftB:D}/rules", Rule(key, when, then));
            Assert.Equal(HttpStatusCode.BadRequest, status);
            var issues = body.GetProperty("issues").EnumerateArray().ToList();
            Assert.Contains(issues, i => i.GetProperty("jsonPath").GetString() == path);
            Assert.Matches("[؀-ۿ]", body.GetProperty("error").GetString());
            refusals.Add(new { key, status = (int)status, path, error = body.GetProperty("error").GetString() });
        }
        Assert.Equal(rulesBefore, await Count($"""SELECT count(*) FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{draftB:D}';"""));
        b.WriteScenarioDbChecks(new { draftB, rulesBefore, refusals });
        b.WriteScenarioSteps(
            "1. A new draft; ACC-P47-LE (value LessThanOrEqual 10000.5) is accepted; the version simulation matches value=10000.5 and not 10001.\n" +
            "2. Negatives (400, an Arabic message naming the JSON path, no new row by psql): a nested \"when\", the operator LessThanOrEqualz, the field employeeName, TriggerWorkflow NOPE, Then=[].");
        b.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 47c
        var c = Recorder("47c", "A version that takes effect in 10 minutes; evaluation as of now and as of 11 minutes", "central-a, a-management-approver");
        var effectiveAt = DateTime.UtcNow.AddMinutes(10);
        var (setFrom, setFromBody) = await SendAsync(author, HttpMethod.Put, $"api/hr-core/api/policies/versions/{draftB:D}/effective-from", new { effectiveFromUtc = effectiveAt });
        Assert.True(setFrom == HttpStatusCode.OK, $"effective-from -> {(int)setFrom} {setFromBody}");
        var (futurePublish, futureBody) = await SendAsync(approver, HttpMethod.Post, $"api/hr-core/api/policies/templates/{draftB:D}/publish", new { evidenceRef = "AC-POL-47c" });
        Assert.True(futurePublish == HttpStatusCode.OK, $"publish future -> {(int)futurePublish} {futureBody}");
        var asOfNow = await EvaluatedVersionAsync(author, facts, DateTime.UtcNow);
        var asOfLater = await EvaluatedVersionAsync(author, facts, DateTime.UtcNow.AddMinutes(11));
        var current = await EvaluatedVersionAsync(author, facts);
        Assert.Equal(draftA, asOfNow);
        Assert.Equal(draftA, current);
        Assert.Equal(draftB, asOfLater);
        var ledgerAsOf = await Count($"""SELECT count(*) FROM "PolicyEnforcementLedgers" WHERE "Domain" = '{Domain}' AND "EvaluatedAsOf" IS NOT NULL;""");

        var retroDraft = await OpenDraftAsync(author);
        var (retro, retroBody) = await SendAsync(author, HttpMethod.Put, $"api/hr-core/api/policies/versions/{retroDraft:D}/effective-from", new { effectiveFromUtc = DateTime.UtcNow.AddDays(-1) });
        Assert.Equal(HttpStatusCode.BadRequest, retro);
        Assert.Contains("النفاذ بأثر رجعي غير مسموح", retroBody.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{retroDraft:D}/discard")).Status);
        c.WriteScenarioDbChecks(new { draftB, effectiveAt, asOfNow, asOfLater, current, ledgerAsOf, retroactive = (int)retro });
        c.WriteScenarioSteps(
            "1. The 47b draft gets EffectiveFrom = now + 10 min and is published by the approver.\n" +
            "2. evaluate as of now (and with no as-of) returns the 47a version; as of now + 11 min returns the new one.\n" +
            "3. Negative: EffectiveFrom = now - 1 day on a new draft -> 400 «النفاذ بأثر رجعي غير مسموح» (the draft is then discarded).");
        c.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 47d
        var d = Recorder("47d", "Rollback to v2: a new version with v2's rules, linked to v2, published by two persons", "central-a, a-management-approver");
        var (noReason, _) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/templates/{Template:D}/rollback-drafts", new { targetVersionId = V2, reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        var (rollback, rollbackBody) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/templates/{Template:D}/rollback-drafts",
            new { targetVersionId = V2, reason = "AC-POL-47d: العودة إلى الإصدار الثاني" });
        Assert.True(rollback == HttpStatusCode.OK, $"rollback draft -> {(int)rollback} {rollbackBody}");
        var draftD = rollbackBody.GetProperty("id").GetGuid();
        var (selfRollback, _) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/templates/{draftD:D}/publish", new { evidenceRef = "AC-POL-47d" });
        Assert.Equal(HttpStatusCode.Forbidden, selfRollback);
        var (rolledBack, rolledBackBody) = await SendAsync(approver, HttpMethod.Post, $"api/hr-core/api/policies/templates/{draftD:D}/publish", new { evidenceRef = "AC-POL-47d" });
        Assert.True(rolledBack == HttpStatusCode.OK, $"publish rollback -> {(int)rolledBack} {rolledBackBody}");
        Assert.Equal(v2Print, await RulesPrintAsync(draftD));
        var link = (await Sql($"""SELECT "Kind" || '|' || "RolledBackFromVersionId" || '|' || "MakerUserId" || '|' || "CheckerUserId" FROM "PolicyPublications" WHERE "PolicyTemplateVersionId" = '{draftD:D}';""")).Trim();
        Assert.Equal($"Rollback|{V2:D}|{authorId:D}|{approverId:D}", link);
        Assert.Equal(draftD, await EvaluatedVersionAsync(author, facts));
        d.WriteScenarioDbChecks(new { draftD, noReason = (int)noReason, selfRollback = (int)selfRollback, link, rules = v2Print });
        d.WriteScenarioSteps(
            "1. Negative: a rollback draft with no reason -> 400.\n" +
            "2. central-a creates a rollback draft to v2 with a reason; central-a cannot publish it (403); a-management-approver publishes it.\n" +
            "3. The new version's rules print equals v2's; the publication is Rollback, RolledBackFromVersionId = v2, maker central-a, checker the approver; it is the effective version.");
        d.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 47e and 47f
        var e = Recorder("47e", "Published versions unchanged across two restarts of hr-core-web; R-REC-HIRE out of the effective version; the API and SQL refused", "system (restart), central-a");
        var f = Recorder("47f", "The seed change sets recorded once with their outcomes; the version count steady after the second restart", "system (restart)");
        var before = await FingerprintAsync();
        var applicationsBefore = (await Sql("""SELECT string_agg("ChangeSetId" || ':' || "Outcome", ',' ORDER BY "ChangeSetId", "PolicyTemplateId") FROM "PolicySeedApplications";""")).Trim();
        var versionsBefore = await Count("""SELECT count(*) FROM "PolicyTemplateVersions";""");
        await RestartHrCoreAsync();
        var versionsAfterFirst = await Count("""SELECT count(*) FROM "PolicyTemplateVersions";""");
        await RestartHrCoreAsync();
        var after = await FingerprintAsync();
        var versionsAfterSecond = await Count("""SELECT count(*) FROM "PolicyTemplateVersions";""");
        var applicationsAfter = (await Sql("""SELECT string_agg("ChangeSetId" || ':' || "Outcome", ',' ORDER BY "ChangeSetId", "PolicyTemplateId") FROM "PolicySeedApplications";""")).Trim();

        var beforeMap = ParseFingerprint(before);
        var afterMap = ParseFingerprint(after);
        var changed = beforeMap.Where(kv => !afterMap.TryGetValue(kv.Key, out var hash) || hash != kv.Value).Select(kv => kv.Key).ToList();
        Assert.Empty(changed);
        Assert.Equal(versionsBefore, versionsAfterSecond);
        Assert.Equal(versionsBefore, versionsAfterFirst);
        Assert.Equal(applicationsBefore, applicationsAfter);

        var effectiveRecruitment = """
            (SELECT v."Id" FROM "PolicyTemplateVersions" v JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
             WHERE t."Domain" = 'Core.Recruitment' AND t."Scope" = 'National' AND v."Status" = 'Published'
               AND (v."EffectiveFrom" IS NULL OR v."EffectiveFrom" <= now()) AND (v."EffectiveTo" IS NULL OR v."EffectiveTo" > now())
             ORDER BY v."VersionNumber" DESC LIMIT 1)
            """;
        var hireInEffective = await Count($"""SELECT count(*) FROM "PolicyRules" WHERE "RuleKey" = 'R-REC-HIRE' AND "PolicyTemplateVersionId" = {effectiveRecruitment};""");
        var hireElsewhere = await Count("""SELECT count(*) FROM "PolicyRules" WHERE "RuleKey" = 'R-REC-HIRE';""");
        Assert.Equal(0, hireInEffective);

        var publishedRule = (await Sql($"""SELECT "Id" FROM "PolicyRules" WHERE "PolicyTemplateVersionId" = '{V2:D}' ORDER BY "RuleKey" LIMIT 1;""")).Trim();
        var (apiAdd, _) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{V2:D}/rules", Rule("ACC-P47-LATE", CategoryWhen));
        Assert.Equal(HttpStatusCode.Conflict, apiAdd);
        var postgres = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("postgresql");
        var (sqlExit, sqlError) = await AcceptanceMessagingHelper.RunProcessAsync(
            "docker", "exec", postgres, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hrm_ci", "-d", "hr_core_db", "-t", "-A", "-c",
            $"""UPDATE "PolicyRules" SET "WhenJson" = '{"{}"}' WHERE "Id" = '{publishedRule}';""");
        Assert.NotEqual(0, sqlExit);
        Assert.Contains("PLAN-47 policy immutability", sqlError);
        Assert.Equal(before, await FingerprintAsync());

        e.WriteScenarioDbChecks(new { publishedVersions = beforeMap.Count, changedVersions = changed, versionsBefore, versionsAfterFirst, versionsAfterSecond, hireInEffective, hireRowsAnywhere = hireElsewhere, apiAdd = (int)apiAdd, sqlRefused = sqlError.Length > 0 });
        e.WriteScenarioSteps(
            "1. Inside the test, before the restarts: the fingerprint (SHA-256 of each published version's RuleKey, WhenJson, ThenJson, Priority).\n" +
            "2. docker restart hr-core-web twice, each to /health/ready: every published version keeps its hash; no version is added.\n" +
            "3. R-REC-HIRE is not in the effective Core.Recruitment version (a fresh stack never had it: the old version that kept it is proved by Hcm025RecruitmentHireRetiredTests).\n" +
            "4. Negatives: AddRule to the published v2 through the API -> 409; psql UPDATE \"PolicyRules\" SET \"WhenJson\"='{}' on a published rule -> the trigger's error, and the fingerprint is unchanged.");
        e.WriteResult("Pass");

        Assert.Contains($"{PolicySeedChangeSetsId.Baseline}:", applicationsAfter);
        Assert.Contains($"{PolicySeedChangeSetsId.Retire}:", applicationsAfter);
        f.WriteScenarioDbChecks(new { applications = applicationsAfter, versionsBefore, versionsAfterSecond, tamperedImage = "not built (optional, P-6): the changed digest failing the boot is proved by Pol047SeedApplierPostgresTests on PostgreSQL" });
        f.WriteScenarioSteps(
            "1. PolicySeedApplications holds POL-2026-10-30-BASELINE and POL-2026-10-30-RETIRE-R-REC-HIRE with their outcomes (Published where they added rules, NoOp elsewhere).\n" +
            "2. After the second restart the records and the version count are the same.\n" +
            "3. The tampered-digest boot failure is proved on PostgreSQL by Pol047SeedApplierPostgresTests (no tampered image is built here).");
        f.WriteResult("Pass");
    }

    /// <summary>The change set ids (the acceptance project does not reference HR.Core.Infrastructure).</summary>
    private static class PolicySeedChangeSetsId
    {
        public const string Baseline = "POL-2026-10-30-BASELINE";
        public const string Retire = "POL-2026-10-30-RETIRE-R-REC-HIRE";
    }
}
