using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-TEN-48 (PLAN-48) on the prod-like stack: national settings visible to every agency, the agency ceiling, and sound numbering
/// in every tenant. One ordered journey, one evidence folder per scenario:
/// (a) agency A's numbers carry their prefix (previews, and the numbers the prod-like journey issued) and /CodePolicies shows the
/// inherited national templates; (b) the national tenant (platform-central) and agency A number positions per unit and grade, a
/// shared derived cost centre, and requisitions per budget year; (c) a kind with no policy is refused with 422 and writes nothing;
/// (d) agency B, with no recruitment override, reads and applies the national rules; (e) an agency's stricter rule publishes and
/// works, a weaker one is refused when added and at publish, and an old layer is bounded at evaluation with the explanation;
/// (f) no national draft from an agency (403), and NationalSharedWriteGuard refuses a write to a national template; (g) the
/// national R-PRIV-CODE reaches agency A, whose privileged tags count apart (VIP) from its ordinary ones (AH).
/// Row security is not enforced on this stack (superuser), so the national read under forced row security is proved by
/// Ten048NationalRlsIntegrationTests.
/// </summary>
[RequiredAcceptance]
public class AcTen48NationalScopeTests
{
    private const string Ac = "AC-TEN-48";
    private static readonly int Year = DateTime.UtcNow.Year;
    private static readonly Guid TenantA = AcceptanceSeedIds.AgencyAId;
    private static readonly Guid TenantB = AcceptanceSeedIds.AgencyBId;
    private static readonly Guid TenantC = AcceptanceSeedIds.AgencyCId;
    private static readonly Guid National = NationalTenant.Id;
    private const string ProblemBase = "https://hrm.local/problems/";

