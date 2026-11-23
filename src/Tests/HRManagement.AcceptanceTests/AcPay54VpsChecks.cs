using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-54 (P8, P-16, P-17) on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>) after the deploy, with the AC-OPS-36 helpers, as three
/// people: <c>e2e-payroll-processor</c> edits, <c>e2e-payroll-approver</c> approves, and the attestor attests (<c>hradmin</c>, or the
/// user named in <c>PAY054_VPS_ATTESTOR</c>). The password is read into memory only; evidence goes under <c>artifacts/plan54/vps</c>
/// and is never committed.
/// <para>
/// Nothing these checks make can govern a run. The version they import takes an effective date inside a posted month of the demo,
/// so D-PAY-13 refuses its publication: it stays approved, attested and unpublished under the check's own code
/// (<c>LAW_50_2004_CHK54</c>). The manual allowance they enter is rejected. No published version, no row of the decision register
/// and no pay datum of the demo changes: the versions and the register are compared before and after by the checksums of P0's
/// picture (and with the picture itself when <c>PAY054_VPS_VERSIONS_MD5</c>, <c>PAY054_VPS_DECISIONS</c> and
/// <c>PAY054_VPS_INPUTS</c> carry its values).
/// </para>
/// </summary>
public partial class AcOps36VpsBootTests
{
    private const string Pay54Processor = "e2e-payroll-processor";
    private const string Pay54Approver = "e2e-payroll-approver";
    /// <summary>The marker of everything these checks leave on the server: the code of their version.</summary>
    private const string Pay54Code = "LAW_50_2004_CHK54";
    private const string Pay54Api = "/ui/payroll/api/payroll/frameworks";
    private const string Pay54Pages = "/ui/payroll/PayrollConfigurations";
    private const string Pay54Allowances = "/ui/payroll/AllowanceRecords";
    private const string Pay54AllowanceReason = "بدل مهمة استثنائية خارج المحافظة وفق قرار التكليف";
    private const string Pay54RejectNote = "لا يوجد قرار تكليف مرفق يسند البدل";
    private const string Pay54AllowanceAmount = "54054.54";

    private static string Pay54Attestor => Environment.GetEnvironmentVariable("PAY054_VPS_ATTESTOR") is { Length: > 0 } user ? user : "hradmin";

