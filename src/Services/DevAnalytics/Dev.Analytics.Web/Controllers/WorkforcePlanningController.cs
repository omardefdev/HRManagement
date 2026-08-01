using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using HR.Movements.Domain.Services;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class WorkforcePlanningController(AnalyticsDbContext db) : ControllerBase
{
    /// <summary>
    /// Gap analysis using AnalyticsEvents with OrgUnitId grain (FUN-006).
    /// Branch-scoped users cannot obtain tenant-wide / national totals.
    /// </summary>
    [HttpGet("gap-analysis")]
    public async Task<IActionResult> GapAnalysis(
        [FromQuery] Guid? orgUnitId,
        [FromQuery] string? plannedPositions,
        CancellationToken cancellationToken)
    {
        var claim = HrOrgScopeVisibility.GetScopeClaimResult(User);
        if (claim.Kind == OrgScopeClaimKind.InvalidClaim)
            return Forbid();

        var branchScoped = claim.Kind == OrgScopeClaimKind.Valid;
        if (branchScoped)
        {
            if (!BranchReportScopeRules.OrgMatchesBranch(orgUnitId, claim.RootOrgUnitId!.Value)
                && orgUnitId is null)
                orgUnitId = claim.RootOrgUnitId;
            if (!BranchReportScopeRules.OrgMatchesBranch(orgUnitId, claim.RootOrgUnitId!.Value))
                return Forbid();
        }

        if (branchScoped && !orgUnitId.HasValue)
            return Forbid();

        IQueryable<Dev.Analytics.Domain.Entities.AnalyticsEvent> createdQ = db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.EventName == "EmployeeCreated");
        IQueryable<Dev.Analytics.Domain.Entities.AnalyticsEvent> separatedQ = db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.EventName == "SeparationCompleted");

        if (orgUnitId.HasValue)
        {
            createdQ = createdQ.Where(e => e.OrgUnitId == orgUnitId);
            separatedQ = separatedQ.Where(e => e.OrgUnitId == orgUnitId);
        }
        else if (!BranchReportScopeRules.AllowsTenantWide(branchScoped))
        {
            return Forbid();
        }

        var created = await createdQ.CountAsync(cancellationToken);
        var separated = await separatedQ.CountAsync(cancellationToken);
        var actualHeadcount = Math.Max(0, created - separated);

        var result = WorkforceGapAnalysisRules.Analyze(plannedPositions, actualHeadcount);
        return Ok(new
        {
            orgUnitId,
            scopeGrain = orgUnitId.HasValue ? "Branch" : "Tenant",
            result.PlannedTotal,
            result.ActualTotal,
            result.Deficit,
            result.Surplus,
            result.PlannedBreakdown,
            analyticsSource = new { employeeCreatedEvents = created, separationCompletedEvents = separated }
        });
    }

    [HttpGet("headcount")]
    public async Task<IActionResult> Headcount([FromQuery] Guid? orgUnitId, CancellationToken cancellationToken)
    {
        var claim = HrOrgScopeVisibility.GetScopeClaimResult(User);
        if (claim.Kind == OrgScopeClaimKind.InvalidClaim)
            return Forbid();

        var branchScoped = claim.Kind == OrgScopeClaimKind.Valid;
        if (branchScoped)
        {
            orgUnitId ??= claim.RootOrgUnitId;
            if (!BranchReportScopeRules.OrgMatchesBranch(orgUnitId, claim.RootOrgUnitId!.Value))
                return Forbid();
        }

        var createdQ = db.AnalyticsEvents.AsNoTracking().Where(e => e.EventName == "EmployeeCreated");
        var separatedQ = db.AnalyticsEvents.AsNoTracking().Where(e => e.EventName == "SeparationCompleted");
        if (orgUnitId.HasValue)
        {
            createdQ = createdQ.Where(e => e.OrgUnitId == orgUnitId);
            separatedQ = separatedQ.Where(e => e.OrgUnitId == orgUnitId);
        }
        else if (branchScoped)
            return Forbid();

        var created = await createdQ.CountAsync(cancellationToken);
        var separated = await separatedQ.CountAsync(cancellationToken);
        return Ok(new
        {
            orgUnitId,
            scopeGrain = orgUnitId.HasValue ? "Branch" : "Tenant",
            activeEstimate = Math.Max(0, created - separated),
            created,
            separated
        });
    }
}
