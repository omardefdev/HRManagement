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

    /// <summary>
    /// Payroll.Web with no seed (as production runs) and the test's clock: 23 November 2026 unless a test moves it, so the interim
    /// gate (the real catalog's exceptions end on 31 January 2027) never depends on the day the suite runs.
    /// </summary>
    internal static Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync(PostgreSqlFixture postgres, Pay054Clock? clock = null) =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry,
            $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}",
            $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}",
            "/PayrollConfigurations/Create", (_, _) => Task.FromResult(new Dictionary<Type, Guid>()),
            testServices: Pay054TestLegal.Services(clock ?? new Pay054Clock(Pay054Clock.Default)),
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

    /// <summary>A person the shared host does not know: an unrestricted user with the given roles.</summary>
    internal static HttpClient Person(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid user, string roles)
    {
        var client = host.Host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
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
        Assert.Equal([PayrollFrameworkEditKind.Imported, PayrollFrameworkEditKind.Edited], edited.Editors.Select(e => e.Kind).Order());
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
/// PLAN-54 (§3, P-8, P-10) over HTTP on Payroll.Web itself (PostgreSQL): the cycle of a framework version as three people run it, on
/// the screens and on the API. A refusal answers 400 with an Arabic message and changes nothing: on a form the version's page is
/// rendered with the message; a person without the act's policy is refused 403. And the indicators: a run's page and its provenance
/// say the framework is interim and under which exception, and the dashboard warns from 60 days before an exception ends.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay054FrameworkCycleHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Guid AttestorUser = Guid.Parse("54000000-0000-4000-8000-0000000000d3");
    private static readonly Guid BothUser = Guid.Parse("54000000-0000-4000-8000-0000000000d4");

    private static async Task<PayrollConfiguration> VersionAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid id) =>
        await host.WithDbAsync(db => db.Set<PayrollConfiguration>().AsNoTracking().SingleAsync(c => c.Id == id));

    private static async Task<string> StateAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid id)
    {
        var v = await VersionAsync(host, id);
        var attestations = await host.WithDbAsync(db => db.Set<LegalAttestation>().CountAsync(a => a.PayrollConfigurationId == id));
        return string.Join("|", v.PublicationStatus, v.IsActive, v.ContentHash, v.ApprovedBy, v.PublishedBy, v.EffectiveFrom.Ticks, attestations);
    }

    /// <summary>Posts one of the cycle's forms from the version's page, with the page's own token.</summary>
    private static async Task<HttpResponseMessage> PostActAsync(HttpClient client, Guid id, string act)
    {
        var page = await Pay054FrameworkHttpTests.PageAsync(client, $"/PayrollConfigurations/Details/{id}");
        var token = Token.Match(page);
        var fields = new Dictionary<string, string>();
        if (token.Success)
            fields["__RequestVerificationToken"] = token.Groups[1].Value;
        else
            fields["__RequestVerificationToken"] = await Pay054FrameworkHttpTests.FormTokenAsync(client, "/PayrollConfigurations");
        return await client.PostAsync($"/PayrollConfigurations/{act}/{id}", new FormUrlEncodedContent(fields));
    }

    private static async Task AssertFormRefusalAsync(
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, HttpClient client, Guid id, string act, string message)
    {
        var before = await StateAsync(host, id);

        var response = await PostActAsync(client, id, act);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(message, html, StringComparison.Ordinal);                           // the version's page, with the message
        Assert.Contains("data-testid=\"framework-refusal\"", html, StringComparison.Ordinal);
        Assert.Contains("شرائح الضريبة", html, StringComparison.Ordinal);
        Assert.Equal(before, await StateAsync(host, id));
    }

    private static async Task AssertApiRefusalAsync(
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, HttpClient client, Guid id, string act, string message, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        var before = await StateAsync(host, id);

        var response = await client.PostAsync($"/api/payroll/frameworks/{id:D}/{act}", null);

        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.BadRequest)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(message, body.RootElement.GetProperty("error").GetString());
        }

        Assert.Equal(before, await StateAsync(host, id));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Three_people_approve_attest_and_publish_and_every_other_order_is_refused_with_nothing_changed()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var clock = new Pay054Clock(Pay054Clock.Default);
        await using var host = await Pay054FrameworkHttpTests.StartAsync(postgres, clock);
        var approverRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}";
        using var processor = await Pay054FrameworkHttpTests.WithApiTokenAsync(host.Central());
        using var approver = await Pay054FrameworkHttpTests.WithApiTokenAsync(host.Restricted());
        using var attestor = await Pay054FrameworkHttpTests.WithApiTokenAsync(Pay054FrameworkHttpTests.Person(host, AttestorUser, approverRoles));
        // A person who holds both roles: once he edits the draft he is an editor, whatever his policies allow.
        using var both = await Pay054FrameworkHttpTests.WithApiTokenAsync(Pay054FrameworkHttpTests.Person(
            host, BothUser, $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor},{HrManagementRoles.PayrollApprover}"));

        var imported = await processor.PostAsync("/api/payroll/frameworks/import",
            Pay054FrameworkHttpTests.ImportBody(Pay054FrameworkHttpTests.InterimPackJson(), code: "LAW_50_2004_CYC", effectiveFrom: "2031-01-01"));
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        Guid id;
        using (var body = JsonDocument.Parse(await imported.Content.ReadAsStringAsync()))
            id = body.RootElement.GetProperty("frameworkId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await both.PutAsJsonAsync($"/api/payroll/frameworks/{id:D}/effective-from", new { effectiveFrom = "2031-01-01" })).StatusCode);

        // The processor holds no policy of the cycle: 403 on the form and on the API, for every act.
        foreach (var act in new[] { "Approve", "LegalAttest", "Publish", "Activate" })
            Assert.Equal(HttpStatusCode.Forbidden, (await PostActAsync(processor, id, act)).StatusCode);
        foreach (var act in new[] { "approve", "legal-attest", "publish", "activate" })
            await AssertApiRefusalAsync(host, processor, id, act, "", HttpStatusCode.Forbidden);

        // Nothing is published from a draft, nor attested, nor activated.
        await AssertFormRefusalAsync(host, approver, id, "Publish", "لا يُنشر إلا إصدار معتمد");
        await AssertApiRefusalAsync(host, approver, id, "publish", "لا يُنشر إلا إصدار معتمد: المسودة تُعتمد ثم تُصادَق قانونياً ثم تُنشر");
        await AssertFormRefusalAsync(host, attestor, id, "LegalAttest", "التصديق القانوني لنسخة معتمدة لم تُنشر بعد");
        await AssertFormRefusalAsync(host, approver, id, "Activate", "التفعيل للنسخ المنشورة فقط");

        // An editor does not approve, though his role would let him.
        await AssertApiRefusalAsync(host, both, id, "approve", "لا يعتمد النسخة من أنشأها أو استوردها أو عدّلها: يعتمدها شخص آخر");
        await AssertFormRefusalAsync(host, both, id, "Approve", "لا يعتمد النسخة من أنشأها أو استوردها أو عدّلها");

        // B approves on the form. The page then offers the attestation and the publication, no longer the approval.
        var approved = await PostActAsync(approver, id, "Approve");
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var version = await VersionAsync(host, id);
        Assert.Equal(PayrollConfigPublicationStatus.Approved, version.PublicationStatus);
        Assert.Equal(64, version.ContentHash!.Length);
        var page = await Pay054FrameworkHttpTests.PageAsync(approver, $"/PayrollConfigurations/Details/{id}");
        Assert.Contains($"/PayrollConfigurations/LegalAttest/{id}", page, StringComparison.Ordinal);
        Assert.Contains($"/PayrollConfigurations/Publish/{id}", page, StringComparison.Ordinal);
        Assert.DoesNotContain($"/PayrollConfigurations/Approve/{id}", page, StringComparison.Ordinal);
        Assert.Contains("معتمد", page, StringComparison.Ordinal);

        // The approver does not attest; an editor neither; nothing is published without an attestation; an approved version is not edited in place.
        await AssertFormRefusalAsync(host, approver, id, "LegalAttest", "لا يصادق قانونياً على النسخة من اعتمدها: يصادق عليها شخص ثالث");
        await AssertApiRefusalAsync(host, approver, id, "legal-attest", "لا يصادق قانونياً على النسخة من اعتمدها: يصادق عليها شخص ثالث");
        await AssertApiRefusalAsync(host, both, id, "legal-attest", "لا يصادق قانونياً على النسخة من أنشأها أو استوردها أو عدّلها");
        await AssertApiRefusalAsync(host, approver, id, "publish", "يلزم تصديق قانوني إلكتروني (LegalAttest) قبل النشر.");
        var moved = await processor.PutAsJsonAsync($"/api/payroll/frameworks/{id:D}/effective-from", new { effectiveFrom = "2032-01-01" });
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal(new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc), (await VersionAsync(host, id)).EffectiveFrom);

        // C attests on the API.
        Assert.Equal(HttpStatusCode.OK, (await attestor.PostAsync($"/api/payroll/frameworks/{id:D}/legal-attest", null)).StatusCode);
        var attestation = await host.WithDbAsync(db => db.Set<LegalAttestation>().AsNoTracking().SingleAsync(a => a.PayrollConfigurationId == id));
        Assert.Equal((AttestorUser, "Active", version.ContentHash), (attestation.AttestedBy, attestation.Status, attestation.ContentHash));
        Assert.Contains("EX-STR2-TAX-INTERIM", attestation.InterimExceptionIdsJson, StringComparison.Ordinal);

        // An editor does not publish. B publishes on the form.
        await AssertFormRefusalAsync(host, both, id, "Publish", "لا ينشر النسخة من أنشأها أو استوردها أو عدّلها");
        var published = await PostActAsync(approver, id, "Publish");
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        version = await VersionAsync(host, id);
        Assert.Equal(PayrollConfigPublicationStatus.Published, version.PublicationStatus);
        Assert.True(version.IsActive);
        Assert.Equal(1, await host.WithDbAsync(db => db.Set<PayrollConfigChangeNotice>().CountAsync(n => n.PayrollConfigurationId == id)));
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsync($"/api/payroll/frameworks/{id:D}/activate", null)).StatusCode);

        // D-PAY-13: a second version that would start inside a posted month of the regime is approved and attested, never published.
        await host.WithDbAsync(async db =>
        {
            db.Set<PayrollRun>().Add(new PayrollRun
            {
                TenantId = HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId,
                PeriodStart = new DateTime(2031, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2031, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                RunDate = new DateTime(2031, 3, 28, 0, 0, 0, DateTimeKind.Utc),
                Status = PayrollRunStatus.Posted,
            });
            return await db.SaveChangesAsync();
        });
        var second = await processor.PostAsync("/api/payroll/frameworks/import",
            Pay054FrameworkHttpTests.ImportBody(Pay054FrameworkHttpTests.InterimPackJson(), code: "LAW_50_2004_CYC", effectiveFrom: "2031-02-01"));
        Guid secondId;
        using (var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync()))
        {
            secondId = body.RootElement.GetProperty("frameworkId").GetGuid();
            Assert.Equal(2, body.RootElement.GetProperty("versionNumber").GetInt32());
        }

        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsync($"/api/payroll/frameworks/{secondId:D}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await attestor.PostAsync($"/api/payroll/frameworks/{secondId:D}/legal-attest", null)).StatusCode);
        await AssertApiRefusalAsync(host, attestor, secondId, "publish",
            "لا تُنشر نسخة تسري من 2031-02-01: للنظام نفسه تشغيل رواتب مرحّل تنتهي فترته في 2031-03-31. اختر تاريخ سريان بعد آخر فترة مرحّلة (الأثر الرجعي غير متاح)");
        await AssertFormRefusalAsync(host, attestor, secondId, "Publish", "للنظام نفسه تشغيل رواتب مرحّل تنتهي فترته في 2031-03-31");
        Assert.Equal(PayrollConfigPublicationStatus.Published, (await VersionAsync(host, id)).PublicationStatus);     // the first is not superseded
        Assert.Null((await VersionAsync(host, id)).EffectiveTo);

        // An act on a version that does not exist: 404.
        Assert.Equal(HttpStatusCode.NotFound, (await approver.PostAsync($"/api/payroll/frameworks/{Guid.NewGuid():D}/approve", null)).StatusCode);
        // And every act of the API needs the antiforgery token.
        using var bare = host.Restricted();
        foreach (var act in new[] { "approve", "legal-attest", "publish", "activate" })
            Assert.Equal(HttpStatusCode.BadRequest, (await bare.PostAsync($"/api/payroll/frameworks/{secondId:D}/{act}", null)).StatusCode);

        // The indicators. A run pinned to the interim version says so, with its exceptions, on its page and in its provenance; a run
        // pinned to a version older than legal packs says that.
        var (interimRun, legacyRun) = await host.WithDbAsync(async db =>
        {
            var legacy = Payroll.Domain.Services.PayrollRegimeSeed.CreateLaw53Configuration();
            legacy.TenantId = HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId;
            foreach (var bracket in legacy.TaxBrackets) bracket.TenantId = legacy.TenantId;
            db.Set<PayrollConfiguration>().Add(legacy);
            PayrollRun Run(Guid configuration, string? hash, int month) => new()
            {
                TenantId = legacy.TenantId,
                PeriodStart = new DateTime(2031, month, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2031, month, 28, 0, 0, 0, DateTimeKind.Utc),
                RunDate = new DateTime(2031, month, 27, 0, 0, 0, DateTimeKind.Utc),
                Status = PayrollRunStatus.Calculated,
                PayrollConfigurationId = configuration,
                ConfigContentHash = hash,
            };
            var a = Run(id, version.ContentHash, 5);
            var b = Run(legacy.Id, legacy.ContentHash, 6);
            db.Set<PayrollRun>().AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });
        var interimPage = await Pay054FrameworkHttpTests.PageAsync(approver, $"/PayrollRuns/Details/{interimRun}");
        Assert.Contains("إطار مؤقت — استثناء EX-STR2-LAW50-UNCERTIFIED، EX-STR2-TAX-INTERIM", interimPage, StringComparison.Ordinal);
        Assert.Contains("إطار أقدم من حزم الإطار القانوني", await Pay054FrameworkHttpTests.PageAsync(approver, $"/PayrollRuns/Details/{legacyRun}"), StringComparison.Ordinal);
        using (var provenance = JsonDocument.Parse(await approver.GetStringAsync($"/api/payroll/runs/{interimRun:D}/provenance")))
        {
            Assert.Equal("إطار مؤقت — استثناء EX-STR2-LAW50-UNCERTIFIED، EX-STR2-TAX-INTERIM", provenance.RootElement.GetProperty("interimIndicator").GetString());
            Assert.Equal(2, provenance.RootElement.GetProperty("frameworkHashSchemaVersion").GetInt32());
            Assert.False(provenance.RootElement.GetProperty("frameworkPredatesLegalPacks").GetBoolean());
            Assert.Equal("LAW_50_2004_CYC", provenance.RootElement.GetProperty("frameworkCode").GetString());
            Assert.Equal(2, provenance.RootElement.GetProperty("interimExceptionIds").GetArrayLength());
        }

        using (var provenance = JsonDocument.Parse(await approver.GetStringAsync($"/api/payroll/runs/{legacyRun:D}/provenance")))
        {
            Assert.True(provenance.RootElement.GetProperty("frameworkPredatesLegalPacks").GetBoolean());
            Assert.Equal(JsonValueKind.Null, provenance.RootElement.GetProperty("interimIndicator").ValueKind);
        }

        // The dashboard. Today (23 November 2026) the exceptions end in 69 days: no warning yet, and the framework notice is gone.
        var dashboard = await Pay054FrameworkHttpTests.PageAsync(approver, "/Dashboard");
        Assert.DoesNotContain("data-testid=\"exception-expiry-warning\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"no-published-framework\"", dashboard, StringComparison.Ordinal);
        // From 2 December 2026 (60 days before 31 January 2027) it names each exception and its date.
        clock.Now = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.DoesNotContain("data-testid=\"exception-expiry-warning\"", await Pay054FrameworkHttpTests.PageAsync(approver, "/Dashboard"), StringComparison.Ordinal);
        clock.Now = new DateTimeOffset(2026, 12, 2, 12, 0, 0, TimeSpan.Zero);
        dashboard = await Pay054FrameworkHttpTests.PageAsync(approver, "/Dashboard");
        Assert.Contains("ينتهي الاستثناء القانوني EX-STR2-TAX-INTERIM", dashboard, StringComparison.Ordinal);
        Assert.Contains("ينتهي الاستثناء القانوني EX-STR2-LAW50-UNCERTIFIED", dashboard, StringComparison.Ordinal);
        Assert.Contains("بعد 60 يوماً", dashboard, StringComparison.Ordinal);
        Assert.Contains("LAW_50_2004_CYC v1", dashboard, StringComparison.Ordinal);
        // After the last day it says the exception has ended, and an approval of a new interim draft is refused for it.
        clock.Now = new DateTimeOffset(2027, 2, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Contains("انتهى الاستثناء القانوني EX-STR2-TAX-INTERIM", await Pay054FrameworkHttpTests.PageAsync(approver, "/Dashboard"), StringComparison.Ordinal);
        var third = await processor.PostAsync("/api/payroll/frameworks/import",
            Pay054FrameworkHttpTests.ImportBody(Pay054FrameworkHttpTests.InterimPackJson(), code: "LAW_50_2004_LATE", effectiveFrom: "2032-01-01"));
        Guid thirdId;
        using (var body = JsonDocument.Parse(await third.Content.ReadAsStringAsync()))
            thirdId = body.RootElement.GetProperty("frameworkId").GetGuid();
        var late = await approver.PostAsync($"/api/payroll/frameworks/{thirdId:D}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        Assert.Contains("وهو غير نافذ اليوم", await late.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(PayrollConfigPublicationStatus.Draft, (await VersionAsync(host, thirdId)).PublicationStatus);
    }
}

/// <summary>
/// PLAN-54 (§2 rule 2): the deduction types table is the catalogue of non-statutory deductions. On its screens «إلزامي» is read-only,
/// with the way to the legal framework; a posted value is ignored on create and on edit.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay054DeductionTypesHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Is_statutory_is_read_only_on_the_deduction_types_screens_and_a_posted_value_is_ignored()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await Pay054FrameworkHttpTests.StartAsync(postgres);
        using var processor = host.Central();
        var legacy = await host.WithDbAsync(async db =>
        {
            var type = new DeductionType { Code = "DED-SOC", NameAr = "تأمينات اجتماعية", IsStatutory = true, Percentage = 7m };
            db.Set<DeductionType>().Add(type);
            await db.SaveChangesAsync();
            return type.Id;
        });

        var create = await Pay054FrameworkHttpTests.PageAsync(processor, "/DeductionTypes/Create");
        Assert.Contains("انتقل إلى الإطار القانوني", create, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"IsStatutory\"", create, StringComparison.Ordinal);
        var created = await processor.PostAsync("/DeductionTypes/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(create).Groups[1].Value,
            ["Code"] = "DED-CRAFTED",
            ["NameAr"] = "اقتطاع مصنوع",
            ["IsStatutory"] = "true",
            ["Percentage"] = "50",
        }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.False(await host.WithDbAsync(db => db.Set<DeductionType>().AsNoTracking().Where(t => t.Code == "DED-CRAFTED").Select(t => t.IsStatutory).SingleAsync()));

        var edit = await Pay054FrameworkHttpTests.PageAsync(processor, $"/DeductionTypes/Edit/{legacy}");
        Assert.Contains("انتقل إلى الإطار القانوني", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"IsStatutory\"", edit, StringComparison.Ordinal);
        var edited = await processor.PostAsync($"/DeductionTypes/Edit/{legacy}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(edit).Groups[1].Value,
            ["Id"] = legacy.ToString("D"),
            ["Code"] = "DED-SOC",
            ["NameAr"] = "تأمينات اجتماعية",
            ["IsStatutory"] = "false",
            ["Percentage"] = "7",
        }));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        // A row marked statutory before PLAN-54 stays so (legacy runs read it); the posted «false» changed nothing.
        Assert.True(await host.WithDbAsync(db => db.Set<DeductionType>().AsNoTracking().Where(t => t.Id == legacy).Select(t => t.IsStatutory).SingleAsync()));
    }
}