    private static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    private static Task<string> Sql(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db", sql);

    private static async Task<long> Count(string sql) => long.Parse((await Sql(sql)).Trim(), CultureInfo.InvariantCulture);

    private static async Task<string[]> Lines(string sql) =>
        (await Sql(sql)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<HttpClient> ClientAsync(string personaId)
    {
        var persona = AcceptanceHarness.RequirePersona(personaId);
        var (_, correlation) = await AcceptanceHarness.LoginAsync(persona);
        return await AcceptanceHarness.CreatePolicyLifecycleClientAsync(persona, correlation);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> SendAsync(HttpClient http, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement json = default;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            json = JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, json, text);
    }

    /// <summary>A preview in the caller's tenant (it consumes no number). The kind goes as its number, as the API binds it.</summary>
    private static Task<(HttpStatusCode Status, JsonElement Body, string Text)> PreviewAsync(HttpClient http, CodeEntityKind kind, Dictionary<string, string>? facts = null) =>
        SendAsync(http, HttpMethod.Post, "api/hr-core/api/codes/preview",
            new CodeGenerationRequest { EntityKind = kind, Facts = facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Preview = true });

    private static async Task<string> PreviewCodeAsync(HttpClient http, CodeEntityKind kind, Dictionary<string, string>? facts = null)
    {
        var (status, body, text) = await PreviewAsync(http, kind, facts);
        Assert.True(status == HttpStatusCode.OK, $"preview {kind} -> {(int)status} {text}");
        return body.GetProperty("code").GetString()!;
    }

    private static async Task<JsonElement> EvaluateAsync(HttpClient http, string domain, string subjectType, string action, Dictionary<string, string> facts)
    {
        var (status, body, text) = await SendAsync(http, HttpMethod.Post, "api/hr-core/api/policies/evaluate",
            new { domain, subjectType, action, facts });
        Assert.True(status == HttpStatusCode.OK, $"evaluate {domain} -> {(int)status} {text}");
        return body;
    }

    private static IEnumerable<string> Strings(JsonElement body, string property) =>
        body.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetString() ?? string.Empty)
            : [];

    private static IEnumerable<(string Effect, JsonElement Params)> Effects(JsonElement decision) =>
        decision.GetProperty("effectDetails").EnumerateArray()
            .Select(e => (e.GetProperty("effect").GetString()!, e.TryGetProperty("params", out var p) ? p : default));

    private static string? Param(JsonElement parameters, string name) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static object Rule(string key, string when, string then, int priority) =>
        new { ruleKey = key, name = "AC-TEN-48 " + key, priority, whenJson = when, thenJson = then };

    private static async Task DiscardOpenDraftsAsync(HttpClient author, Guid templateId)
    {
        foreach (var draft in await Lines($"""SELECT "Id" FROM "PolicyTemplateVersions" WHERE "PolicyTemplateId" = '{templateId:D}' AND "Status" = 'Draft';"""))
        {
            var (status, _, text) = await SendAsync(author, HttpMethod.Post, $"api/hr-core/api/policies/versions/{draft}/discard");
            Assert.True(status == HttpStatusCode.OK, $"discard {draft} -> {(int)status} {text}");
        }
    }

    [SkippableFact]
    public async Task Ten48_national_scope_ceiling_and_numbering()
    {
        var a = Recorder("48a", "Agency A's numbers carry their prefix; /CodePolicies shows the national templates as inherited", "central-a");
        AcceptanceGate.RequireEnabled(a);
        using var central = await ClientAsync("central-a");
        using var aApprover = await ClientAsync("a-management-approver");
        using var platform = await ClientAsync("platform-central");
        using var outsiderB = await ClientAsync("outsider-b");
        using var bApprover = await ClientAsync("b-approver");
        using var cHr = await ClientAsync("c-hr");
        var nextYear = Year + 1;
        var run = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // ---------------------------------------------------------------------------------------------- 48a
        var reqPreview = await PreviewCodeAsync(central, CodeEntityKind.RecruitmentRequisition, new(StringComparer.OrdinalIgnoreCase) { ["BudgetYear"] = nextYear.ToString(CultureInfo.InvariantCulture) });
        var offPreview = await PreviewCodeAsync(central, CodeEntityKind.OfferLetter);
        var citPreview = await PreviewCodeAsync(central, CodeEntityKind.CitizenReference);
        Assert.Matches($@"^REQ-{nextYear}-\d{{5}}$", reqPreview);
        Assert.Matches($@"^OFF-{Year}-\d{{5}}$", offPreview);
        Assert.Matches($@"^CIT-{Year}-\d{{6}}$", citPreview);

        // The numbers the recruitment flows issued on this stack (the prod-like journey runs first): agency A's requisitions and
        // offers, and the citizens' references, which are one national sequence (D-P0-1).
        var issuedA = await Lines($"""
            SELECT "EntityKind" || '|' || "Code" FROM "CodeIssuances"
            WHERE ("TenantId" = '{TenantA:D}' AND "EntityKind" IN ('RecruitmentRequisition','OfferLetter'))
               OR ("TenantId" = '{National:D}' AND "EntityKind" = 'CitizenReference')
            ORDER BY 1;
            """);
        AcceptanceGate.RequireData(a,
            issuedA.Any(l => l.StartsWith("RecruitmentRequisition|", StringComparison.Ordinal)) && issuedA.Any(l => l.StartsWith("OfferLetter|", StringComparison.Ordinal)),
            "لم تصدر رحلة التوظيف أرقام طلب شاغر وعرض في الجهة A على هذا المكدس",
            "No requisition and offer numbers were issued in agency A on this stack: the prod-like journey runs before this class.");
        foreach (var line in issuedA)
        {
            var (kind, code) = (line[..line.IndexOf('|')], line[(line.IndexOf('|') + 1)..]);
            Assert.Matches(kind switch
            {
                "RecruitmentRequisition" => @"^REQ-\d{4}-\d{5}$",
                "OfferLetter" => @"^OFF-\d{4}-\d{5}$",
                _ => @"^CIT-\d{4}-\d{6}$"
            }, code);
        }

        var prefixless = await Count("""SELECT count(*) FROM "CodeIssuances" WHERE "Code" LIKE '-%';""");
        Assert.Equal(0, prefixless);

        var (page, _, pageText) = await SendAsync(central, HttpMethod.Get, "ui/hr-core/CodePolicies");
        Assert.Equal(HttpStatusCode.OK, page);
        Assert.Contains("data-testid=\"badge-inherited\"", pageText, StringComparison.Ordinal);
        a.WriteScenarioDbChecks(new { reqPreview, offPreview, citPreview, issued = issuedA, prefixlessIssuances = prefixless, codePoliciesInherited = true });
        a.WriteScenarioSteps(
            "1. central-a previews a requisition for the next budget year, an offer and a citizen reference: REQ-<budget year>-#####, OFF-YYYY-#####, CIT-YYYY-######.\n" +
            "2. By psql: every requisition and offer number of agency A and every citizen reference issued on this stack has its prefix; no issuance starts with '-'.\n" +
            "3. GET /ui/hr-core/CodePolicies as central-a: the national templates carry the «وطني (موروث — للقراءة)» badge.\n" +
            "4. After the run, scripts/ten048-find-prefixless-codes.ps1 checks every number column of the stack (the job's evidence).");
        a.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 48b
        var b = Recorder("48b", "Positions per unit and grade with one derived cost centre, and requisitions per budget year, in the national tenant and in agency A", "platform-central, central-a");
        var previews = new List<object>();
        foreach (var (label, http) in new[] { ("PLATFORM", platform), ("AGENCY-A", central) })
        {
            Dictionary<string, string> Position(int budgetYear) => new(StringComparer.OrdinalIgnoreCase)
            {
                ["OrgUnitCode"] = "T48",
                ["GradeCode"] = "G5",
                ["BudgetYear"] = budgetYear.ToString(CultureInfo.InvariantCulture)
            };
            var thisYear = await PreviewCodeAsync(http, CodeEntityKind.FundedPosition, Position(Year));
            var carried = await PreviewCodeAsync(http, CodeEntityKind.FundedPosition, Position(nextYear));
            var costCentre = await PreviewCodeAsync(http, CodeEntityKind.CostCenter, new(StringComparer.OrdinalIgnoreCase) { ["OrgUnitCode"] = "T48" });
            var reqThis = await PreviewCodeAsync(http, CodeEntityKind.RecruitmentRequisition, new(StringComparer.OrdinalIgnoreCase) { ["BudgetYear"] = Year.ToString(CultureInfo.InvariantCulture) });
            var reqNext = await PreviewCodeAsync(http, CodeEntityKind.RecruitmentRequisition, new(StringComparer.OrdinalIgnoreCase) { ["BudgetYear"] = nextYear.ToString(CultureInfo.InvariantCulture) });

            // D-TEN-6: the budget year is neither in the number nor in the counter; D-TEN-7: the cost centre is the unit's code.
            Assert.Matches(@"^T48-G5-\d{3}$", thisYear);
            Assert.Equal(thisYear, carried);
            Assert.Equal("T48", costCentre);
            // D-TEN-5: the requisition shows its budget year, and each budget year counts apart.
            Assert.Matches($@"^REQ-{Year}-\d{{5}}$", reqThis);
            Assert.Matches($@"^REQ-{nextYear}-\d{{5}}$", reqNext);
            previews.Add(new { tenant = label, thisYear, carried, costCentre, reqThis, reqNext });
        }

        var costCentreCounters = await Count("""SELECT count(*) FROM "CodeSequenceCounters" WHERE "EntityKind" = 'CostCenter';""");
        var yearlyPositionCounters = await Count("""SELECT count(*) FROM "CodeSequenceCounters" WHERE "EntityKind" = 'FundedPosition' AND "Year" <> 0;""");
        Assert.Equal(0, costCentreCounters);
        Assert.Equal(0, yearlyPositionCounters);
        b.WriteScenarioDbChecks(new { previews, costCentreCounters, yearlyPositionCounters });
        b.WriteScenarioSteps(
            "1. platform-central (PLATFORM) and central-a (AGENCY-A), each in its own tenant, preview a funded position of unit T48 and grade G5 for this and the next budget year (the same number: the counter is per unit and grade), the unit's cost centre (T48, derived), and a requisition for this and the next budget year (REQ-<year>, counted apart).\n" +
            "2. By psql: no cost-centre counter exists and no funded-position counter has a year.\n" +
            "3. Deviation (recorded): positions are not created through the establishment flow here (it needs a second approver in PLATFORM, which the persona set lacks); the issuance under parallel load, the shared cost centre issued once and the carried-over budget year are proved on PostgreSQL by Ten048FundedPositionRealCodeServiceTests and Est002's real-service test.");
        b.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 48c
        var c = Recorder("48c", "A kind with no published policy is refused with 422, and nothing is written", "central-a");
        var issuancesBefore = await Count("""SELECT count(*) FROM "CodeIssuances" WHERE "EntityKind" = 'ForensicCase';""");
        var (forensic, forensicBody, forensicText) = await PreviewAsync(central, CodeEntityKind.ForensicCase);
        Assert.True(forensic == HttpStatusCode.UnprocessableEntity, $"ForensicCase preview -> {(int)forensic} {forensicText}");
        Assert.Equal(ProblemBase + "code-policy-missing", forensicBody.GetProperty("type").GetString());
        Assert.Matches("[؀-ۿ]", forensicBody.GetProperty("title").GetString());
        var issuancesAfter = await Count("""SELECT count(*) FROM "CodeIssuances" WHERE "EntityKind" = 'ForensicCase';""");
        var forensicCounters = await Count("""SELECT count(*) FROM "CodeSequenceCounters" WHERE "EntityKind" = 'ForensicCase';""");
        Assert.Equal(issuancesBefore, issuancesAfter);
        Assert.Equal(0, forensicCounters);
        c.WriteScenarioDbChecks(new { status = (int)forensic, problem = forensicBody.GetProperty("type").GetString(), title = forensicBody.GetProperty("title").GetString(), issuancesBefore, issuancesAfter, forensicCounters });
        c.WriteScenarioSteps("1. central-a previews a ForensicCase number (no national policy is published for that kind): 422, problem code-policy-missing, an Arabic message.\n2. By psql: no ForensicCase issuance and no ForensicCase counter.");
        c.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 48d
        var d = Recorder("48d", "Agency B, with no recruitment override, reads the national rules and is decided by them", "b-approver");
        var bRecruitmentOverrides = await Count($"""SELECT count(*) FROM "PolicyTemplates" WHERE "TenantId" = '{TenantB:D}' AND "Domain" = '{PolicyDomains.CoreRecruitment}';""");
        Assert.Equal(0, bRecruitmentOverrides);
        var (snapshot, snapshotBody, snapshotText) = await SendAsync(bApprover, HttpMethod.Get, $"api/hr-core/api/policies/snapshots/{PolicyDomains.CoreRecruitment}");
        Assert.True(snapshot == HttpStatusCode.OK, $"snapshot -> {(int)snapshot} {snapshotText}");
        var snapshotRules = snapshotBody.GetProperty("rules").EnumerateArray()
            .Select(r => (Id: r.GetProperty("id").GetString(), Origin: r.TryGetProperty("origin", out var o) ? o.ToString() : null)).ToList();
        Assert.Contains(snapshotRules, r => r.Id == "R-REQ-PUBLISH-DENY");
        Assert.Contains(snapshotRules, r => r.Id == "R-REQ-SUBMIT");
        Assert.All(snapshotRules, r => Assert.Equal("National", r.Origin));
        // The gateway lets an approver read the policy pages but not call evaluate ("missing role for this module"), so agency B's
        // hr-core user asks for the decision.
        var denied = await EvaluateAsync(outsiderB, PolicyDomains.CoreRecruitment, "RecruitmentRequisition", "Publish",
            new() { ["action"] = "Publish", ["requisitionStatus"] = "Draft", ["positionVacant"] = "true" });
        Assert.False(denied.GetProperty("allowed").GetBoolean());
        Assert.Contains("R-REQ-PUBLISH-DENY", Strings(denied, "matchedRuleIds"));
        Assert.Equal("لا يمكن النشر إلا لطلب معتمد على منصب شاغر", denied.GetProperty("denyReason").GetString());
        d.WriteScenarioDbChecks(new { bRecruitmentOverrides, rules = snapshotRules.Select(r => r.Id), decision = denied.ToString() });
        d.WriteScenarioSteps("1. By psql: agency B has no Core.Recruitment template.\n2. b-approver reads GET api/policies/snapshots/Core.Recruitment: the national rules, every one of origin National.\n3. outsider-b (agency B's hr-core user; the gateway does not route an approver to evaluate) evaluates a publish of an unapproved requisition: denied by the national R-REQ-PUBLISH-DENY, with its reason.");
        d.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 48e
        var e = Recorder("48e", "A stricter agency rule publishes and works; a weaker one is refused when added and at publish; an old layer is bounded at evaluation", "central-a, a-management-approver, outsider-b, b-approver, c-hr");
        // 1. Agency A adds a Deny to its own override of Analytics.ETL (created by the request when it has none).
        var etlKey = $"ACC-T48-DENY-{run}";
        var (etlDraft, etlDraftBody, etlDraftText) = await SendAsync(central, HttpMethod.Post, "api/hr-core/api/policies/drafts",
            new { domain = PolicyDomains.AnalyticsEtl, subjectType = "EtlJob", name = "AC-TEN-48 agency override" });
        Assert.True(etlDraft == HttpStatusCode.OK, $"agency draft -> {(int)etlDraft} {etlDraftText}");
        var etlVersion = etlDraftBody.GetProperty("id").GetGuid();
        var (etlRule, _, etlRuleText) = await SendAsync(central, HttpMethod.Post, $"api/hr-core/api/policies/versions/{etlVersion:D}/rules",
            Rule(etlKey, """{"all":[{"field":"environment","operator":"Equals","value":"TEN48-SANDBOX"}]}""", """[{"effect":"Deny","params":{"reason":"AC-TEN-48: بيئة اختبار"}}]""", 5));
        Assert.True(etlRule == HttpStatusCode.OK, $"agency Deny -> {(int)etlRule} {etlRuleText}");
        var (etlPublish, _, etlPublishText) = await SendAsync(aApprover, HttpMethod.Post, $"api/hr-core/api/policies/templates/{etlVersion:D}/publish", new { evidenceRef = "AC-TEN-48e" });
        Assert.True(etlPublish == HttpStatusCode.OK, $"publish agency Deny -> {(int)etlPublish} {etlPublishText}");
        var etlScope = (await Sql($"""SELECT t."TenantId" || '|' || t."Scope" FROM "PolicyTemplateVersions" v JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId" WHERE v."Id" = '{etlVersion:D}';""")).Trim();
        Assert.Equal($"{TenantA:D}|TenantOverride", etlScope);
        var sandbox = await EvaluateAsync(central, PolicyDomains.AnalyticsEtl, "EtlJob", "Run", new() { ["environment"] = "TEN48-SANDBOX", ["action"] = "Run" });
        Assert.False(sandbox.GetProperty("allowed").GetBoolean());
        Assert.Contains(etlKey, Strings(sandbox, "matchedRuleIds"));
        var production = await EvaluateAsync(central, PolicyDomains.AnalyticsEtl, "EtlJob", "Run", new() { ["environment"] = "Production", ["action"] = "Run" });
        Assert.True(production.GetProperty("allowed").GetBoolean());
        Assert.Contains("R-ETL", Strings(production, "matchedRuleIds"));
        Assert.Contains(Effects(production), x => x.Effect == "RequireApproval");

        // 2. Agency B: its custody v2 is a layer written before the ceiling. Weaker rules are refused when added; a copy of the old
        // layer is refused at publish, naming its rules; the draft is discarded and B's published versions do not change.
        var bTemplate = Guid.Parse((await Sql($"""SELECT "Id" FROM "PolicyTemplates" WHERE "TenantId" = '{TenantB:D}' AND "Domain" = '{PolicyDomains.AssetsCustody}' AND "Scope" = 'TenantOverride';""")).Trim());
        var bPublishedBefore = await Count($"""SELECT count(*) FROM "PolicyTemplateVersions" WHERE "PolicyTemplateId" = '{bTemplate:D}' AND "Status" = 'Published';""");
        await DiscardOpenDraftsAsync(outsiderB, bTemplate);
        var (bDraft, bDraftBody, bDraftText) = await SendAsync(outsiderB, HttpMethod.Post, $"api/hr-core/api/policies/templates/{bTemplate:D}/drafts", new { });
        Assert.True(bDraft == HttpStatusCode.OK, $"B draft -> {(int)bDraft} {bDraftText}");
        var bVersion = bDraftBody.GetProperty("id").GetGuid();
        var refusedAtAdd = new List<object>();
        foreach (var (key, when, then, code) in new[]
                 {
                     ("ACC-T48-UNPRIV", """{"all":[{"field":"action","operator":"Equals","value":"Classify"}]}""", """[{"effect":"SetField","params":{"field":"isPrivileged","value":"false"}}]""", PolicyCeiling.SetFieldCode),
                     ("ACC-T48-FIRST", """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""", """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-LOSS-APPROVAL"}}]""", PolicyCeiling.TriggerWorkflowCode)
                 })
        {
            var (status, body, text) = await SendAsync(outsiderB, HttpMethod.Post, $"api/hr-core/api/policies/versions/{bVersion:D}/rules", Rule(key, when, then, 1));
            Assert.True(status == HttpStatusCode.BadRequest, $"{key} -> {(int)status} {text}");
            Assert.Contains(body.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == code);
            refusedAtAdd.Add(new { key, status = (int)status, code });
        }

        var (bPublish, bPublishBody, bPublishText) = await SendAsync(bApprover, HttpMethod.Post, $"api/hr-core/api/policies/templates/{bVersion:D}/publish", new { evidenceRef = "AC-TEN-48e-old" });
        Assert.True(bPublish == HttpStatusCode.BadRequest, $"publish B's old layer -> {(int)bPublish} {bPublishText}");
        var publishIssues = bPublishBody.GetProperty("issues").EnumerateArray()
            .Select(i => (Code: i.GetProperty("code").GetString()!, Message: i.GetProperty("messageAr").GetString()!)).ToList();
        Assert.Contains(publishIssues, i => i.Code == PolicyCeiling.TriggerWorkflowCode && i.Message.Contains("ACC-R-PRIV-WORKFLOW", StringComparison.Ordinal));
        Assert.Contains(publishIssues, i => i.Code == PolicyCeiling.TriggerWorkflowCode && i.Message.Contains("ACC-R-OLD-FIRST-WF", StringComparison.Ordinal));
        Assert.Contains(publishIssues, i => i.Code == PolicyCeiling.SetFieldCode && i.Message.Contains("ACC-R-OLD-UNPRIV", StringComparison.Ordinal));
        Assert.Equal("Draft", (await Sql($"""SELECT "Status" FROM "PolicyTemplateVersions" WHERE "Id" = '{bVersion:D}';""")).Trim());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(outsiderB, HttpMethod.Post, $"api/hr-core/api/policies/versions/{bVersion:D}/discard")).Status);
        Assert.Equal(bPublishedBefore, await Count($"""SELECT count(*) FROM "PolicyTemplateVersions" WHERE "PolicyTemplateId" = '{bTemplate:D}' AND "Status" = 'Published';"""));

