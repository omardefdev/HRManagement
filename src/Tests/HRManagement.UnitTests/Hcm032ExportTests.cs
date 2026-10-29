using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.ExportJobs;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HR.Core.Web.Controllers;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Exports;
using HRManagement.TestSupport;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using static HRManagement.UnitTests.Hcm032MetricsFixture;
using Ctl = HRManagement.UnitTests.Hcm032RecruitmentReportsControllerTests;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-032 (PLAN-32, AC-HCM-32c): recruitment report exports — no national id (not even masked) in any export, candidate
/// names only for HrCoreWrite (also through the export job, re-checked at download), branch scope, Gregorian dates under
/// ar-SA, audit and row caps.
/// </summary>
public class Hcm032ExportTests
{
    private static readonly RecruitmentReportFilterForm PeriodForm = new() { From = "2026-01-01", To = "2026-11-01" };

    /// <summary>AC-26 pattern (9–11 digit run) plus masked runs; and the fixture's own national id prefix.</summary>
    internal static void AssertNoNationalId(string text)
    {
        Assert.DoesNotMatch(new Regex(@"(?<![0-9])[0-9]{9,11}(?![0-9])"), text);
        Assert.DoesNotMatch(new Regex(@"\*{3,}[0-9]"), text);
        Assert.DoesNotContain(NationalIdPrefix + "0", text);
    }

    private static string WorkbookText(byte[] bytes)
    {
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var text = new StringBuilder();
        foreach (var sheet in workbook.Worksheets)
        {
            text.AppendLine("#" + sheet.Name);
            foreach (var cell in sheet.CellsUsed())
                text.AppendLine(cell.GetFormattedString());
        }
        return text.ToString();
    }

    private static string FileText(IActionResult result) =>
        Encoding.UTF8.GetString(Assert.IsType<FileContentResult>(result).FileContents);

    private static IHRCoreExportJobService NoJobs() => Mock.Of<IHRCoreExportJobService>();

    [Fact]
    public async Task Excel_export_has_the_sheets_and_provenance_and_no_national_id_or_names()
    {
        await using var db = await Ctl.SeededAsync();

        var file = Assert.IsType<FileContentResult>(await Ctl.Controller(db, Ctl.Hr()).ExportExcel(PeriodForm, CancellationToken.None));
        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        var text = WorkbookText(file.FileContents);

        Assert.Equal(
            ["المؤشرات", "القمع", "طلبات الشاغر", "الاعتمادات المتأخرة", "العروض", "الاستئنافات", "الشواغر المفتوحة", "المسابقات"],
            workbook.Worksheets.Select(s => s.Name));
        foreach (var sheet in workbook.Worksheets)
        {
            Assert.Equal("الفترة (UTC)", sheet.Cell(2, 1).GetString());
            Assert.Equal("2026-01-01 — 2026-10-31", sheet.Cell(2, 2).GetString());
            Assert.Equal("2026-10-29 12:00 UTC", sheet.Cell(4, 2).GetString());
            Assert.Equal(RecruitmentMetricsService.DefinitionsVersion, sheet.Cell(5, 2).GetString());
            Assert.True(sheet.RightToLeft);
        }
        AssertNoNationalId(text);
        Assert.DoesNotContain(CandidateNamePrefix, text);
        // Funnel counts are numeric cells (invariant), the requisition table lists every active requisition.
        Assert.Contains("REQ-H32-6", text);
        Assert.Equal(27d, workbook.Worksheet("القمع").Column(1).CellsUsed().First(c => c.GetString() == "التقديم").CellRight().GetDouble());
    }

