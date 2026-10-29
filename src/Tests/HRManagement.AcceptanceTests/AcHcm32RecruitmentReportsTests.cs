using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-32a..d (PLAN-32): «تقارير التوظيف» through the gateway with the agency A personas — central-a (HR, HrCoreWrite,
/// unrestricted), br1-mgr (branch manager, RIF scope) and a-executive-reader (read-only). Fixtures come from
/// <c>AcceptanceRecruitmentReportsPatch</c>: REQ-2026-A-AC32-1 (RIF, closed by a hire: funnel 6,4,3,3,3,3,2,1,1, time to fill
/// 37 days), REQ-2026-A-AC32-2 (Aleppo) and REQ-2026-A-AC32-3 (RIF, approval pending since 2026-09-01, past the 48 h SLA).
/// Reports are read-only GETs: no fixture is changed, so the journey can be repeated.
/// </summary>
[RequiredAcceptance]
public class AcHcm32RecruitmentReportsTests
{
    private const string Ac = "AC-HCM-32";
    private const string Period = "From=2026-08-01&To=2026-10-01";
    private static readonly string Req1 = AcceptanceSeedIds.RecruitmentReportsRequisition1Id.ToString("D");
    private static readonly string Req2 = AcceptanceSeedIds.RecruitmentReportsRequisition2Id.ToString("D");
    private static readonly string Br2Unit = AcceptanceSeedIds.Br2OrgUnitId.ToString("D");

    // ── AC-HCM-32a: funnel and time-to-fill equal the hand-computed seed values ─────────────────────────────────────

    [SkippableFact]
    public async Task Ac32_a_funnel_and_time_to_fill_match_the_seed()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("32a", "AC-HCM-32a funnel and time-to-fill of REQ-2026-A-AC32-1 equal the hand calculation");
        await RequireFixturesAsync(s);
        s.Recorder.WriteSteps("""
            32a. central-a opens «تقارير التوظيف» for REQ-2026-A-AC32-1. Expected (hand calculation in AcceptanceRecruitmentReportsPatch):
                 funnel Applied 6 · Screened 4 · Shortlisted 3 · Interviewed 3 · Assessed 3 · Ranked 3 · OfferIssued 2 · OfferAccepted 1 ·
                 Hired 1; time to fill = 2026-08-04 (approval) → 2026-09-10 (Hired event) = 37 days (n = 1, no fallback).
            """);

        var page = await s.Central.GetAsync("32a-requisition", $"ui/hr-core/RecruitmentReports/Requisition/{Req1}");
        Assert.Equal([6, 4, 3, 3, 3, 3, 2, 1, 1], TableCounts(page, "requisition-funnel-table"));
        Assert.Contains("زمن الملء 37", page);

        var index = await s.Central.GetAsync("32a-index", $"ui/hr-core/RecruitmentReports?RequisitionId={Req1}&{Period}");
        Assert.Equal([6, 4, 3, 3, 3, 3, 2, 1, 1], TableCounts(index, "funnel-table", columnIndex: 0));
        Assert.Matches(new Regex(@"زمن الملء \(أيام\)</h3>\s*<p class=""h4 mb-1"">37 <small"), index);
        Assert.Matches(new Regex(@"P90: 37 · n = 1"), index);

        s.Recorder.WriteDbChecks(new { requisition = AcceptanceSeedIds.RecruitmentReportsRequisition1Number, funnel = "6,4,3,3,3,3,2,1,1", timeToFillDays = 37 });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-32b: the branch manager sees only its unit; another unit's report has no data ────────────────────────

    [SkippableFact]
    public async Task Ac32_b_branch_manager_sees_only_its_unit()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("32b", "AC-HCM-32b br1-mgr sees RIF requisitions only; the Aleppo unit and requisition give no data");
        await RequireFixturesAsync(s);
        s.Recorder.WriteSteps("""
            32b. br1-mgr (RIF scope) opens the report for Aug–Sep 2026: REQ-2026-A-AC32-1 (RIF) is listed, REQ-2026-A-AC32-2 (Aleppo) is not.
                 Negative: the same report filtered on the Aleppo unit shows no data (0 applications, no requisition), and the
                 Aleppo requisition page answers 404.
            """);

        var own = await s.Br1.GetAsync("32b-own", $"ui/hr-core/RecruitmentReports?{Period}");
        Assert.Contains(AcceptanceSeedIds.RecruitmentReportsRequisition1Number, own);
        Assert.DoesNotContain(AcceptanceSeedIds.RecruitmentReportsRequisition2Number, own);
        Assert.DoesNotContain("id=\"competitions-table\"", own);

        var other = await s.Br1.GetAsync("32b-other-unit", $"ui/hr-core/RecruitmentReports?OrgUnitId={Br2Unit}&{Period}");
        Assert.Matches(new Regex(@"الطلبات في الفترة</h3>\s*<p class=""h4 mb-1"">0</p>"), other);
        Assert.DoesNotContain("REQ-2026-A-AC32-", other);

        var (status, _, _) = await s.Br1.GetRawAsync("32b-other-requisition", $"ui/hr-core/RecruitmentReports/Requisition/{Req2}");
        Assert.Equal(HttpStatusCode.NotFound, status);

        s.Recorder.WriteDbChecks(new { visible = AcceptanceSeedIds.RecruitmentReportsRequisition1Number, hidden = AcceptanceSeedIds.RecruitmentReportsRequisition2Number });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-32c: the Excel export has no national id; the read-only export has no names ──────────────────────────

    [SkippableFact]
    public async Task Ac32_c_exports_have_no_national_id_and_no_names_for_read_only()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("32c", "AC-HCM-32c exports: no national id in Excel; read-only CSV without names");
        await RequireFixturesAsync(s);
        var executive = await PersonaUiAsync(s, "a-executive-reader");
        s.Recorder.WriteSteps("""
            32c. central-a downloads the Excel report (Aug–Sep 2026): no AC-HCM-32 national id (320320000NN), no 9–11 digit run, not
                 even masked. Negative: a-executive-reader (read-only) downloads the applications CSV: no CandidateName column and no
                 applicant name, only references; central-a's CSV (HrCoreWrite) carries the names (control).
            """);

        var (excelStatus, excelType, excel) = await s.Central.GetBytesAsync("32c-excel", $"ui/hr-core/RecruitmentReports/ExportExcel?{Period}");
        Assert.Equal(HttpStatusCode.OK, excelStatus);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excelType);
        var excelText = WorkbookText(excel);
        Assert.Contains(AcceptanceSeedIds.RecruitmentReportsRequisition1Number, excelText);
        Assert.DoesNotContain(AcceptanceSeedIds.RecruitmentReportsNationalIdPrefix, excelText);
        Assert.DoesNotMatch(new Regex(@"(?<![0-9])[0-9]{9,11}(?![0-9])"), excelText);
        Assert.DoesNotMatch(new Regex(@"\*{3,}[0-9]"), excelText);
        Assert.DoesNotContain(AcceptanceSeedIds.RecruitmentReportsApplicantPrefix, excelText);

        var (csvStatus, _, csvBytes) = await executive.GetBytesAsync("32c-csv-read-only", $"ui/hr-core/RecruitmentReports/ExportApplicationsCsv?{Period}");
        Assert.Equal(HttpStatusCode.OK, csvStatus);
        var csv = Encoding.UTF8.GetString(csvBytes).TrimStart('﻿');
        Assert.StartsWith("Reference,Channel,", csv);
        Assert.DoesNotContain("CandidateName", csv);
        Assert.DoesNotContain(AcceptanceSeedIds.RecruitmentReportsApplicantPrefix, csv);
        Assert.DoesNotContain(AcceptanceSeedIds.RecruitmentReportsNationalIdPrefix, csv);
        Assert.Contains(AcceptanceSeedIds.RecruitmentReportsRequisition1Number, csv);

        var (_, _, hrCsvBytes) = await s.Central.GetBytesAsync("32c-csv-hr", $"ui/hr-core/RecruitmentReports/ExportApplicationsCsv?{Period}");
        var hrCsv = Encoding.UTF8.GetString(hrCsvBytes).TrimStart('﻿');
        Assert.StartsWith("Reference,CandidateName,", hrCsv);
        Assert.Contains(AcceptanceSeedIds.RecruitmentReportsApplicantPrefix + " 1", hrCsv);
        Assert.DoesNotContain(AcceptanceSeedIds.RecruitmentReportsNationalIdPrefix, hrCsv);

        s.Recorder.WriteDbChecks(new { excelBytes = excel.Length, readOnlyCsvLines = csv.Split('\n').Length, hrCsvLines = hrCsv.Split('\n').Length });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── AC-HCM-32d: an approval past its SLA is listed ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ac32_d_overdue_approval_is_listed()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync("32d", "AC-HCM-32d the approval of REQ-2026-A-AC32-3 (pending since 2026-09-01) is listed as overdue");
        await RequireFixturesAsync(s);
        AcceptanceGate.RequireData(s.Recorder,
            await Scalar($"SELECT COUNT(*) FROM \"WorkflowInstances\" WHERE \"Id\"='{AcceptanceSeedIds.RecruitmentReportsWorkflowInstanceId:D}' AND \"Status\"='InProgress';") == 1,
            "مثيل الاعتماد الخاص بـ AC-HCM-32d غير مُهيّأ أو اتُّخذ عليه إجراء — شغّل scripts/acceptance-reset.ps1",
            "The AC-HCM-32d approval instance is not seeded or was acted on — run scripts/acceptance-reset.ps1.");
        s.Recorder.WriteSteps("""
            32d. central-a opens the report: «الاعتمادات المتأخرة» lists REQ-2026-A-AC32-3 (RECRUITMENT-REQUISITION-APPROVAL, step 1, waiting
                 since 2026-09-01 09:00 UTC) with its age above the 48 h SLA (default policy value).
            """);

        var index = await s.Central.GetAsync("32d-index", "ui/hr-core/RecruitmentReports");
        var table = Section(index, "overdue-approvals-table");
        Assert.Contains(AcceptanceSeedIds.RecruitmentReportsRequisition3Number, table);
        Assert.Contains("2026-09-01 09:00 UTC", table);
        Assert.Contains("/RecruitmentRequisitions/Details/" + AcceptanceSeedIds.RecruitmentReportsRequisition3Id.ToString("D"), table);

        s.Recorder.WriteDbChecks(new { overdue = AcceptanceSeedIds.RecruitmentReportsRequisition3Number });
        s.Recorder.FlushTiming();
        s.Recorder.WriteResult("Pass");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task RequireFixturesAsync(Session s) =>
        AcceptanceGate.RequireData(s.Recorder,
            await Scalar("SELECT COUNT(*) FROM \"RecruitmentRequisitions\" WHERE \"RequisitionNumber\" LIKE 'REQ-2026-A-AC32-%';") == 3,
            "بيانات تهيئة AC-HCM-32 غير مُهيّأة — أعد بناء hr-core-web من هذا الفرع (AcceptanceRecruitmentReportsPatch)",
            "The AC-HCM-32 fixtures are not seeded — rebuild hr-core-web from this branch (AcceptanceRecruitmentReportsPatch).");

    /// <summary>The count column of each body row of the table with <paramref name="tableId"/>.</summary>
    private static List<int> TableCounts(string html, string tableId, int columnIndex = 0)
    {
        var table = Section(html, tableId);
        return Regex.Matches(table, @"<tr><th scope=""row"">[^<]*</th>((?:<td>[^<]*</td>)+)</tr>")
            .Select(m => Regex.Matches(m.Groups[1].Value, "<td>([^<]*)</td>")[columnIndex].Groups[1].Value)
            .Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }

    private static string Section(string html, string tableId)
    {
        var start = html.IndexOf($"id=\"{tableId}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Table {tableId} not found.");
        var end = html.IndexOf("</table>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    /// <summary>Every text and value of the workbook (shared strings and sheet cells).</summary>
    private static string WorkbookText(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        var text = new StringBuilder();
        foreach (var entry in zip.Entries.Where(e => e.FullName == "xl/sharedStrings.xml" || e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            var xml = reader.ReadToEnd();
            // Elements may carry a namespace prefix (<x:t>, <x:v>).
            foreach (Match m in Regex.Matches(xml, @"<(?:\w+:)?(?:t|v)(?: [^>]*)?>([^<]*)</(?:\w+:)?(?:t|v)>"))
                text.AppendLine(WebUtility.HtmlDecode(m.Groups[1].Value));
        }
        return text.ToString();
    }

    private sealed record Session(EvidenceRecorder Recorder, string Tag, string CorrelationId, Ui Central, Ui Br1);

    private static async Task<Session> StartAsync(string tag, string scenario)
    {
        var central = AcceptanceHarness.RequirePersona("central-a");
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(central);
        var recorder = new EvidenceRecorder(Ac, correlationId);
        recorder.WriteMetadata(scenario, $"{central.Id}+{br1.Id}+a-executive-reader");
        return new Session(recorder, tag, correlationId,
            new Ui(await CreateClientAsync(central, correlationId), recorder, central.Id, tag),
            new Ui(await CreateClientAsync(br1, correlationId), recorder, br1.Id, tag));
    }

    private static async Task<Ui> PersonaUiAsync(Session s, string personaId)
    {
        var persona = AcceptanceHarness.RequirePersona(personaId);
        return new Ui(await CreateClientAsync(persona, s.CorrelationId), s.Recorder, persona.Id, s.Tag);
    }

    private static async Task<HttpClient> CreateClientAsync(HRManagement.E2E.E2ePersona persona, string correlationId)
    {
        var token = await AcceptanceHarness.FetchPasswordGrantTokenAsync(persona, correlationId);
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(HRManagement.E2E.E2eEnv.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(2)
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        return http;
    }

    private static Task<long> Scalar(string sql) => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync("hr_core_db", sql);

    /// <summary>Gateway UI client for one persona (GET only: the reports and their exports are read-only).</summary>
    private sealed class Ui(HttpClient http, EvidenceRecorder recorder, string persona, string tag)
    {
        public async Task<string> GetAsync(string step, string path)
        {
            using var resp = await http.GetAsync(path);
            var body = await resp.Content.ReadAsStringAsync();
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode, body.Length > 600 ? body[..600] : body);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"GET {path} as {persona} returned {(int)resp.StatusCode}.");
            return WebUtility.HtmlDecode(body);
        }

        public async Task<(HttpStatusCode Status, string? ContentType, byte[] Body)> GetBytesAsync(string step, string path)
        {
            using var resp = await http.GetAsync(path);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            recorder.CaptureHttp($"{tag}-{step}-{persona}", "GET", "/" + path, (int)resp.StatusCode,
                new { contentType = resp.Content.Headers.ContentType?.MediaType, length = bytes.Length });
            return (resp.StatusCode, resp.Content.Headers.ContentType?.MediaType, bytes);
        }

        public async Task<(HttpStatusCode Status, string? ContentType, string Head)> GetRawAsync(string step, string path)
        {
            var (status, type, bytes) = await GetBytesAsync(step, path);
            return (status, type, bytes.Length >= 4 ? Encoding.ASCII.GetString(bytes, 0, 4) : string.Empty);
        }
    }
}