        // 3. Agency C's old layer, at evaluation: the national workflow and the national classification stand, and the decision says
        // which agency rules the ceiling dropped.
        var assign = await EvaluateAsync(cHr, PolicyDomains.AssetsCustody, "Asset", "Assign", new() { ["isPrivileged"] = "true", ["value"] = "500", ["action"] = "Assign" });
        Assert.Equal("ASSET-ASSIGN-APPROVAL", Effects(assign).Where(x => x.Effect == "TriggerWorkflow").Select(x => Param(x.Params, "workflowCode")).First());
        Assert.DoesNotContain(Effects(assign), x => Param(x.Params, "workflowCode") == "ASSET-LOSS-APPROVAL");
        Assert.Contains(Strings(assign, "explanations"), s => s.Contains(PolicyCeiling.ExplanationAr, StringComparison.Ordinal) && s.Contains("ACC-R-OLD-FIRST-WF", StringComparison.Ordinal));
        var classify = await EvaluateAsync(cHr, PolicyDomains.AssetsCustody, "Asset", "Classify", new() { ["value"] = "20000", ["action"] = "Classify" });
        Assert.Equal("true", Effects(classify).Where(x => x.Effect == "SetField" && Param(x.Params, "field") == "isPrivileged").Select(x => Param(x.Params, "value")).Last());
        Assert.Contains(Strings(classify, "explanations"), s => s.Contains(PolicyCeiling.ExplanationAr, StringComparison.Ordinal) && s.Contains("ACC-R-OLD-UNPRIV", StringComparison.Ordinal));
        e.WriteScenarioDbChecks(new
        {
            agencyOverride = new { etlVersion, etlScope, etlKey, sandboxDenied = true, productionStillRequiresApproval = true },
            agencyB = new { bTemplate, refusedAtAdd, publishStatus = (int)bPublish, publishIssues = publishIssues.Select(i => i.Code), bPublishedBefore },
            agencyC = new { assign = assign.ToString(), classify = classify.ToString() }
        });
        e.WriteScenarioSteps(
            "1. central-a POST api/policies/drafts for Analytics.ETL (agency A's own TenantOverride, created by the request), adds a Deny for environment TEN48-SANDBOX; a-management-approver publishes it. Evaluated: TEN48-SANDBOX is denied by the agency rule; Production still gets the national R-ETL RequireApproval.\n" +
            "2. outsider-b drafts agency B's custody override (a copy of its pre-ceiling v2): adding SetField isPrivileged=false -> 400 CEILING_SETFIELD; adding a priority-1 TriggerWorkflow -> 400 CEILING_TRIGGER_WORKFLOW. b-approver publishes the copy -> 400, the issues name ACC-R-PRIV-WORKFLOW, ACC-R-OLD-FIRST-WF (CEILING_TRIGGER_WORKFLOW) and ACC-R-OLD-UNPRIV (CEILING_SETFIELD); the draft stays a draft and is discarded; B's published versions are unchanged.\n" +
            "3. c-hr evaluates agency C (its pre-ceiling layer is in force): a privileged assignment gets the national ASSET-ASSIGN-APPROVAL, not the agency's ASSET-LOSS-APPROVAL; a high-value classification keeps isPrivileged=true; the explanations name the dropped agency rules with «أُهمل أثر الجهة بسقف الوطني (D-POL-4)».");
        e.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 48f
        var f = Recorder("48f", "No national draft from an agency; the guard refuses a write to a national template", "central-a");
        var nationalCustody = Guid.Parse((await Sql($"""SELECT "Id" FROM "PolicyTemplates" WHERE "TenantId" = '{National:D}' AND "Domain" = '{PolicyDomains.AssetsCustody}' AND "Scope" = 'National';""")).Trim());
        var nationalDrafts = await Count($"""SELECT count(*) FROM "PolicyTemplateVersions" WHERE "PolicyTemplateId" = '{nationalCustody:D}' AND "Status" = 'Draft';""");
        AcceptanceGate.RequireData(f, nationalDrafts == 0, "يوجد مسودة وطنية مفتوحة لعهدة الأصول", "A national custody draft is open, so a new draft would be a 409 before the guard.");
        var nationalPrint = (await Sql($"""SELECT count(*) || '|' || max("VersionNumber") FROM "PolicyTemplateVersions" WHERE "PolicyTemplateId" = '{nationalCustody:D}';""")).Trim();
        var (scopeNational, scopeNationalBody, scopeNationalText) = await SendAsync(central, HttpMethod.Post, "api/hr-core/api/policies/drafts",
            new { domain = PolicyDomains.AssetsCustody, subjectType = "Asset", name = "AC-TEN-48f", scope = "National" });
        Assert.True(scopeNational == HttpStatusCode.Forbidden, $"scope National -> {(int)scopeNational} {scopeNationalText}");
        Assert.Matches("[؀-ۿ]", scopeNationalBody.GetProperty("error").GetString());
        var (direct, directBody, directText) = await SendAsync(central, HttpMethod.Post, $"api/hr-core/api/policies/templates/{nationalCustody:D}/drafts", new { });
        Assert.True(direct == HttpStatusCode.Forbidden, $"draft of the national template -> {(int)direct} {directText}");
        Assert.Equal(ProblemBase + "national-shared-write", directBody.GetProperty("type").GetString());
        Assert.Equal(nationalPrint, (await Sql($"""SELECT count(*) || '|' || max("VersionNumber") FROM "PolicyTemplateVersions" WHERE "PolicyTemplateId" = '{nationalCustody:D}';""")).Trim());
        var agencyNationalTemplates = await Count($"""SELECT count(*) FROM "PolicyTemplates" WHERE "TenantId" = '{TenantA:D}' AND "Scope" = 'National';""");
        Assert.Equal(0, agencyNationalTemplates);
        f.WriteScenarioDbChecks(new { nationalCustody, scopeNational = (int)scopeNational, direct = (int)direct, problem = directBody.GetProperty("type").GetString(), nationalVersionsBeforeAndAfter = nationalPrint, agencyNationalTemplates });
        f.WriteScenarioSteps("1. central-a POST api/policies/drafts with scope National -> 403 (an Arabic message).\n2. central-a POST api/policies/templates/{national custody template}/drafts -> 403, problem national-shared-write (NationalSharedWriteGuard).\n3. By psql: the national template's versions are unchanged; agency A holds no template of scope National.");
        f.WriteResult("Pass");

