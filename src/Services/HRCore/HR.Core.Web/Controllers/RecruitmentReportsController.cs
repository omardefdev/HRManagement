using System.Text;
using System.Text.Json;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.ExportJobs;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HR.Core.Web.ViewModels;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-032 (PLAN-32): «تقارير التوظيف» — recruitment KPIs, funnel, requisitions, overdue approvals and the exports. Every
/// figure comes from <see cref="RecruitmentMetricsService"/> under the caller's org scope (a scoped user sees only the units
/// in scope; an out-of-scope org-unit filter shows no data). Candidate names appear only for <c>HrCoreWrite</c>; never a
/// national id. Reports are read-only; the list screens they link to keep their own authorization (PLAN-25 deferred).
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class RecruitmentReportsController(
    RecruitmentMetricsService metrics,
    OrgScopeService orgScope,
    IAuthorizationService authorization,
    HRCoreDbContext db) : Controller
{
    private static readonly JsonSerializerOptions ChartJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IActionResult> Index([FromQuery] RecruitmentReportFilterForm form, CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
            return Forbid();

        var filter = form.ToFilter(metrics.DefaultFilter());
        var vm = await BuildAsync(filter, scope, Math.Max(1, form.Page), ct);
        return View(vm);
    }

    public async Task<IActionResult> Requisition(Guid id, CancellationToken ct)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (scope.IsDenied)
            return Forbid();

        var showNames = await CanSeeNamesAsync();
        var detail = await metrics.GetRequisitionDetailAsync(id, scope, showNames, ct);
        if (detail is null)
            return NotFound();

        return View(new RecruitmentRequisitionReportViewModel
        {
            Detail = detail,
            ShowNames = showNames,
            GeneratedAt = metrics.UtcNow,
            ChartJson = FunnelJson(detail.Funnel)
        });
    }

    /// <summary>Candidate names in drill lists and exports: HrCoreWrite only (decision 7).</summary>
    private async Task<bool> CanSeeNamesAsync() =>
        (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreWrite)).Succeeded;

    internal async Task<RecruitmentReportsViewModel> BuildAsync(
        RecruitmentMetricsFilter filter, OrgAccessScope scope, int page, CancellationToken ct,
        int requisitionPageSize = HRManagement.SharedKernel.ViewModels.SearchFilterBase.MaxPageSize)
    {
        // Sequential: one DbContext per request (the service memoises the shared cohort and hire loads).
        var funnel = await metrics.GetFunnelAsync(filter, scope, ct);
        var channels = await metrics.GetChannelMixAsync(filter, scope, ct);
        var vm = new RecruitmentReportsViewModel
        {
            Filter = filter,
            Funnel = funnel,
            TimeToFill = await metrics.GetTimeToFillAsync(filter, scope, ct),
            TimeToHire = await metrics.GetTimeToHireAsync(filter, scope, ct),
            StageTime = await metrics.GetStageTimeAsync(filter, scope, ct),
            Offers = await metrics.GetOfferOutcomesAsync(filter, scope, ct),
            Declines = await metrics.GetDeclineReasonsAsync(filter, scope, ct),
            Channels = channels,
            Appeals = await metrics.GetAppealsAsync(filter, scope, ct),
            Merit = await metrics.GetMeritOverridesAsync(filter, scope, ct),
            Vacancies = await metrics.GetOpenVacanciesAsync(filter, scope, ct),
            Approvals = await metrics.GetApprovalSlaAsync(filter, scope, ct),
            Competitions = await metrics.GetCompetitionsAsync(filter, scope, ct),
            Requisitions = await metrics.GetRequisitionTableAsync(filter, scope, page, requisitionPageSize, ct, maxPageSize: requisitionPageSize),
            OrgUnits = await OrgUnitOptionsAsync(scope, ct),
            CompetitionOptions = scope.IsUnrestricted ? await CompetitionOptionsAsync(ct) : [],
            IsScoped = scope.MustFilter,
            GeneratedAt = metrics.UtcNow,
            ChartJson = ChartJson(funnel, channels)
        };
        return vm;
    }

    private async Task<IReadOnlyList<ReportOption>> OrgUnitOptionsAsync(OrgAccessScope scope, CancellationToken ct)
    {
        var units = db.OrgUnits.AsNoTracking().AsQueryable();
        if (scope.MustFilter)
        {
            var allowed = scope.AllowedOrgUnitIds.ToArray();
            units = units.Where(u => allowed.Contains(u.Id));
        }
        return (await units
            .OrderBy(u => u.MaterializedPath).ThenBy(u => u.NameAr)
            .Take(500)
            .Select(u => new { u.Id, u.NameAr })
            .ToListAsync(ct))
            .Select(u => new ReportOption(u.Id.ToString(), u.NameAr))
            .ToList();
    }

    private async Task<IReadOnlyList<ReportOption>> CompetitionOptionsAsync(CancellationToken ct) =>
        (await db.RecruitmentCompetitions.AsNoTracking()
            .Where(c => c.AnnouncedAt != null)
            .OrderByDescending(c => c.AnnouncedAt)
            .Take(200)
            .Select(c => new { c.Id, c.Number, c.Title })
            .ToListAsync(ct))
            .Select(c => new ReportOption(c.Id.ToString(), c.Number + " — " + c.Title))
            .ToList();

    private static string ChartJson(FunnelResult funnel, ChannelMixResult channels) =>
        JsonSerializer.Serialize(new
        {
            funnel = funnel.Stages.Select(s => new { label = RecruitmentReportFormat.StageLabel(s.Stage), count = s.Count }),
            channels = channels.Rows.Select(r => new { label = RecruitmentReportFormat.Label(r.Channel), applied = r.Applied, hired = r.Hired })
        }, ChartJsonOptions);

    private static string FunnelJson(FunnelResult funnel) =>
        JsonSerializer.Serialize(new
        {
            funnel = funnel.Stages.Select(s => new { label = RecruitmentReportFormat.StageLabel(s.Stage), count = s.Count }),
            channels = Array.Empty<object>()
        }, ChartJsonOptions);

    // ------------------------------------------------------------------ exports
    // HCM-032 (PLAN-32): GET actions (the gateway maps POST under /ui/hr-core to HrCoreWrite, and read-only users must
    // export too — progress log K14), each audited as BulkExport and bounded by ExportQueryLimits. Excel and PDF are
    // aggregates; the applications CSV is synchronous up to MaxExportRows, else an export job. Never a national id; names
    // only for HrCoreWrite.
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
