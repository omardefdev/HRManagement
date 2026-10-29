using System.Text.Json;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
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
public partial class RecruitmentReportsController(
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
        RecruitmentMetricsFilter filter, OrgAccessScope scope, int page, CancellationToken ct)
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
            Requisitions = await metrics.GetRequisitionTableAsync(filter, scope, page, ct: ct),
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
}
