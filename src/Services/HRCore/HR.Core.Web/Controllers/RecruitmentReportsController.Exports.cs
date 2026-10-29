using System.Text;
using HR.Core.Infrastructure.ExportJobs;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-032 (PLAN-32): exports of «تقارير التوظيف». GET actions (the gateway maps POST under <c>/ui/hr-core</c> to
/// HrCoreWrite, and read-only users must export too — progress log K14), each audited as <c>BulkExport</c> and bounded by
/// <see cref="ExportQueryLimits"/>. Excel and PDF are aggregates; the applications CSV is synchronous up to
/// <see cref="SearchFilterBase.MaxExportRows"/>, else an export job. Never a national id; names only for HrCoreWrite.
/// </summary>
public partial class RecruitmentReportsController
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public async Task<IActionResult> ExportExcel([FromQuery] RecruitmentReportFilterForm form, CancellationToken ct)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "RecruitmentReports/ExportExcel");
        var (result, vm, scopeLabel) = await BuildExportAsync(form, ct);
        if (result is not null)
            return result;
        return File(RecruitmentReportDocuments.Workbook(vm!, scopeLabel!), ExcelContentType,
            "recruitment-report-" + RecruitmentReportFormat.Date(vm!.GeneratedAt).Replace("-", "") + ".xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportPdf([FromQuery] RecruitmentReportFilterForm form, CancellationToken ct)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "RecruitmentReports/ExportPdf");
        var (result, vm, scopeLabel) = await BuildExportAsync(form, ct);
        if (result is not null)
            return result;
        return File(RecruitmentReportDocuments.Pdf(vm!, scopeLabel!), "application/pdf",
            "recruitment-report-" + RecruitmentReportFormat.Date(vm!.GeneratedAt).Replace("-", "") + ".pdf");
    }

    /// <summary>
    /// Applications of the period as CSV. Over <see cref="SearchFilterBase.MaxExportRows"/> rows (or <paramref name="async"/>)
    /// an export job is queued: scope and the names flag are captured at request time and re-checked at download.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportApplicationsCsv(
        [FromQuery] RecruitmentReportFilterForm form,
        [FromServices] IHRCoreExportJobService exportJobs,
        bool async = false,
        CancellationToken ct = default)
    {
        SecurityAudit.Log(HttpContext, SecurityAudit.EventTypes.BulkExport, "RecruitmentReports/ExportApplicationsCsv");
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
            return Forbid();

        var filter = form.ToFilter(metrics.DefaultFilter());
        var includeNames = await CanSeeNamesAsync();
        var ids = await metrics.CohortIdsAsync(filter, scope, ct);
        var (allowed, _) = ids is null ? (true, 0) : await ExportQueryLimits.EnsureWithinLimitAsync(ids, cancellationToken: ct);
        if (async || !allowed)
        {
            var (ok, jobId, error) = await exportJobs.EnqueueAsync(
                RecruitmentApplicationsCsv.TemplateCode,
                RecruitmentApplicationsExportFilter.Create(filter, scope, includeNames),
                User, ct);
            if (!ok)
                return BadRequest(error);
            return Accepted(ExportJobMessages.AcceptedBody(jobId!.Value));
        }

        var rows = await metrics.GetApplicationDetailsAsync(filter, scope, includeNames, SearchFilterBase.MaxExportRows, ct);
        // RFC 4180 line ends, whatever the host OS.
        var csv = new StringBuilder();
        csv.Append(RecruitmentApplicationsCsv.Header(includeNames)).Append("\r\n");
        foreach (var row in rows)
            csv.Append(RecruitmentApplicationsCsv.Line(row, includeNames)).Append("\r\n");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", RecruitmentApplicationsCsv.FileName(metrics.UtcNow));
    }

    /// <summary>The report model for an export: every requisition of the period (capped), not one page.</summary>
    private async Task<(IActionResult? Result, RecruitmentReportsViewModel? Model, string? ScopeLabel)> BuildExportAsync(
        RecruitmentReportFilterForm form, CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
            return (Forbid(), null, null);

        var filter = form.ToFilter(metrics.DefaultFilter());
        if (await metrics.RequisitionIdsAsync(filter, scope, ct) is { } requisitionIds)
        {
            var (allowed, _) = await ExportQueryLimits.EnsureWithinLimitAsync(requisitionIds, cancellationToken: ct);
            if (!allowed)
                return (BadRequest(ExportQueryLimits.OverLimitArabicMessage), null, null);
        }

        var vm = await BuildAsync(filter, scope, 1, ct, SearchFilterBase.MaxExportRows);
        return (null, vm, await ScopeLabelAsync(filter, scope, ct));
    }

    private async Task<string> ScopeLabelAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, CancellationToken ct)
    {
        var label = scope.MustFilter ? "وحدات نطاق المستخدم فقط" : "كل الوحدات";
        if (filter.OrgUnitId is { } unitId)
        {
            var name = await db.OrgUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.NameAr).FirstOrDefaultAsync(ct);
            label += " — الوحدة: " + (name ?? unitId.ToString());
        }
        if (filter.Channel is { } channel)
            label += " — القناة: " + RecruitmentReportFormat.Label(channel);
        if (filter.RequisitionId.HasValue)
            label += " — طلب شاغر واحد";
        if (filter.CompetitionId.HasValue)
            label += " — مسابقة واحدة";
        return label;
    }
}