    private static EvidenceRecorder StartPay54(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"pay54-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan54", "vps"))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "demo users on the server (password read into memory only)");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    /// <summary>P0's checksum of the versions, over every version that is not the check's own (the same expression as the picture).</summary>
    private static string Pay54Versions() =>
        Sql("payroll_db",
            "select count(*) || '|' || coalesce(md5(string_agg(\"Id\"::text||'|'||\"Code\"||'|'||\"VersionNumber\"||'|'||\"PublicationStatus\"||'|'||\"IsActive\"||'|'||\"EffectiveFrom\"||'|'||" +
            "coalesce(\"EffectiveTo\"::text,'')||'|'||coalesce(\"ContentHash\",''), ';' order by \"Id\")), '') " +
            $"from \"PayrollConfigurations\" where \"Code\" <> '{Pay54Code}'").Single();

    /// <summary>P0's checksum of the decision register (the same expression as the picture).</summary>
    private static string Pay54Decisions() =>
        Sql("payroll_db",
            "select count(*) || '|' || coalesce(md5(string_agg(\"Id\"::text||'|'||\"DecisionId\"||'|'||\"ApprovalStatus\"||'|'||\"OfficialSource\"||'|'||coalesce(\"Notes\",''), ';' order by \"Id\")), '') " +
            "from \"LegalDecisionRecords\"").Single();

    /// <summary>The register's rows with the time each was last written: an import that repeats the register writes none of them.</summary>
    private static string Pay54DecisionsWritten() =>
        Sql("payroll_db", "select coalesce(md5(string_agg(\"Id\"::text||'|'||\"UpdatedAt\"::text, ';' order by \"Id\")), '') from \"LegalDecisionRecords\"").Single();

    /// <summary>The rows a framework command may write, counted.</summary>
    private static string Pay54Rows() =>
        Sql("payroll_db",
            "select (select count(*) from \"PayrollConfigurations\") || '|' || (select count(*) from \"TaxBrackets\") || '|' || " +
            "(select count(*) from \"StatutoryDeductionRules\") || '|' || (select count(*) from \"OfficialHolidays\") || '|' || " +
            "(select count(*) from \"DisbursementPriorityRules\") || '|' || (select count(*) from \"PayrollConfigurationFieldSources\") || '|' || " +
            "(select count(*) from \"PayrollConfigurationEditors\") || '|' || (select count(*) from \"LegalDecisionRecords\") || '|' || " +
            "(select count(*) from \"LegalAttestations\") || '|' || (select count(*) from \"PayrollConfigChangeNotices\")").Single();

    /// <summary>A version's place in the cycle: status, active, fingerprint, approver, publisher, dates, active attestations.</summary>
    private static string Pay54State(string id) =>
        Sql("payroll_db",
            "select c.\"PublicationStatus\" || '~' || c.\"IsActive\"::text || '~' || coalesce(c.\"ContentHash\",'') || '~' || coalesce(c.\"ApprovedBy\"::text,'') || '~' || " +
            "coalesce(c.\"PublishedBy\"::text,'') || '~' || to_char(c.\"EffectiveFrom\", 'YYYY-MM-DD') || '~' || coalesce(to_char(c.\"EffectiveTo\", 'YYYY-MM-DD'),'') || '~' || " +
            "(select count(*) from \"LegalAttestations\" a where a.\"PayrollConfigurationId\" = c.\"Id\" and a.\"Status\" = 'Active')::text " +
            $"from \"PayrollConfigurations\" c where c.\"Id\" = '{id}'").SingleOrDefault() ?? "";

    /// <summary>The demo's pay inputs that are not a person's (the rows the migration found): how many, and how many are not approved.</summary>
    private static string Pay54Inputs() =>
        Sql("payroll_db",
            "select (select count(*) filter (where \"Origin\" = 'System') || ':' || count(*) filter (where \"Origin\" = 'System' and \"ApprovalStatus\" <> 'Approved') from \"AllowanceRecords\") || '|' || " +
            "(select count(*) filter (where \"Origin\" = 'System') || ':' || count(*) filter (where \"Origin\" = 'System' and \"ApprovalStatus\" <> 'Approved') from \"DeductionRecords\") || '|' || " +
            "(select count(*) filter (where \"Origin\" = 'System') || ':' || count(*) filter (where \"Origin\" = 'System' and \"ApprovalStatus\" <> 'Approved') from \"PayrollAdjustments\")").Single();

    /// <summary>A signed-in session that sends the antiforgery token of the frameworks API (the cookie stays in the client).</summary>
    private static async Task<HttpClient> Pay54ApiAsync(string user, string password)
    {
        var (client, _) = await SignInAsync(user, password, "/ui/payroll/");
        using var issued = await client.GetAsync($"https://{AppDomain}{Pay54Api}/antiforgery");
        var body = await issued.Content.ReadAsStringAsync();
        Assert.True(issued.IsSuccessStatusCode, $"The antiforgery token of the frameworks API answered {(int)issued.StatusCode} to {user}.");
        using var token = JsonDocument.Parse(body);
        client.DefaultRequestHeaders.Add(token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    private static async Task<(int Status, string Error, string Body)> Pay54PostAsync(HttpClient client, string path, HttpContent? content = null)
    {
        using var response = await client.PostAsync($"https://{AppDomain}{path}",
            content ?? new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        var error = "";
        if (body.TrimStart().StartsWith('{'))
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String)
                error = value.GetString() ?? "";
        }

        return ((int)response.StatusCode, error, body);
    }

    private static string Pay54Pack() =>
        File.ReadAllText(Path.Combine(AcceptanceHarness.RepoRoot, "docs", "syr", "payroll-framework-packs", "law50-interim.json"));

    private static StringContent Pay54Import(string packJson, string effectiveFrom) =>
        new("{\"pack\":" + packJson + ",\"code\":\"" + Pay54Code + "\",\"effectiveFrom\":\"" + effectiveFrom + "\"}", Encoding.UTF8, "application/json");

    [SkippableFact]
    public void AC_PAY_54_vps_the_migration_ran_and_every_existing_version_and_input_kept_its_value()
    {
        var recorder = StartPay54("migration",
            "Demo server: PAY054 is recorded; the three new tables have forced row security; every version the demo had is a v1 version with its fingerprint, and every input it had is approved and of origin System; the counts and checksums of P0's picture hold");
        var migration = Sql("payroll_db", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '%PAY054_LegalFrameworkPacks'").Single();
        var rowSecurity = Sql("payroll_db",
            "select string_agg(relname || ':' || relrowsecurity::text || ':' || relforcerowsecurity::text, ',' order by relname) from pg_class " +
            "where relname in ('StatutoryDeductionRules', 'PayrollConfigurationFieldSources', 'PayrollConfigurationEditors')").Single();
        var versions = Sql("payroll_db",
            "select count(*) || '|' || count(*) filter (where \"HashSchemaVersion\" = 1 and length(coalesce(\"ContentHash\", '')) = 64) " +
            $"from \"PayrollConfigurations\" where \"Code\" <> '{Pay54Code}'").Single();
        var versionsChecksum = Pay54Versions();
        var decisions = Pay54Decisions();
        var inputs = Pay54Inputs();
        var generalRows = Sql("payroll_db",
            "select (select count(*) from \"OfficialHolidays\" where \"PayrollConfigurationId\" is null) || '|' || " +
            "(select count(*) from \"DisbursementPriorityRules\" where \"IsActive\") || '|' || (select count(*) from \"DeductionTypes\" where \"IsStatutory\")").Single();
        var runs = Sql("payroll_db",
            "select coalesce(string_agg(m || ':' || s || ':' || n || ':' || pinned, ',' order by m, s), '') from (select to_char(\"PeriodStart\", 'YYYY-MM') m, \"Status\" s, count(*) n, " +
            "count(\"PayrollConfigurationId\") pinned from \"PayrollRuns\" group by 1, 2) r").Single();
        var pictureVersions = Environment.GetEnvironmentVariable("PAY054_VPS_VERSIONS_MD5");
        var pictureDecisions = Environment.GetEnvironmentVariable("PAY054_VPS_DECISIONS");
        var pictureInputs = Environment.GetEnvironmentVariable("PAY054_VPS_INPUTS");

        recorder.WriteScenarioDbChecks(new
        {
            migration,
            rowSecurity,
            versions,
            versionsFormat = "versions that are not the check's | of them v1 with a fingerprint",
            versionsChecksum,
            decisions,
            inputs,
            inputsFormat = "allowances | deductions | adjustments, each: System rows : of them not approved",
            generalRows,
            generalRowsFormat = "general holidays | active priority rules | statutory deduction types",
            runs,
            runsFormat = "month:status:runs:pinned",
            comparedWithThePicture = pictureVersions is not null && pictureDecisions is not null && pictureInputs is not null
        });

        Assert.Equal("1", migration);
        Assert.Equal("PayrollConfigurationEditors:true:true,PayrollConfigurationFieldSources:true:true,StatutoryDeductionRules:true:true", rowSecurity);
        var counted = versions.Split('|');
        Assert.True(counted[0] != "0" && counted[0] == counted[1], $"A version of the demo is not a v1 version with its fingerprint: {versions}");
        Assert.All(inputs.Split('|'), kind => Assert.EndsWith(":0", kind, StringComparison.Ordinal));
        // No rule of a version is an active row of the general table (what the release before this one reads after a rollback).
        Assert.Equal("0", Sql("payroll_db", "select count(*) from \"DisbursementPriorityRules\" where \"IsActive\" and \"PayrollConfigurationId\" is not null").Single());
        if (pictureVersions is not null) Assert.EndsWith("|" + pictureVersions, versionsChecksum, StringComparison.Ordinal);
        if (pictureDecisions is not null) Assert.Equal(pictureDecisions, decisions);
        if (pictureInputs is not null) Assert.Equal(pictureInputs, string.Join("|", inputs.Split('|').Select(kind => kind.Split(':')[0])));
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_54_vps_three_people_take_a_version_to_attested_and_it_is_not_published_into_a_posted_month()
    {
        var recorder = StartPay54("cycle",
            "Demo server: the processor imports the real pack under the check's code from a date inside a posted month (a draft); publishing it is refused; the processor cannot approve; the approver approves and cannot attest; the attestor attests; publishing is refused for the posted month (D-PAY-13); a pack with a 120% bracket writes nothing; no version, decision or run of the demo changes");
        var posted = Sql("payroll_db",
            "select coalesce(to_char(max(\"PeriodStart\"), 'YYYY-MM-DD') || '|' || to_char(max(\"PeriodEnd\"), 'YYYY-MM-DD'), '') from \"PayrollRuns\" where \"Status\" = 2").Single().Split('|');
        AcceptanceGate.RequireData(recorder, posted.Length == 2, "لا يوجد تشغيل رواتب مرحّل على خادم العرض", "The demo has no posted payroll run: the check's version could be published.");
        var from = posted[0];
        var password = DemoPassword();
        using var processor = await Pay54ApiAsync(Pay54Processor, password);
        using var approver = await Pay54ApiAsync(Pay54Approver, password);
        using var attestor = await Pay54ApiAsync(Pay54Attestor, password);

        var versionsBefore = Pay54Versions();
        var decisionsBefore = Pay54Decisions();
        var decisionsWrittenBefore = Pay54DecisionsWritten();
        var runsBefore = Sql("payroll_db", "select count(*) || '|' || md5(coalesce(string_agg(t::text, '|' order by t.\"Id\"), '')) from \"PayrollRuns\" t").Single();

        // A malformed pack: 400, the field named, no row.
        var rowsBefore = Pay54Rows();
        var broken = JsonNode.Parse(Pay54Pack())!.AsObject();
        broken["taxBrackets"]![2]!["ratePercent"] = 120;
        var (malformed, _, malformedBody) = await Pay54PostAsync(processor, $"{Pay54Api}/import", Pay54Import(broken.ToJsonString(), from));
        var rowsAfterMalformed = Pay54Rows();

        // The real pack, under the check's code, from the first day of the demo's last posted month.
        var (imported, _, importedBody) = await Pay54PostAsync(processor, $"{Pay54Api}/import", Pay54Import(Pay54Pack(), from));
        var id = Regex.Match(importedBody, "\"frameworkId\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"").Groups[1].Value;
        Assert.True(imported == 201 && id.Length == 36, $"The import answered {imported}: {(importedBody.Length > 300 ? importedBody[..300] : importedBody)}");
        var draft = Pay54State(id);

        var (publishDraft, publishDraftError, _) = await Pay54PostAsync(approver, $"{Pay54Api}/{id}/publish");
        var afterPublishDraft = Pay54State(id);
        var (processorApprove, _, _) = await Pay54PostAsync(processor, $"{Pay54Api}/{id}/approve");
        var afterProcessorApprove = Pay54State(id);
        var (approved, approvedError, _) = await Pay54PostAsync(approver, $"{Pay54Api}/{id}/approve");
        var afterApproval = Pay54State(id);
        var (approverAttest, approverAttestError, _) = await Pay54PostAsync(approver, $"{Pay54Api}/{id}/legal-attest");
        var afterApproverAttest = Pay54State(id);
        var (attested, attestedError, _) = await Pay54PostAsync(attestor, $"{Pay54Api}/{id}/legal-attest");
        var afterAttestation = Pay54State(id);
        var (published, publishedError, _) = await Pay54PostAsync(approver, $"{Pay54Api}/{id}/publish");
        var final = Pay54State(id);
        var people = Sql("payroll_db",
            "select (select string_agg(distinct e.\"UserId\"::text, ',') from \"PayrollConfigurationEditors\" e where e.\"PayrollConfigurationId\" = c.\"Id\") || '|' || c.\"ApprovedBy\" || '|' || " +
            "(select a.\"AttestedBy\" from \"LegalAttestations\" a where a.\"PayrollConfigurationId\" = c.\"Id\" and a.\"Status\" = 'Active') " +
            $"from \"PayrollConfigurations\" c where c.\"Id\" = '{id}'").Single().Split('|');
        var rulesStoredActive = Sql("payroll_db", $"select count(*) from \"DisbursementPriorityRules\" where \"PayrollConfigurationId\" = '{id}' and \"IsActive\"").Single();

        var versionsAfter = Pay54Versions();
        var decisionsAfter = Pay54Decisions();
        var decisionsWrittenAfter = Pay54DecisionsWritten();
        var runsAfter = Sql("payroll_db", "select count(*) || '|' || md5(coalesce(string_agg(t::text, '|' order by t.\"Id\"), '')) from \"PayrollRuns\" t").Single();
        recorder.WriteScenarioDbChecks(new
        {
            attestor = Pay54Attestor,
            effectiveFrom = from,
            latestPostedPeriodEnd = posted[1],
            malformed,
            malformedNamesTheField = malformedBody.Contains("$.taxBrackets[2].ratePercent", StringComparison.Ordinal),
            noRowAfterTheMalformedPack = rowsBefore == rowsAfterMalformed,
            version = id,
            draft,
            publishDraft,
            publishDraftError,
            processorApprove,
            approved,
            approvedError,
            afterApproval,
            approverAttest,
            approverAttestError,
            attested,
            attestedError,
            afterAttestation,
            published,
            publishedError,
            final,
            stateFormat = "status~active~fingerprint~approved by~published by~effective from~effective to~active attestations",
            threeDifferentPeople = people.Length == 3 && people.Distinct().Count() == 3,
            rulesStoredActive,
            versionsUnchanged = versionsBefore == versionsAfter,
            decisionsUnchanged = decisionsBefore == decisionsAfter,
            noDecisionRowWritten = decisionsWrittenBefore == decisionsWrittenAfter,
            runsUnchanged = runsBefore == runsAfter
        });

        Assert.Equal(400, malformed);
        Assert.Contains("$.taxBrackets[2].ratePercent", malformedBody, StringComparison.Ordinal);
        Assert.Equal(rowsBefore, rowsAfterMalformed);
        Assert.StartsWith("Draft~false~~~~" + from + "~~0", draft, StringComparison.Ordinal);
        Assert.Equal(400, publishDraft);
        Assert.Contains("لا يُنشر إلا إصدار معتمد", publishDraftError, StringComparison.Ordinal);
        Assert.Equal(draft, afterPublishDraft);
        Assert.Equal(403, processorApprove);
        Assert.Equal(draft, afterProcessorApprove);
        Assert.True(approved == 200, $"The approval answered {approved}: {approvedError}");
        Assert.StartsWith("Approved~false~", afterApproval, StringComparison.Ordinal);
        Assert.Equal(400, approverAttest);
        Assert.Contains("لا يصادق قانونياً على النسخة من اعتمدها", approverAttestError, StringComparison.Ordinal);
        Assert.Equal(afterApproval, afterApproverAttest);
        Assert.True(attested == 200,
            $"The attestation by {Pay54Attestor} answered {attested}: {attestedError}. If it is 403 the user does not meet Payroll:Legal:AttestorPolicy on this server: name another in PAY054_VPS_ATTESTOR.");
        Assert.EndsWith("~1", afterAttestation, StringComparison.Ordinal);
        Assert.Equal(400, published);
        Assert.Contains("تشغيل رواتب مرحّل", publishedError, StringComparison.Ordinal);
        Assert.Contains(posted[1], publishedError, StringComparison.Ordinal);
        Assert.Equal(afterAttestation, final);                                         // approved, attested, unpublished: it governs nothing
        Assert.StartsWith("Approved~false~", final, StringComparison.Ordinal);
        Assert.True(people.Length == 3 && people.Distinct().Count() == 3, "The importer, the approver and the attestor are not three people.");
        Assert.Equal("0", rulesStoredActive);
        Assert.Equal(versionsBefore, versionsAfter);
        Assert.Equal(decisionsBefore, decisionsAfter);
        Assert.Equal(decisionsWrittenBefore, decisionsWrittenAfter);
        Assert.Equal(runsBefore, runsAfter);
        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_54_vps_a_manual_allowance_waits_its_author_cannot_approve_it_and_the_approver_rejects_it()
    {
        var recorder = StartPay54("manual-input",
            "Demo server: the processor enters an allowance for a demo employee (pending, of origin Manual); his own approval answers 403; the approver rejects it with a note; nothing is left pending and no other input changes");
        var password = DemoPassword();
        var (processor, _) = await SignInAsync(Pay54Processor, password, "/ui/payroll/");
        var (approver, _) = await SignInAsync(Pay54Approver, password, "/ui/payroll/");
        using (processor)
        using (approver)
        {
            var employee = Sql("payroll_db", "select \"EmployeeId\" from \"EmployeePayProfiles\" where \"IsActive\" order by \"EmployeeId\" limit 1").Single();
            var type = Sql("payroll_db", "select \"Id\" from \"CompensationTypes\" order by \"Code\" limit 1").SingleOrDefault();
            AcceptanceGate.RequireData(recorder, type is not null, "لا يوجد نوع بدل على خادم العرض", "The demo has no allowance type.");
            var today = HRManagement.ServiceDefaults.Localization.HrLocalTime.ToLocal(DateTime.UtcNow, HRManagement.ServiceDefaults.Localization.HrLocalTime.DefaultZone);
            var next = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
            var inputsBefore = Pay54Inputs();
            var pendingBefore = Sql("payroll_db", "select count(*) from \"AllowanceRecords\" where \"ApprovalStatus\" = 'PendingApproval'").Single();
            var mineBefore = Sql("payroll_db", $"select count(*) from \"AllowanceRecords\" where \"EmployeeId\" = '{employee}' and \"Origin\" = 'Manual' and \"Amount\" = {Pay54AllowanceAmount}").Single();

            var form = new Dictionary<string, string>
            {
                ["EmployeeId"] = employee,
                ["AllowanceTypeId"] = type!,
                ["Amount"] = Pay54AllowanceAmount,
                ["EffectiveFrom"] = next.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["EffectiveTo"] = next.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["Reason"] = Pay54AllowanceReason,
                // Not the form's: a person's input is pending and of origin Manual whatever the request says.
                ["ApprovalStatus"] = "Approved",
                ["Origin"] = "System",
                ["__RequestVerificationToken"] = await TokenAsync(processor, $"{Pay54Allowances}/Create")
            };
            var (entered, _, enteredBody) = await PostFormAsync(processor, $"{Pay54Allowances}/Create", form);
            var allowance = Sql("payroll_db",
                $"select \"Id\" from \"AllowanceRecords\" where \"EmployeeId\" = '{employee}' and \"Origin\" = 'Manual' and \"Amount\" = {Pay54AllowanceAmount} order by \"CreatedAt\" desc limit 1").SingleOrDefault();
            Assert.True(allowance is not null && mineBefore != Sql("payroll_db",
                    $"select count(*) from \"AllowanceRecords\" where \"EmployeeId\" = '{employee}' and \"Origin\" = 'Manual' and \"Amount\" = {Pay54AllowanceAmount}").Single(),
                $"The allowance was not entered ({entered}): {(enteredBody.Length > 300 ? WebUtility.HtmlDecode(enteredBody)[..300] : enteredBody)}");
            string Row() => Sql("payroll_db",
                "select \"ApprovalStatus\" || '|' || \"Origin\" || '|' || (\"CreatedBy\" is not null)::text || '|' || coalesce((\"ApprovedBy\" <> \"CreatedBy\")::text, '') || '|' || coalesce(\"DecisionNote\", '') " +
                $"from \"AllowanceRecords\" where \"Id\" = '{allowance}'").Single();
            var pending = Row();
            try
            {
                var (own, _, _) = await PostFormAsync(processor, $"{Pay54Allowances}/Approve/{allowance}", new()
                {
                    ["__RequestVerificationToken"] = await TokenAsync(processor, $"{Pay54Allowances}/Create")
                });
                var afterOwn = Row();
                var (rejected, _, _) = await PostFormAsync(approver, $"{Pay54Allowances}/Reject/{allowance}", new()
                {
                    ["note"] = Pay54RejectNote,
                    ["__RequestVerificationToken"] = await TokenAsync(approver, Pay54Allowances)
                });
                var closed = Row();
                var pendingAfter = Sql("payroll_db", "select count(*) from \"AllowanceRecords\" where \"ApprovalStatus\" = 'PendingApproval'").Single();
                var inputsAfter = Pay54Inputs();
                recorder.WriteScenarioDbChecks(new
                {
                    employee,
                    allowance,
                    entered,
                    pending,
                    rowFormat = "approval status|origin|has an author|decided by another person|decision note",
                    own,
                    afterOwn,
                    rejected,
                    closed,
                    pendingBefore,
                    pendingAfter,
                    systemInputsUnchanged = inputsBefore == inputsAfter
                });

                Assert.Equal("PendingApproval|Manual|true||", pending);
                Assert.Equal(403, own);
                Assert.Equal(pending, afterOwn);
                Assert.True(rejected < 400, $"The rejection answered {rejected}.");
                Assert.Equal($"Rejected|Manual|true|true|{Pay54RejectNote}", closed);
                Assert.Equal(pendingBefore, pendingAfter);
                Assert.Equal(inputsBefore, inputsAfter);
            }
            finally
            {
                // Leave nothing open: whatever failed above, the allowance does not stay pending.
                if (Row().StartsWith("PendingApproval", StringComparison.Ordinal))
                {
                    await PostFormAsync(approver, $"{Pay54Allowances}/Reject/{allowance}", new()
                    {
                        ["note"] = Pay54RejectNote,
                        ["__RequestVerificationToken"] = await TokenAsync(approver, Pay54Allowances)
                    });
                }
            }
        }

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task AC_PAY_54_vps_the_framework_and_manual_input_screens_open_in_arabic_under_one_policy_with_no_violation()
    {
        var recorder = StartPay54("screens",
            "Demo server: the legal framework screens (the list, the check's version, the import and create forms) and the manual input lists, signed in as e2e-payroll-processor in a real Chromium: one CSP policy each, no securitypolicyviolation, Arabic and right-to-left, people named by the registry's span and no user id printed");
        var version = Sql("payroll_db",
            "select c.\"Id\" || '|' || (select e.\"UserId\" from \"PayrollConfigurationEditors\" e where e.\"PayrollConfigurationId\" = c.\"Id\" order by e.\"At\" limit 1) " +
            $"from \"PayrollConfigurations\" c where c.\"Code\" = '{Pay54Code}' order by c.\"VersionNumber\" desc limit 1").SingleOrDefault()?.Split('|');
        var run = Sql("payroll_db", "select \"Id\" from \"PayrollRuns\" order by \"PeriodStart\" desc limit 1").SingleOrDefault();
        AcceptanceGate.RequireData(recorder, version is { Length: 2 } && run is not null,
            "لا توجد نسخة فحص الخادم أو لا يوجد تشغيل رواتب", "Run the cycle check first: this check opens the version it leaves approved.");
        Ux067KeyPage[] pages =
        [
            new("frameworks", Pay54Pages),
            new("framework-details", $"{Pay54Pages}/Details/{version![0]}"),
            new("framework-import", $"{Pay54Pages}/Import"),
            new("framework-create", $"{Pay54Pages}/Create"),
            new("manual-allowances", Pay54Allowances),
            new("manual-allowance-form", $"{Pay54Allowances}/Create"),
            new("manual-deductions", "/ui/payroll/DeductionRecords"),
            new("manual-adjustments", "/ui/payroll/PayrollAdjustments"),
            new("run-details", $"/ui/payroll/PayrollRuns/Details/{run}"),
            new("payroll-dashboard", "/ui/payroll/Dashboard"),
        ];

        var password = DemoPassword();
        var output = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan54", "vps", "screens");
        IReadOnlyList<Ux067PageResult>? results = null;
        var attempts = 0;
        var networkChanges = new List<string>();
        // A page load fails with ERR_NETWORK_CHANGED when a network interface of the machine that runs the check changes
        // under Chromium (a local container restarting does it). That says nothing of the server: the probe is run again.
        while (results is null && attempts < 5)
        {
            attempts++;
            var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var state = Path.Combine(Path.GetTempPath(), $"pay54-state-{Guid.NewGuid():N}.json");
            try
            {
                await Ux067PageProbe.SignInAndSaveStateAsync(browser, $"https://{AppDomain}", Pay54Processor, password, state);
                results = await Ux067PageProbe.ProbeAsync(browser, $"https://{AppDomain}", state, pages, [(1366, 768), (390, 844)], Path.Combine(output, "screenshots"));
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("ERR_NETWORK_CHANGED", StringComparison.Ordinal))
            {
                networkChanges.Add(ex.Message.Split('\n')[0]);
            }
            finally
            {
                await browser.DisposeAsync();
                playwright.Dispose();
                if (File.Exists(state)) File.Delete(state);
            }
        }

        Assert.True(results is not null, $"The probe met a network change on this machine {attempts} times: {string.Join(" / ", networkChanges)}");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "csp-pages.json"), Ux067PageProbe.ToJson(results!));
        recorder.WriteScenarioSteps(string.Join("\n", results!.Select(r =>
            $"- {r.Name}: HTTP {r.Status}, {r.CspHeaders} CSP header(s), {r.CspPolicies} policies, {r.Violations.Count} violation(s)" + (r.Note is null ? "" : $" ({r.Note})"))));

        // Arabic, right-to-left, people by name, and every value with its reference and status: the version's page as the server sends it.
        var (processor, _) = await SignInAsync(Pay54Processor, password, "/ui/payroll/");
        string details;
        using (processor)
        using (var page = await processor.GetAsync($"https://{AppDomain}{Pay54Pages}/Details/{version[0]}"))
            details = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var visible = Regex.Replace(Regex.Replace(details, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase), "<[^>]+>", " ");
        var failures = results!.Where(r => !r.Passes || r.Status != 200)
            .Select(r => $"{r.Name}: HTTP {r.Status}, {r.CspPolicies} policies, {r.Violations.Count} violations {string.Join("; ", r.Violations.Select(v => $"{v.Directive} {v.BlockedUri} {v.Source}"))} {r.Note}")
            .ToList();
        recorder.WriteScenarioDbChecks(new
        {
            attempts,
            networkChanges,
            pages = results!.Select(r => new { r.Name, r.Status, r.CspHeaders, r.CspPolicies, violations = r.Violations.Count, r.Passes }),
            rightToLeft = details.Contains("dir=\"rtl\"", StringComparison.Ordinal),
            arabic = details.Contains("lang=\"ar\"", StringComparison.Ordinal),
            editorNamedByTheRegistry = details.Contains($"data-hr-user-id=\"{version[1]}\"", StringComparison.OrdinalIgnoreCase),
            editorIdPrinted = visible.Contains(version[1], StringComparison.OrdinalIgnoreCase),
            saysInterim = visible.Contains("مؤقت", StringComparison.Ordinal),
            namesTheException = visible.Contains("EX-STR2-LAW50-UNCERTIFIED", StringComparison.Ordinal),
            namesAStatutoryDeduction = visible.Contains("تأمينات اجتماعية", StringComparison.Ordinal)
        });

        Assert.Equal(pages.Length, results!.Count);
        Assert.True(failures.Count == 0, "Screens failing the CSP check:\n" + string.Join("\n", failures));
        Assert.Contains("dir=\"rtl\"", details, StringComparison.Ordinal);
        Assert.Contains("lang=\"ar\"", details, StringComparison.Ordinal);
        Assert.Contains($"data-hr-user-id=\"{version[1]}\"", details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(version[1], visible, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("مؤقت", visible, StringComparison.Ordinal);
        Assert.Contains("EX-STR2-LAW50-UNCERTIFIED", visible, StringComparison.Ordinal);
        Assert.Contains("تأمينات اجتماعية", visible, StringComparison.Ordinal);
        recorder.WriteResult("Pass");
    }
}
