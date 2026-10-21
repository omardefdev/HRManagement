using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HR.Core.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Route("api/establishment")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class EstablishmentPlanningApiController(
    HRCoreDbContext db,
    IEstablishmentGapAnalysisService gapAnalysisService,
    IEstablishmentFinanceReconciliationService reconciliationService,
    IFinanceBudgetImportService financeImportService,
    IJobCatalogImportService jobCatalogImportService,
    EstablishmentChangeLifecycleService changeLifecycle,
    EstablishmentScopeService establishmentScope,
    OrgScopeService orgScope) : ControllerBase
{
    [HttpGet("gap-analysis")]
    public async Task<IActionResult> GapAnalysis(
        [FromQuery] Guid orgUnitId,
        [FromQuery] int fiscalYear,
        [FromQuery] DateTime? asOf,
        CancellationToken ct)
    {
        var orgAccess = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
        if (orgAccess.IsDenied) return Forbid();
        if (orgAccess.MustFilter && !orgAccess.CanAccess(orgUnitId)) return Forbid();

        var plan = await db.WorkforcePlans.AsNoTracking().Include(p => p.Lines)
            .Where(p => p.OrgUnitId == orgUnitId && p.FiscalYear == fiscalYear && p.Status == "approved")
            .OrderByDescending(p => p.ApprovedAt)
            .FirstOrDefaultAsync(ct);

        var summary = plan is not null
            ? await gapAnalysisService.AnalyzeOrgAsync(orgUnitId, fiscalYear, plan.Lines.ToList(), asOf, ct)
            : await gapAnalysisService.AnalyzeOrgAsync(orgUnitId, fiscalYear, [], asOf, ct);

        return Ok(new { orgUnitId, fiscalYear, asOf = asOf?.ToUniversalTime(), source = "FundedPosition", summary });
    }

    [HttpGet("workforce-plans/{id:guid}/gap")]
    public async Task<IActionResult> PlanGap(Guid id, [FromQuery] DateTime? asOf, CancellationToken ct)
    {
        var plan = await db.WorkforcePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();
        if (plan.OrgUnitId is Guid orgUnitId)
        {
            var scope = await establishmentScope.GetScopeAsync(User, ct);
            if (scope.IsDenied) return Forbid();
            if (scope.MustFilter && !scope.CanAccess(orgUnitId)) return Forbid();
        }

        var summary = await gapAnalysisService.AnalyzePlanAsync(id, asOf, ct);
        if (summary is null) return NotFound();
        return Ok(new { workforcePlanId = id, asOf = asOf?.ToUniversalTime(), source = "FundedPosition", summary });
    }

    [HttpGet("finance/reconciliation")]
    public async Task<IActionResult> Reconciliation(
        [FromQuery] int fiscalYear,
        [FromQuery] Guid? batchId,
        [FromQuery] Guid? orgUnitId,
        CancellationToken ct)
    {
        if (orgUnitId is Guid ou)
        {
            var orgAccess = await orgScope.GetAllowedOrgUnitIdsAsync(User, ct);
            if (orgAccess.IsDenied) return Forbid();
            if (orgAccess.MustFilter && !orgAccess.CanAccess(ou)) return Forbid();
        }

        var result = await reconciliationService.ReconcileAsync(fiscalYear, batchId, orgUnitId, ct);
        return Ok(result);
    }

    [HttpPost("finance/import")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> ImportFinance(
        IFormFile file,
        [FromQuery] int fiscalYear,
        CancellationToken ct)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User, ct) is not null)
            return Forbid();
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "الملف مطلوب." });

        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : (Guid?)null;
        await using var stream = file.OpenReadStream();
        var (batch, error) = await financeImportService.ImportCsvAsync(stream, file.FileName, fiscalYear, userId, ct);
        if (batch is null)
            return BadRequest(new { error });

        return Ok(new { batchId = batch.Id, batch.RowCount, batch.FiscalYear, note = "Stub file import — no live ERP" });
    }

    [HttpPost("job-catalog/import")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> ImportJobCatalog(
        IFormFile file,
        [FromQuery] JobCatalogImportMode mode = JobCatalogImportMode.Standard,
        CancellationToken ct = default)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User, ct) is not null)
            return Forbid();
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "الملف مطلوب." });

        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : (Guid?)null;
        await using var stream = file.OpenReadStream();
        var (batch, changeRequest, error) = await jobCatalogImportService.ImportCsvAsync(stream, file.FileName, mode, userId, ct);
        if (changeRequest is null)
            return BadRequest(new { error });

        return Ok(new
        {
            batchId = batch!.Id,
            changeRequestId = changeRequest.Id,
            changeRequestNumber = changeRequest.RequestNumber,
            rowCount = batch.RowCount,
            importMode = batch.ImportMode.ToString()
        });
    }

    [HttpPost("changes/{id:guid}/submit")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> SubmitChange(
        Guid id,
        [FromBody] EstablishmentChangeSubmitApiRequest body,
        CancellationToken ct = default)
    {
        if (await establishmentScope.ValidateCentralWriteAsync(User, ct) is not null)
            return Forbid();

        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : (Guid?)null;
        var userName = User.Identity?.Name;
        var (success, error) = await changeLifecycle.SubmitForApprovalAsync(
            id,
            body.LegalSourceId,
            body.DecisionReference,
            userId,
            userName,
            body.EffectiveFrom,
            ct);

        if (!success)
            return BadRequest(new { error });

        var request = await db.EstablishmentChangeRequests.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        return Ok(new { request.Id, request.Status, request.ResultEntityId });
    }
}

public sealed class EstablishmentChangeSubmitApiRequest
{
    public string LegalSourceId { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTime? EffectiveFrom { get; set; }
}