/// <summary>
/// PLAN-54 (§4, D-PAY-6) over HTTP on Payroll.Web itself (PostgreSQL), through the pages a user opens: a processor enters an
/// allowance, a deduction and an adjustment; each is pending and says so; fields a form does not have (the run, the approval state)
/// are ignored; the processor cannot approve; a second person approves or rejects, a rejection with its reason; and a person who
/// holds both roles is refused his own input.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay054ManualInputHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Guid BothUser = Guid.Parse("54000000-0000-4000-8000-0000000000d4");

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string url, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await Pay054FrameworkHttpTests.FormTokenAsync(client, tokenPage);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_manual_input_waits_for_a_second_person_on_the_three_screens()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await Pay054FrameworkHttpTests.StartAsync(postgres);
        using var processor = host.Central();
        using var approver = host.Restricted();
        using var both = Pay054FrameworkHttpTests.Person(
            host, BothUser, $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor},{HrManagementRoles.PayrollApprover}");
        var tenant = HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId;
        var (employee, type, postedRun) = await host.WithDbAsync(async db =>
        {
            var id = Guid.NewGuid();
            var allowanceType = new CompensationType { TenantId = tenant, Code = "ALL-NAT", NameAr = "بدل طبيعة عمل" };
            db.Set<CompensationType>().Add(allowanceType);
            db.Set<EmployeePayProfile>().Add(new EmployeePayProfile
            {
                TenantId = tenant,
                EmployeeId = id,
                BaseSalary = 100_000m,
                IsActive = true,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            var run = new PayrollRun
            {
                TenantId = tenant,
                Status = PayrollRunStatus.Posted,
                RunDate = DateTime.UtcNow,
                PeriodStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Set<PayrollRun>().Add(run);
            await db.SaveChangesAsync();
            return (id, allowanceType.Id, run.Id);
        });

        // The three forms and the three lists render for the processor.
        foreach (var url in new[] { "/AllowanceRecords", "/AllowanceRecords/Create", "/DeductionRecords", "/DeductionRecords/Create", "/PayrollAdjustments", "/PayrollAdjustments/Create" })
            await Pay054FrameworkHttpTests.PageAsync(processor, url);
        Assert.DoesNotContain("AppliedInRunId", await Pay054FrameworkHttpTests.PageAsync(processor, "/DeductionRecords/Create"), StringComparison.Ordinal);
        Assert.DoesNotContain("PayrollRunId", await Pay054FrameworkHttpTests.PageAsync(processor, "/PayrollAdjustments/Create"), StringComparison.Ordinal);

        // The processor enters one of each. The crafted fields are not on the forms and are ignored.
        var crafted = new Dictionary<string, string>
        {
            ["AppliedInRunId"] = postedRun.ToString("D"),
            ["PayrollRunId"] = postedRun.ToString("D"),
            ["ApprovalStatus"] = "Approved",
            ["Origin"] = "System",
            ["ApprovedBy"] = Guid.NewGuid().ToString("D"),
            ["CreatedBy"] = Guid.NewGuid().ToString("D"),
        };
        Dictionary<string, string> With(Dictionary<string, string> fields) => fields.Concat(crafted).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(processor, "/AllowanceRecords/Create", "/AllowanceRecords/Create", With(new()
        {
            ["EmployeeId"] = employee.ToString("D"),
            ["AllowanceTypeId"] = type.ToString("D"),
            ["Amount"] = "1500.75",
            ["EffectiveFrom"] = "2026-10-01",
            ["Reason"] = "قرار الوزير 12 لعام 2026",
        }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(processor, "/DeductionRecords/Create", "/DeductionRecords/Create", With(new()
        {
            ["EmployeeId"] = employee.ToString("D"),
            ["DeductionType"] = "Other",
            ["Amount"] = "250.5",
            ["Reason"] = "استرداد مبلغ صُرف خطأً",
        }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(processor, "/PayrollAdjustments/Create", "/PayrollAdjustments/Create", With(new()
        {
            ["EmployeeId"] = employee.ToString("D"),
            ["Amount"] = "-75.25",
            ["AdjustmentReason"] = "تسوية فرق تقريب",
        }))).StatusCode);

        var (allowance, deduction, adjustment) = await host.WithDbAsync(async db => (
            await db.Set<AllowanceRecord>().AsNoTracking().SingleAsync(),
            await db.Set<DeductionRecord>().AsNoTracking().SingleAsync(),
            await db.Set<PayrollAdjustment>().AsNoTracking().SingleAsync()));
        foreach (IManualPayInput input in new IManualPayInput[] { allowance, deduction, adjustment })
        {
            Assert.Equal(ManualInputApprovalStatus.PendingApproval, input.ApprovalStatus);
            Assert.Equal(ManualInputOrigin.Manual, input.Origin);
            Assert.NotNull(input.CreatedBy);
            Assert.Null(input.ApprovedBy);
        }

        Assert.Equal(1500.75m, allowance.Amount);                                   // a fraction binds under the Arabic culture
        Assert.Equal(-75.25m, adjustment.Amount);
        Assert.Null(deduction.AppliedInRunId);                                      // the posted run id was ignored
        Assert.Null(adjustment.PayrollRunId);

        // A form with no reason is refused, 400, and writes nothing.
        var noReason = await PostAsync(processor, "/PayrollAdjustments/Create", "/PayrollAdjustments/Create", new()
        {
            ["EmployeeId"] = employee.ToString("D"),
            ["Amount"] = "10",
        });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(1, await host.WithDbAsync(db => db.Set<PayrollAdjustment>().CountAsync()));

        // The lists say the inputs wait. The processor sees no decision form and a crafted decision is refused (403).
        var list = await Pay054FrameworkHttpTests.PageAsync(processor, "/AllowanceRecords");
        Assert.Contains("بانتظار الاعتماد", list, StringComparison.Ordinal);
        Assert.Contains("يدوي", list, StringComparison.Ordinal);
        Assert.DoesNotContain($"/AllowanceRecords/Approve/{allowance.Id}", list, StringComparison.Ordinal);
        foreach (var url in new[] { $"/AllowanceRecords/Approve/{allowance.Id}", $"/DeductionRecords/Reject/{deduction.Id}", $"/PayrollAdjustments/Approve/{adjustment.Id}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(processor, "/AllowanceRecords/Create", url, new() { ["note"] = "x" })).StatusCode);

        // A person with both roles who entered an input is refused his own (403, the list with the message), not another's.
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(both, "/PayrollAdjustments/Create", "/PayrollAdjustments/Create", new()
        {
            ["EmployeeId"] = employee.ToString("D"),
            ["Amount"] = "40",
            ["AdjustmentReason"] = "تسوية أدخلها صاحب الدورين",
        })).StatusCode);
        var ownId = await host.WithDbAsync(db => db.Set<PayrollAdjustment>().AsNoTracking().Where(a => a.CreatedBy == BothUser).Select(a => a.Id).SingleAsync());
        var bothList = await Pay054FrameworkHttpTests.PageAsync(both, "/PayrollAdjustments");
        Assert.DoesNotContain($"/PayrollAdjustments/Approve/{ownId}", bothList, StringComparison.Ordinal);     // no form for his own
        Assert.Contains($"/PayrollAdjustments/Approve/{adjustment.Id}", bothList, StringComparison.Ordinal);   // a form for another's
        var own = await both.PostAsync($"/PayrollAdjustments/Approve/{ownId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token.Match(bothList).Groups[1].Value,
        }));
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Contains("لا يعتمد المدخل اليدوي ولا يرفضه من أدخله", WebUtility.HtmlDecode(await own.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(ManualInputApprovalStatus.PendingApproval,
            await host.WithDbAsync(db => db.Set<PayrollAdjustment>().AsNoTracking().Where(a => a.Id == ownId).Select(a => a.ApprovalStatus).SingleAsync()));

        // The approver, a second person: he approves the allowance, rejects the deduction with a reason (not without one).
        var approverList = await Pay054FrameworkHttpTests.PageAsync(approver, "/AllowanceRecords");
        Assert.Contains($"/AllowanceRecords/Approve/{allowance.Id}", approverList, StringComparison.Ordinal);
        Assert.Contains("data-hr-confirm=", approverList, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(approver, "/AllowanceRecords", $"/AllowanceRecords/Approve/{allowance.Id}", new())).StatusCode);
        var bare = await PostAsync(approver, "/DeductionRecords", $"/DeductionRecords/Reject/{deduction.Id}", new() { ["note"] = " " });
        Assert.Equal(HttpStatusCode.BadRequest, bare.StatusCode);
        Assert.Contains("ملاحظة الرفض إلزامية", WebUtility.HtmlDecode(await bare.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect,
            (await PostAsync(approver, "/DeductionRecords", $"/DeductionRecords/Reject/{deduction.Id}", new() { ["note"] = "لا سند للاسترداد" })).StatusCode);
        // A decision is not taken twice: 409.
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(approver, "/AllowanceRecords", $"/AllowanceRecords/Approve/{allowance.Id}", new())).StatusCode);

        var decided = await host.WithDbAsync(async db => (
            await db.Set<AllowanceRecord>().AsNoTracking().SingleAsync(),
            await db.Set<DeductionRecord>().AsNoTracking().SingleAsync()));
        Assert.Equal(ManualInputApprovalStatus.Approved, decided.Item1.ApprovalStatus);
        Assert.NotNull(decided.Item1.ApprovedBy);
        Assert.NotEqual(decided.Item1.CreatedBy, decided.Item1.ApprovedBy);
        Assert.Equal((ManualInputApprovalStatus.Rejected, "لا سند للاسترداد"), (decided.Item2.ApprovalStatus, decided.Item2.DecisionNote));
        var after = await Pay054FrameworkHttpTests.PageAsync(processor, "/DeductionRecords");
        Assert.Contains("مرفوض", after, StringComparison.Ordinal);
        Assert.Contains("لا سند للاسترداد", after, StringComparison.Ordinal);
        Assert.Contains("معتمد", await Pay054FrameworkHttpTests.PageAsync(processor, "/AllowanceRecords"), StringComparison.Ordinal);
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