        // ---------------------------------------------------------------------------------------------- 48g
        var g = Recorder("48g", "The national R-PRIV-CODE reaches agency A; its privileged tags count apart (VIP) from its ordinary ones (AH)", "central-a, svc-admin-logistics");
        var create = await EvaluateAsync(central, PolicyDomains.AssetsCustody, "Asset", "Create", new() { ["isPrivileged"] = "true", ["value"] = "500", ["action"] = "Create" });
        Assert.Contains("R-PRIV-CODE", Strings(create, "matchedRuleIds"));
        var policyKey = Effects(create).Where(x => x.Effect == "SelectCodePolicy").Select(x => Param(x.Params, "policyKey")).FirstOrDefault();
        Assert.Equal("ASSET-PRIVILEGED", policyKey);
        using var logistics = await AcceptanceHarness.CreateServiceClientAsync("svc-admin-logistics", ServiceScopes.HrCoreWorkflow, g, TenantA);
        async Task<string> GenerateAsync(string? key)
        {
            var (status, body, text) = await SendAsync(logistics, HttpMethod.Post, "api/internal/codes/generate",
                new CodeGenerationRequest { EntityKind = CodeEntityKind.AssetTag, PolicyKey = key, Preview = false });
            Assert.True(status == HttpStatusCode.OK, $"AssetTag {key ?? "default"} -> {(int)status} {text}");
            return body.GetProperty("code").GetString()!;
        }

