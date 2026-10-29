using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-006: HR-only candidate application legal lifecycle (anonymize/purge). HCM-030 (G4): the retention screen of the
/// admin's tenant — the due list (pair rule), a dry run now, «تنفيذ الإخفاء» when <c>Recruitment:Retention:Apply</c> is
/// true, and the run history.
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
[Route("CandidateLifecycle")]
public sealed class CandidateLifecycleController(
    HRCoreDbContext db,
    CandidateRetentionService retention,
    IServiceScopeFactory scopeFactory,
    IOptions<RecruitmentOptions> options,
    ITenantContext tenantContext) : Controller
{
    public const int RunHistorySize = 20;

    public record AnonymizeCitizenDto(Guid CitizenApplicationId);
    public record AnonymizeRecruitmentDto(Guid RecruitmentApplicationId);
    public record LegalHoldDto(Guid CitizenApplicationId, bool Hold);

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index([FromQuery] SearchFilterBase? filter, CancellationToken ct)
    {
        filter ??= new SearchFilterBase();
        filter.NormalizePaging();
        var tenantId = tenantContext.RequireTenantId();
        var now = DateTime.UtcNow;
        var (items, total) = await retention.GetDuePageAsync(tenantId, now, filter.Page, filter.PageSize, ct);
        var settings = options.Value;
        return View(new CandidateLifecycleIndexViewModel
        {
            Due = new PaginatedList<CandidateRetentionDueItem>(items, total, filter.Page, filter.PageSize),
            Counts = await retention.CountAsync(tenantId, now, ct),
            Runs = await retention.GetRunsAsync(tenantId, RunHistorySize, ct),
            RetentionMonths = Math.Max(1, settings.Retention.Months),
            ApplyEnabled = settings.Retention.Apply,
            JobEnabled = settings.Jobs.CandidateRetentionEnabled
        });
    }

    /// <summary>«تشغيل تجريبي»: counts the admin's tenant now and logs a dry run; nothing is changed.</summary>
    [HttpPost("dry-run")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DryRun(CancellationToken ct) => RunAsync(apply: false);

    /// <summary>«تنفيذ الإخفاء»: anonymizes the due pairs of the admin's tenant; refused unless Retention:Apply is true.</summary>
    [HttpPost("apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(CancellationToken ct)
    {
        if (!options.Value.Retention.Apply)
        {
            TempData["Error"] = "تنفيذ الإخفاء غير مفعّل في هذه البيئة (Recruitment:Retention:Apply = false). التشغيل التجريبي متاح.";
            return RedirectToAction(nameof(Index));
        }
        return await RunAsync(apply: true);
    }

    private async Task<IActionResult> RunAsync(bool apply)
    {
        var tenantId = tenantContext.RequireTenantId();
        var triggeredBy = WorkflowPanelBuilder.ParseUserId(User)?.ToString() ?? "User";
        // Not the request-abort token: a started run (each pair commits on its own) must finish and be logged even when the
        // browser or the gateway gives up, and the advisory lock must be released with a live token.
        var runs = await CandidateRetentionJob.RunWithLeaderLockAsync(
            scopeFactory, new CandidateRetentionRunRequest(DateTime.UtcNow, apply, triggeredBy, tenantId), CancellationToken.None);
        if (runs is null || runs.Count == 0)
        {
            TempData["Warning"] = "يوجد تشغيل آخر للاحتفاظ قيد التنفيذ حالياً، يرجى المحاولة لاحقاً.";
            return RedirectToAction(nameof(Index));
        }

        var run = runs[0];
        var counts = $"المستحق: {run.DueCount}، مستثنى للحجز القانوني: {run.SkippedLegalHold}، مستثنى لاستئناف معلّق: {run.SkippedAppeal}.";
        if (run.DryRun)
            TempData["Success"] = "تم التشغيل التجريبي دون تعديل أي سجل. " + counts;
        else if (run.Error is null)
            TempData["Success"] = $"تم إخفاء هوية {run.AnonymizedCount} طلباً. " + counts;
        else
            TempData["Warning"] = $"تم إخفاء هوية {run.AnonymizedCount} طلباً وتعذّر بعضها (انظر سجل التشغيل). " + counts;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("citizen/anonymize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AnonymizeCitizen([FromBody] AnonymizeCitizenDto dto, CancellationToken ct)
    {
        var app = await db.CitizenApplications
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == dto.CitizenApplicationId, ct);
        if (app is null) return NotFound();

        var (ok, error) = CandidateApplicationLegalLifecycleService.TryAnonymize(app);
        if (!ok) return Conflict(new { error });

        await db.SaveChangesAsync(ct);
        return Ok(new { app.Id, app.AnonymizedAt });
    }

    [HttpPost("recruitment/anonymize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AnonymizeRecruitment([FromBody] AnonymizeRecruitmentDto dto, CancellationToken ct)
    {
        var app = await db.RecruitmentApplications
            .Include(a => a.Requisition)
            .FirstOrDefaultAsync(a => a.Id == dto.RecruitmentApplicationId, ct);
        if (app is null) return NotFound();

        var (ok, error) = CandidateApplicationLegalLifecycleService.TryAnonymize(app);
        if (!ok) return Conflict(new { error });

        await db.SaveChangesAsync(ct);
        return Ok(new { app.Id, app.AnonymizedAt });
    }

    [HttpPost("citizen/legal-hold")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetCitizenLegalHold([FromBody] LegalHoldDto dto, CancellationToken ct)
    {
        var app = await db.CitizenApplications.FirstOrDefaultAsync(a => a.Id == dto.CitizenApplicationId, ct);
        if (app is null) return NotFound();

        var (ok, error) = CandidateApplicationLegalLifecycleService.SetLegalHold(app, dto.Hold, reason: null);
        if (!ok) return Conflict(new { error });

        await db.SaveChangesAsync(ct);
        return Ok(new { app.Id, app.LegalHold });
    }

    /// <summary>
    /// HCM-030 (G4): one page of the due list of the caller's tenant under the pair rule. <c>Id</c> is the recruitment
    /// application of a pair (<c>type</c> = RecruitmentApplication) or a legacy unlinked citizen row (CitizenApplication);
    /// <c>count</c> is the total over all pages.
    /// </summary>
    [HttpGet("retention/candidates")]
    public async Task<IActionResult> RetentionCandidates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = SearchFilterBase.DefaultPageSize,
        CancellationToken ct = default)
    {
        page = SearchFilterBase.ClampPage(page);
        pageSize = SearchFilterBase.ClampPageSize(pageSize);
        var (items, total) = await retention.GetDuePageAsync(tenantContext.RequireTenantId(), DateTime.UtcNow, page, pageSize, ct);
        var candidates = items
            .Select(i => new
            {
                i.Id,
                ReferenceNumber = i.Reference,
                type = i.IsLegacyCitizen ? nameof(CitizenApplication) : nameof(RecruitmentApplication),
                requisitionNumber = i.RequisitionNumber,
                triggerDate = i.TriggerDate,
                partiallyAnonymized = i.PartiallyAnonymized
            })
            .ToList();
        return Ok(new { count = total, page, pageSize, candidates });
    }
}
