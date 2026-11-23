using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-54 (§1, P-4) over HTTP on Payroll.Web itself (PostgreSQL), through the pages and the API a user reaches: an agency with no
/// framework is told so and where to start; a processor imports the legal pack (a file on the form, a JSON body on the API), sees every
/// table with its references and statuses in Arabic, exports the version and edits its date; a malformed pack answers 400 with the
/// field named and writes nothing; every write of the API needs the antiforgery token; and a person without the policy writes nothing.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay054FrameworkHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsLocation = new("/PayrollConfigurations/Details/([0-9a-fA-F-]{36})", RegexOptions.Compiled);

    internal static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
        }
    }

    internal static string InterimPackJson() =>
        System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "syr", "payroll-framework-packs", "law50-interim.json"));

    internal static string PackWith(Action<JsonObject> change)
    {
        var node = JsonNode.Parse(InterimPackJson())!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    internal static Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync(PostgreSqlFixture postgres) =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry,
            $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}",
            $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}",
            "/PayrollConfigurations/Create", (_, _) => Task.FromResult(new Dictionary<Type, Guid>()),
            // As production runs: no seed. The Development default would seed the Demo frameworks.
            settings: new Dictionary<string, string?> { ["Seed:Profile"] = "None" });

    internal static async Task<string> PageAsync(HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(url);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.True(response.StatusCode == expected, $"GET {url}: {(int)response.StatusCode}, expected {(int)expected}\n{html[..Math.Min(html.Length, 800)]}");
        return html;
    }

    internal static async Task<string> FormTokenAsync(HttpClient client, string formUrl)
    {
        var match = Token.Match(await PageAsync(client, formUrl));
        Assert.True(match.Success, $"No antiforgery token on {formUrl}");
        return match.Groups[1].Value;
    }

    /// <summary>Uploads a pack through the import form, as a browser does.</summary>
    internal static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string packJson, string? code = null, string? effectiveFrom = null, Guid? targetDraftId = null,
        string fileName = "pack.json", string contentType = "application/json")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(await FormTokenAsync(client, "/PayrollConfigurations/Import")), "__RequestVerificationToken" },
        };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(packJson));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "PackFile", fileName);
        if (code is not null) form.Add(new StringContent(code), "Code");
        if (effectiveFrom is not null) form.Add(new StringContent(effectiveFrom), "EffectiveFrom");
        if (targetDraftId is Guid target) form.Add(new StringContent(target.ToString("D")), "TargetDraftId");
        return await client.PostAsync("/PayrollConfigurations/Import", form);
    }

    internal static Guid FrameworkIdOf(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"expected a redirect to the version, got {(int)response.StatusCode}");
        var match = DetailsLocation.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Not redirected to the version: {response.Headers.Location}");
        return Guid.Parse(match.Groups[1].Value);
    }

    /// <summary>The client with the antiforgery token of the frameworks API on every request (the client keeps the cookie).</summary>
    internal static async Task<HttpClient> WithApiTokenAsync(HttpClient client)
    {
        using var issued = await client.GetAsync("/api/payroll/frameworks/antiforgery");
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var token = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(
            token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    internal static StringContent ImportBody(string packJson, string? code = null, string? effectiveFrom = null, Guid? targetDraftId = null)
    {
        var parts = new List<string> { "\"pack\":" + packJson };
        if (code is not null) parts.Add($"\"code\":\"{code}\"");
        if (effectiveFrom is not null) parts.Add($"\"effectiveFrom\":\"{effectiveFrom}\"");
        if (targetDraftId is Guid target) parts.Add($"\"targetDraftId\":\"{target:D}\"");
        return new StringContent("{" + string.Join(",", parts) + "}", Encoding.UTF8, "application/json");
    }

    private static Task<int> VersionCountAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host) =>
        host.WithDbAsync(db => db.Set<PayrollConfiguration>().CountAsync());

    private static Task<int> RowCountAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host) =>
        host.WithDbAsync(async db =>
            await db.Set<PayrollConfiguration>().CountAsync() + await db.Set<TaxBracket>().CountAsync()
            + await db.Set<StatutoryDeductionRule>().CountAsync() + await db.Set<OfficialHoliday>().CountAsync()
            + await db.Set<DisbursementPriorityRule>().CountAsync() + await db.Set<PayrollConfigurationFieldSource>().CountAsync()
            + await db.Set<PayrollConfigurationEditor>().CountAsync() + await db.Set<LegalDecisionRecord>().CountAsync());

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task An_agency_with_no_framework_imports_the_pack_on_the_form_and_reads_exports_and_edits_the_version()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync(postgres);
        using var processor = host.Central();

        // First run: the dashboard says there is no published framework and where to start; the list is empty and says the same.
        var dashboard = await PageAsync(processor, "/Dashboard");
        Assert.Contains("لا يوجد إطار منشور", dashboard, StringComparison.Ordinal);
        Assert.Contains("/PayrollConfigurations/Import", dashboard, StringComparison.Ordinal);
        Assert.Contains("استورد حزمة قانونية", await PageAsync(processor, "/PayrollConfigurations"), StringComparison.Ordinal);
        await PageAsync(processor, "/PayrollConfigurations/Create");

        // A malformed pack (a bracket of 120%): 400, the field and the row named in Arabic, nothing written.
        var malformed = await UploadAsync(processor, PackWith(p => p["taxBrackets"]![2]!["ratePercent"] = 120));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var refusal = WebUtility.HtmlDecode(await malformed.Content.ReadAsStringAsync());
        Assert.Contains("$.taxBrackets[2].ratePercent", refusal, StringComparison.Ordinal);
        Assert.Contains("الشريحة 3", refusal, StringComparison.Ordinal);
        Assert.Contains("رُفضت الحزمة ولم يُكتب منها شيء", refusal, StringComparison.Ordinal);
        Assert.Equal(0, await RowCountAsync(host));

        // A file that is not a JSON file is not read at all.
        var notJson = await UploadAsync(processor, InterimPackJson(), fileName: "pack.exe", contentType: "application/octet-stream");
        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);
        Assert.Contains("ملف الحزمة يجب أن يكون ملف JSON", WebUtility.HtmlDecode(await notJson.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(0, await RowCountAsync(host));

        // The interim pack, under its own code, from 2031.
        var imported = await UploadAsync(processor, InterimPackJson(), code: "LAW_50_2004_HTTP", effectiveFrom: "2031-01-01");
        var id = FrameworkIdOf(imported);
        var version = await host.WithDbAsync(db => db.Set<PayrollConfiguration>().AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.Equal(PayrollConfigPublicationStatus.Draft, version.PublicationStatus);
        Assert.Equal(PayrollConfiguration.CurrentHashSchemaVersion, version.HashSchemaVersion);
        Assert.Equal(new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc), version.EffectiveFrom);

        // The details page: the four tables, the field sources, each value with its reference, its status in Arabic and its exception.
        var details = await PageAsync(processor, $"/PayrollConfigurations/Details/{id}");
        foreach (var expected in new[]
                 {
                     "الحقول العددية ومراجعها", "شرائح الضريبة", "الخصومات النظامية", "العطل الرسمية", "قواعد أولوية الصرف",
                     "المرجع القانوني", "الحالة القانونية", "الاستثناء المؤقت", "مؤقتة", "مسودة", "بلا سقف",
                     "LD-TAX-BRACKETS", "EX-STR2-TAX-INTERIM", "EX-STR2-LAW50-UNCERTIFIED", "DED-SOC", "تأمينات اجتماعية", "عيد الجلاء",
                     "إجمالي الأجر", "قانون العاملين الأساسي 50/2004", "إطار مؤقت",
                 })
            Assert.True(details.Contains(expected, StringComparison.Ordinal), $"the details page does not show «{expected}»");
        // Statuses and kinds print through their Arabic names, never as the code writes them.
        foreach (var raw in new[] { ">Interim<", ">Draft<", ">GrossPay<", ">Imported<" })
            Assert.DoesNotContain(raw, details, StringComparison.Ordinal);

        // The export is a download of the pack, and it is the pack the importer takes.
        using (var export = await processor.GetAsync($"/PayrollConfigurations/Export/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Equal("application/json", export.Content.Headers.ContentType!.MediaType);
            Assert.Contains("LAW_50_2004_HTTP-v1.json", export.Content.Headers.ContentDisposition!.ToString(), StringComparison.Ordinal);
            using var pack = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
            Assert.Equal("payroll-framework-pack.v1", pack.RootElement.GetProperty("schema").GetString());
            Assert.Equal(4, pack.RootElement.GetProperty("statutoryDeductions").GetArrayLength());
        }

        // The edit form: the numeric fields and the effective date of the draft.
        var edit = await PageAsync(processor, $"/PayrollConfigurations/Edit/{id}");
        Assert.Contains("name=\"EffectiveFrom\"", edit, StringComparison.Ordinal);
        var saved = await processor.PostAsync($"/PayrollConfigurations/Edit/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(edit).Groups[1].Value,
            ["Id"] = id.ToString("D"),
            ["NameAr"] = "قانون العاملين الأساسي 50/2004",
            ["EffectiveFrom"] = "2031-03-01",
            ["NonWorkingDays"] = "Friday,Saturday",
            ["EosGratuityRateFirstPeriod"] = "0.5",
            ["EosGratuityFirstYearsThreshold"] = "5",
            ["EosGratuityRateAfterThreshold"] = "1",
            ["TaxableBaseMode"] = "GrossPay",
            ["TaxExemptionAmount"] = "0",
            ["LeavePayoutDailyDivisor"] = "30",
            ["DailyRateDivisor"] = "30",
            ["CurrencyCode"] = "SYP",
            ["DecimalPlaces"] = "2",
            ["RoundingMode"] = "0",
            ["MaxRetroCorrectionMonths"] = "6",
            ["UseDbHolidaysOnly"] = "true",
        }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var edited = await host.WithDbAsync(db => db.Set<PayrollConfiguration>().AsNoTracking()
            .Include(c => c.Editors).Include(c => c.StatutoryDeductions).SingleAsync(c => c.Id == id));
        Assert.Equal(new DateTime(2031, 3, 1, 0, 0, 0, DateTimeKind.Utc), edited.EffectiveFrom);
        Assert.Equal(6, edited.MaxRetroCorrectionMonths);
        Assert.Equal(4, edited.StatutoryDeductions.Count);                       // the edit form does not touch the tables
        Assert.Equal([PayrollFrameworkEditKind.Imported, PayrollFrameworkEditKind.Edited], edited.Editors.OrderBy(e => e.At).Select(e => e.Kind));
        Assert.Single(edited.Editors.Select(e => e.UserId).Distinct());
        Assert.Equal(1, await VersionCountAsync(host));

        // An import into the same draft replaces its content.
        var replaced = await UploadAsync(processor, PackWith(p => p["statutoryDeductions"]!.AsArray().RemoveAt(3)), code: "LAW_50_2004_HTTP", targetDraftId: id);
        Assert.Equal(id, FrameworkIdOf(replaced));
        Assert.Equal(3, await host.WithDbAsync(db => db.Set<StatutoryDeductionRule>().CountAsync(d => d.PayrollConfigurationId == id)));
        Assert.Equal(0, await host.WithDbAsync(db => db.Set<OfficialHoliday>().CountAsync(x => x.PayrollConfigurationId == null)));

        // A draft from a regime's template, on the create form.
        var createPage = await PageAsync(processor, "/PayrollConfigurations/Create");
        var created = await processor.PostAsync("/PayrollConfigurations/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(createPage).Groups[1].Value,
            ["RegimeCode"] = "LAW_53_2021",
            ["Code"] = "LAW_53_2021_HTTP",
            ["EffectiveFrom"] = "2031-01-01",
        }));
        var templateId = FrameworkIdOf(created);
        var template = await PageAsync(processor, $"/PayrollConfigurations/Details/{templateId}");
        Assert.Contains("قانون العاملين الأساسي 53/2021", template, StringComparison.Ordinal);
        // A create with no source: 400, the form again with the message.
        var noSource = await processor.PostAsync("/PayrollConfigurations/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(createPage).Groups[1].Value,
            ["EffectiveFrom"] = "2031-01-01",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, noSource.StatusCode);
        Assert.Contains("اختر مصدراً واحداً للمسودة", WebUtility.HtmlDecode(await noSource.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(2, await VersionCountAsync(host));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Every_write_of_the_frameworks_api_needs_the_antiforgery_token_and_its_policy_and_without_them_nothing_is_written()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync(postgres);
        using var bare = host.Central();

        // Without the token: 400 on every write, and nothing written.
        Assert.Equal(HttpStatusCode.BadRequest, (await bare.PostAsync("/api/payroll/frameworks/import", ImportBody(InterimPackJson()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bare.PostAsJsonAsync("/api/payroll/frameworks", new { regimeCode = "LAW_50_2004", effectiveFrom = "2031-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await bare.PutAsJsonAsync($"/api/payroll/frameworks/{Guid.NewGuid():D}/effective-from", new { effectiveFrom = "2031-01-01" })).StatusCode);
        Assert.Equal(0, await RowCountAsync(host));

        // With it: the processor imports (201), reads, sets the date, exports.
        using var processor = await WithApiTokenAsync(host.Central());
        var imported = await processor.PostAsync("/api/payroll/frameworks/import", ImportBody(InterimPackJson(), code: "LAW_50_2004_API", effectiveFrom: "2031-01-01"));
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        using var body = JsonDocument.Parse(await imported.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("frameworkId").GetGuid();
        Assert.Equal("Draft", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("versionNumber").GetInt32());

        var moved = await processor.PutAsJsonAsync($"/api/payroll/frameworks/{id:D}/effective-from", new { effectiveFrom = "2031-02-01" });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        using (var read = JsonDocument.Parse(await processor.GetStringAsync($"/api/payroll/frameworks/{id:D}")))
        {
            Assert.Equal("2031-02-01", read.RootElement.GetProperty("effectiveFrom").GetString());
            Assert.Equal(2, read.RootElement.GetProperty("hashSchemaVersion").GetInt32());
        }

        using (var export = await processor.GetAsync($"/api/payroll/frameworks/{id:D}/export"))
        {
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            using var pack = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
            Assert.Equal("LAW_50_2004_API", pack.RootElement.GetProperty("code").GetString());
            Assert.Equal("2031-02-01", pack.RootElement.GetProperty("effectiveFrom").GetString());
        }

        // A refused pack over the API: 400, each error with its path and an Arabic message, nothing more written.
        var rows = await RowCountAsync(host);
        var refused = await processor.PostAsync("/api/payroll/frameworks/import",
            ImportBody(PackWith(p => p["taxBrackets"]![2]!["ratePercent"] = 120), code: "LAW_50_2004_BAD"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using (var answer = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            var error = Assert.Single(answer.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal("$.taxBrackets[2].ratePercent", error.GetProperty("path").GetString());
            Assert.Matches(@"\p{IsArabic}", error.GetProperty("message").GetString()!);
        }

        // A body that is not the envelope, and one that is not JSON: 400 in Arabic, the body never repeated.
        foreach (var bad in new[] { "{\"paket\":{}}", "ZZ-ECHO-MARKER not json", "[]", "{\"pack\":{},\"effectiveFrom\":\"01/02/2031\"}" })
        {
            var answer = await processor.PostAsync("/api/payroll/frameworks/import", new StringContent(bad, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
            var text = await answer.Content.ReadAsStringAsync();
            Assert.DoesNotContain("ZZ-ECHO-MARKER", text, StringComparison.Ordinal);
            Assert.DoesNotContain("paket", text, StringComparison.Ordinal);
        }

        Assert.Equal(rows, await RowCountAsync(host));

        // A person without Payroll.MasterData: the approver and the module role alone write nothing and export nothing (403).
        using var approver = await WithApiTokenAsync(host.Restricted());
        using var moduleOnly = await WithApiTokenAsync(host.CentralWith(HrManagementRoles.Payroll, Guid.NewGuid()));
        foreach (var client in new[] { approver, moduleOnly })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/payroll/frameworks/import", ImportBody(InterimPackJson(), code: "LAW_50_2004_NO"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/payroll/frameworks", new { regimeCode = "LAW_50_2004", effectiveFrom = "2031-01-01" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PutAsJsonAsync($"/api/payroll/frameworks/{id:D}/effective-from", new { effectiveFrom = "2032-01-01" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/payroll/frameworks/{id:D}/export")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/PayrollConfigurations/Import")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/PayrollConfigurations/Export/{id:D}")).StatusCode);
            // The list and the version stay readable to the module.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/payroll/frameworks/{id:D}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/PayrollConfigurations/Details/{id:D}")).StatusCode);
        }

        Assert.Equal(rows, await RowCountAsync(host));
        Assert.Equal(new DateTime(2031, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            await host.WithDbAsync(db => db.Set<PayrollConfiguration>().AsNoTracking().Where(c => c.Id == id).Select(c => c.EffectiveFrom).SingleAsync()));
    }
}

/// <summary>
/// PLAN-54 (§1): the edit form of a draft is a view model, and it covers every numeric field that governs pay: a field added to the
/// governed list without a place on the form fails here, as does a field the form reads but does not write back.
/// </summary>
public sealed class Pay054FrameworkEditFormTests
{
    [Fact]
    public void The_edit_form_reads_and_writes_every_governed_field_and_nothing_of_the_cycle()
    {
        var source = Payroll.Domain.Services.PayrollRegimeSeed.CreateLaw50Configuration();
        source.DailyRateDivisor = 26;
        source.MissionAllowanceAmount = 125.5m;
        source.SecondmentAllowancePercent = 12.25m;
        source.EnableTrainingPremiums = true;
        source.TrainingPremiumAmount = 77.75m;
        source.MedicalCompensationDefaultAmount = 33.3m;
        source.MaxRetroCorrectionMonths = 7;
        source.EosGratuityRateFirstPeriod = 0.75m;
        source.EosGratuityFirstYearsThreshold = 9;
        source.EosGratuityRateAfterThreshold = 1.5m;
        source.NonWorkingDays = "Friday";
        source.UseDbHolidaysOnly = false;
        source.TaxableBaseMode = "NetOfStatutory";
        source.TaxExemptionAmount = 1234.56m;
        source.LeavePayoutDailyDivisor = 22;
        source.RoundingMode = PayrollRoundingMode.ToEven;
        source.DecimalPlaces = 3;
        source.CurrencyCode = "USD";
        source.ProrationDaysBasis = ProrationDaysBasis.Calendar30;

        var form = Payroll.Web.ViewModels.PayrollFrameworkEditViewModel.From(source);
        var target = new PayrollConfiguration();
        form.ApplyTo(target);

        foreach (var field in PayrollConfigurationFieldSource.GovernedFields)
        {
            Assert.NotNull(typeof(Payroll.Web.ViewModels.PayrollFrameworkEditViewModel).GetProperty(field));
            var property = typeof(PayrollConfiguration).GetProperty(field)!;
            Assert.True(Equals(property.GetValue(source), property.GetValue(target)), $"{field} is not carried by the edit form");
        }

        // Nothing of the cycle, of the fingerprint or of the tables is a property of the form.
        var formProperties = typeof(Payroll.Web.ViewModels.PayrollFrameworkEditViewModel).GetProperties().Select(p => p.Name).ToList();
        foreach (var forbidden in new[]
                 {
                     nameof(PayrollConfiguration.PublicationStatus), nameof(PayrollConfiguration.ContentHash), nameof(PayrollConfiguration.IsActive),
                     nameof(PayrollConfiguration.ApprovedBy), nameof(PayrollConfiguration.PublishedBy), nameof(PayrollConfiguration.VersionNumber),
                     nameof(PayrollConfiguration.HashSchemaVersion), nameof(PayrollConfiguration.Code), nameof(PayrollConfiguration.RegimeCode),
                     nameof(PayrollConfiguration.TaxBrackets), nameof(PayrollConfiguration.StatutoryDeductions), nameof(PayrollConfiguration.Editors),
                     nameof(PayrollConfiguration.EffectiveTo), nameof(PayrollConfiguration.TenantId),
                 })
            Assert.DoesNotContain(forbidden, formProperties);
    }
}