        var ordinary = await GenerateAsync(null);
        var privileged = await GenerateAsync(policyKey);
        Assert.Matches($@"^AH-{Year}-\d{{6}}$", ordinary);
        Assert.Matches($@"^VIP-{Year}-\d{{6}}$", privileged);
        var keys = await Lines($"""SELECT "SequenceScope" FROM "CodeIssuances" WHERE "TenantId" = '{TenantA:D}' AND "Code" IN ('{ordinary}','{privileged}') ORDER BY 1;""");
        Assert.Equal(2, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(keys, k => k.Contains("|ASSET-PRIVILEGED|", StringComparison.Ordinal));
        g.WriteScenarioDbChecks(new { matched = Strings(create, "matchedRuleIds"), policyKey, ordinary, privileged, scopes = keys });
        g.WriteScenarioSteps("1. central-a evaluates the creation of a privileged asset in agency A: the national R-PRIV-CODE matches and selects ASSET-PRIVILEGED (agencies did not see it before PLAN-48).\n2. svc-admin-logistics generates an ordinary and a privileged tag in agency A: AH-YYYY-###### and VIP-YYYY-######.\n3. By psql: the two issuances have different sequence scopes, the privileged one keyed by ASSET-PRIVILEGED.");
        g.WriteResult("Pass");
    }
}