    [Fact]
    public async Task Pdf_export_is_generated_for_the_scope()
    {
        await using var db = await Ctl.SeededAsync();

        var file = Assert.IsType<FileContentResult>(await Ctl.Controller(db, Ctl.BranchManager()).ExportPdf(PeriodForm, CancellationToken.None));

        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(file.FileContents, 0, 4));
        Assert.Equal("recruitment-report-20261029.pdf", file.FileDownloadName);
    }

    [Fact]
    public async Task Csv_export_has_names_for_hr_core_write_and_never_a_national_id()
    {
        await using var db = await Ctl.SeededAsync();

        var csv = FileText(await Ctl.Controller(db, Ctl.Hr()).ExportApplicationsCsv(PeriodForm, NoJobs(), ct: CancellationToken.None));
        var lines = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(RecruitmentApplicationsCsv.Header(true), lines[0]);
        Assert.Equal(28, lines.Length);
        Assert.Contains(CandidateNamePrefix + "B1", csv);
        AssertNoNationalId(csv);
    }

    [Fact]
    public async Task Csv_export_of_a_read_only_user_has_no_names()
    {
        await using var db = await Ctl.SeededAsync();

        var csv = FileText(await Ctl.Controller(db, Ctl.Executive()).ExportApplicationsCsv(PeriodForm, NoJobs(), ct: CancellationToken.None));
        var lines = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(RecruitmentApplicationsCsv.Header(false), lines[0]);
        Assert.DoesNotContain("CandidateName", lines[0]);
        Assert.DoesNotContain(CandidateNamePrefix, csv);
        Assert.All(lines.Skip(1), l => Assert.Matches("^(PUB|INT|HR|LEG)-[0-9A-F]{8},", l));
        AssertNoNationalId(csv);
    }

    [Fact]
    public async Task Branch_exports_contain_only_the_branch()
    {
        await using var db = await Ctl.SeededAsync();
        var controller = Ctl.Controller(db, Ctl.BranchManager());

        var csv = FileText(await controller.ExportApplicationsCsv(PeriodForm, NoJobs(), ct: CancellationToken.None));
        var excel = WorkbookText(Assert.IsType<FileContentResult>(
            await Ctl.Controller(db, Ctl.BranchManager()).ExportExcel(PeriodForm, CancellationToken.None)).FileContents);

        Assert.Equal(13, csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("REQ-H32-1", csv);
        Assert.DoesNotContain("REQ-H32-2", csv);
        Assert.Contains("REQ-H32-3", excel);
        Assert.DoesNotContain("REQ-H32-1", excel);
        Assert.DoesNotContain("REQ-H32-5", excel);
        Assert.DoesNotContain("#المسابقات", excel);
        Assert.Contains("وحدات نطاق المستخدم فقط", excel);
    }

    [Fact]
    public async Task Out_of_scope_export_is_empty_and_denied_scope_is_forbidden()
    {
        await using var db = await Ctl.SeededAsync();
        var form = new RecruitmentReportFilterForm { From = "2026-01-01", To = "2026-11-01", OrgUnitId = UnitHq };

        var csv = FileText(await Ctl.Controller(db, Ctl.BranchManager()).ExportApplicationsCsv(form, NoJobs(), ct: CancellationToken.None));

        Assert.Single(csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.IsType<ForbidResult>(await Ctl.Controller(db, Ctl.BranchManager("bad")).ExportExcel(PeriodForm, CancellationToken.None));
        Assert.IsType<ForbidResult>(await Ctl.Controller(db, Ctl.BranchManager("bad")).ExportApplicationsCsv(PeriodForm, NoJobs(), ct: CancellationToken.None));
    }

    [Fact]
    public async Task Exports_print_gregorian_dates_under_ar_sa()
    {
        await using var db = await Ctl.SeededAsync();
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");
            var csv = FileText(await Ctl.Controller(db, Ctl.Hr()).ExportApplicationsCsv(PeriodForm, NoJobs(), ct: CancellationToken.None));
            var excel = WorkbookText(Assert.IsType<FileContentResult>(
                await Ctl.Controller(db, Ctl.Hr()).ExportExcel(PeriodForm, CancellationToken.None)).FileContents);

            Assert.Contains(",2026-09-01,", csv);
            Assert.DoesNotContain("1448", csv);
            Assert.Contains("2026-02-03", excel);
            Assert.DoesNotContain("1447", excel);
            Assert.DoesNotContain("1448", excel);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }
    }

    // ------------------------------------------------------------------ export job path

    private sealed class MemoryArtifactStore : IExportArtifactStore
    {
        private readonly Dictionary<string, byte[]> _files = new();

        public async Task<ExportArtifactRef> SaveAsync(Guid tenantId, Guid jobId, string fileName, string contentType, Stream content,
            long contentLength, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var key = jobId.ToString("N");
            _files[key] = buffer.ToArray();
            return new ExportArtifactRef(Guid.NewGuid(), key, contentType, fileName);
        }

        public Task<Stream> OpenReadAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(_files[reference.RelativePath]));

        public Task DeleteAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static HRCoreExportJobService JobService(HRCoreDbContext db, MemoryArtifactStore store) =>
        new(db, Mock.Of<IPublishEndpoint>(), store, Microsoft.Extensions.Options.Options.Create(new ExportJobOptions { ChunkSize = 50 }),
            new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()))),
            tenantContext: null, recruitmentMetrics: CreateService(db));

    private static ClaimsPrincipal WithRoles(ClaimsPrincipal user, params string[] roles) =>
        new(new ClaimsIdentity(
            user.FindAll(c => c.Type != ClaimTypes.Role).Select(c => new Claim(c.Type, c.Value))
                .Concat(roles.Select(r => new Claim(ClaimTypes.Role, r))), "test"));

    private static async Task<string> RunJobAsync(HRCoreExportJobService jobs, ClaimsPrincipal user, OrgAccessScope scope, bool includeNames, HRCoreDbContext db)
    {
        var (ok, jobId, error) = await jobs.EnqueueAsync(RecruitmentApplicationsCsv.TemplateCode,
            RecruitmentApplicationsExportFilter.Create(Period, scope, includeNames), user);
        Assert.True(ok, error);
        await jobs.ProcessAsync(jobId!.Value);
        var job = await jobs.GetAsync(jobId.Value);
        Assert.Equal(ExportJobStatus.Completed, job!.Status);
        Assert.Equal(RecruitmentApplicationsCsv.TemplateCode, job.TemplateCode);
        return jobId.Value.ToString();
    }

    [Fact]
    public async Task Export_job_writes_names_only_when_captured_and_rechecks_them_at_download()
    {
        await using var db = await Ctl.SeededAsync();
        var store = new MemoryArtifactStore();
        var jobs = JobService(db, store);
        var hr = Ctl.Hr();

        var jobId = Guid.Parse(await RunJobAsync(jobs, hr, OrgAccessScope.Unrestricted(), includeNames: true, db));
        var (ok, stream, fileName, _, error) = await jobs.OpenDownloadAsync(jobId, hr);
        Assert.True(ok, error);
        var csv = await new StreamReader(stream!).ReadToEndAsync();
        Assert.Contains(CandidateNamePrefix + "A1", csv);
        Assert.StartsWith("recruitment-applications-", fileName);
        Assert.Equal(28, csv.TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        AssertNoNationalId(csv);

        // Same user, HrCoreWrite lost since the request: the names file is refused.
        var demoted = WithRoles(hr, HrManagementRoles.HrCoreExecutive);
        var (allowed, _, _, _, refusal) = await jobs.OpenDownloadAsync(jobId, demoted);
        Assert.False(allowed);
        Assert.Equal("غير مصرح بتحميل هذا التصدير.", refusal);
    }

    [Fact]
    public async Task Export_job_of_a_read_only_user_has_no_names_and_keeps_the_branch_scope()
    {
        await using var db = await Ctl.SeededAsync();
        var store = new MemoryArtifactStore();
        var jobs = JobService(db, store);
        var branch = Ctl.BranchManager();

        var jobId = Guid.Parse(await RunJobAsync(jobs, branch, OrgAccessScope.Restricted(new HashSet<Guid> { UnitBranch }), includeNames: false, db));
        var (ok, stream, _, _, error) = await jobs.OpenDownloadAsync(jobId, branch);
        Assert.True(ok, error);
        var csv = await new StreamReader(stream!).ReadToEndAsync();
        var lines = csv.TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(RecruitmentApplicationsCsv.Header(false), lines[0].TrimEnd('\r'));
        Assert.Equal(13, lines.Length);
        Assert.DoesNotContain(CandidateNamePrefix, csv);
        Assert.DoesNotContain("REQ-H32-1", csv);
    }

    [Fact]
    public async Task Unrestricted_export_job_is_refused_to_a_user_who_became_scoped()
    {
        await using var db = await Ctl.SeededAsync();
        var jobs = JobService(db, new MemoryArtifactStore());
        var hr = Ctl.Hr();

        var jobId = Guid.Parse(await RunJobAsync(jobs, hr, OrgAccessScope.Unrestricted(), includeNames: false, db));
        // Same user id, now a branch manager scoped to the branch.
        var scoped = new ClaimsPrincipal(new ClaimsIdentity(
            WithRoles(hr, HrManagementRoles.BranchManager).Claims.Select(c => new Claim(c.Type, c.Value))
                .Append(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, UnitBranch.ToString())), "test"));

        Assert.False((await jobs.OpenDownloadAsync(jobId, scoped)).Ok);
    }

    [Fact]
    public async Task Over_the_row_cap_the_csv_is_queued_as_a_job_with_the_captured_flags()
    {
        await using var db = await Ctl.SeededAsync();
        var jobs = new Mock<IHRCoreExportJobService>();
        object? captured = null;
        jobs.Setup(j => j.EnqueueAsync(RecruitmentApplicationsCsv.TemplateCode, It.IsAny<object>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, ClaimsPrincipal, CancellationToken>((_, filter, _, _) => captured = filter)
            .ReturnsAsync((true, Guid.NewGuid(), (string?)null));

        var result = await Ctl.Controller(db, Ctl.Executive()).ExportApplicationsCsv(PeriodForm, jobs.Object, async: true, ct: CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var filter = Assert.IsType<RecruitmentApplicationsExportFilter>(captured);
        Assert.False(filter.IncludeNames);
        Assert.Null(filter.AllowedOrgUnitIds);
        Assert.Equal(("2026-01-01", "2026-11-01"), (filter.From, filter.To));
    }

    // ------------------------------------------------------------------ rules

    [Fact]
    public async Task Name_access_mirror_matches_the_hr_core_write_policy_for_every_role()
    {
        var authorization = Ctl.Authorization();
        var roles = typeof(HrManagementRoles).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        Assert.NotEmpty(roles);
        foreach (var role in roles)
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
            var policy = (await authorization.AuthorizeAsync(user, HrManagementPolicies.HrCoreWrite)).Succeeded;
            Assert.Equal(policy, RecruitmentReportAccess.CanSeeCandidateNames(user));
        }
    }

    [Fact]
    public void Every_export_action_is_audited_bounded_and_reachable_by_get()
    {
        var source = System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "Controllers", "RecruitmentReportsController.cs"));
        foreach (var action in new[] { "ExportExcel", "ExportPdf", "ExportApplicationsCsv" })
        {
            Assert.Contains($"SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, \"RecruitmentReports/{action}\")", source);
            var method = typeof(RecruitmentReportsController).GetMethod(action)!;
            Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
        }
        Assert.Equal(2, Regex.Matches(source, "ExportQueryLimits.EnsureWithinLimitAsync").Count);
    }

    [Fact]
    public void Report_and_export_code_never_reads_a_national_id()
    {
        var files = new[]
        {
            Path.Combine(RepoPaths.Root, "src", "Services", "HRCore", "HR.Core.Infrastructure", "Services", "Recruitment", "Analytics"),
        }
        .SelectMany(d => Directory.GetFiles(d, "*.cs"))
        .Concat(
        [
            RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "Controllers", "RecruitmentReportsController.cs"),
            RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "Services", "RecruitmentReportDocuments.cs"),
            RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "ViewModels", "RecruitmentReportsViewModels.cs")
        ]);
        foreach (var file in files)
            Assert.DoesNotContain("NationalId", System.IO.File.ReadAllText(file));
    }
}
