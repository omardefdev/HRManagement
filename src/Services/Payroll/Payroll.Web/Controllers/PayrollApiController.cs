using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Dtos;
using Payroll.Application.Features.EmployeePayProfiles;
using Payroll.Application.Features.Fraud;
using Payroll.Application.Features.PartialDisbursement;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Services;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using System.Security.Claims;

namespace Payroll.Web.Controllers;

[ApiController]
[Route("api/payroll")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
public class PayrollApiController : ControllerBase
{
    private readonly PayrollDbContext _db;
    private readonly IMediator _mediator;

    public PayrollApiController(PayrollDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    private Guid RequireUserId()
    {
        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(v, out var g)
            ? g
            : throw new UnauthorizedAccessException("Missing user identity claim.");
    }

    [HttpPost("runs")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> CreateRun([FromBody] CreatePayrollRunApiRequest request, CancellationToken ct)
    {
        var (success, id, error) = await _mediator.Send(new CreatePayrollRunCommand(
            request.PeriodStart, request.PeriodEnd, request.RunDate, request.RunType, request.SourceRunId), ct);
        if (!success) return BadRequest(new { error });
        return CreatedAtAction(nameof(GetRun), new { id }, new { id });
    }

    [HttpPost("runs/{id:guid}/submit-for-review")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> SubmitForReview(Guid id, CancellationToken ct)
    {
        var (success, error) = await _mediator.Send(new SubmitPayrollRunForReviewCommand(id), ct);
        if (!success) return BadRequest(new { error });
        return Ok();
    }

    [HttpPost("runs/{id:guid}/approve")]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> ApproveRun(Guid id, CancellationToken ct)
    {
        var (success, error) = await _mediator.Send(
            new ApprovePayrollRunCommand(id, RequireUserId(), "payroll-approver", null), ct);
        if (!success) return BadRequest(new { error });
        return Ok();
    }

    [HttpPost("runs/{id:guid}/post")]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> PostRun(Guid id, CancellationToken ct)
    {
        var (success, error) = await _mediator.Send(new PostPayrollRunCommand(id, RequireUserId()), ct);
        if (!success) return BadRequest(new { error });
        return Ok();
    }

    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun(Guid id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new PayrollRunDetailsDto(
                r.Id,
                r.TenantId,
                r.CreatedAt,
                r.UpdatedAt,
                r.CreatedBy,
                r.UpdatedBy,
                r.PeriodStart,
                r.PeriodEnd,
                r.RunDate,
                r.Status,
                r.RunType,
                r.SourceRunId,
                r.PayrollConfigurationId,
                r.ConfigContentHash,
                r.TotalNetPay,
                r.ResultsFrozen,
                r.ValidatedAtCalculate,
                r.SalaryLines
                    .OrderBy(line => line.EmployeeId)
                    .Select(line => new SalaryLineDetailsDto(
                        line.Id,
                        line.TenantId,
                        line.CreatedAt,
                        line.UpdatedAt,
                        line.CreatedBy,
                        line.UpdatedBy,
                        line.PayrollRunId,
                        line.EmployeeId,
                        line.GrossPay,
                        line.TotalDeductions,
                        line.NetPay,
                        line.PayComponents))
                    .ToList()))
            .FirstOrDefaultAsync(ct);
        if (run is null) return NotFound();
        return Ok(run);
    }

    [HttpGet("runs/{id:guid}/freeze-status")]
    public async Task<IActionResult> GetFreezeStatus(Guid id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();

        var dto = new PayrollRunFreezeStatusDto(
            run.Id,
            run.ResultsFrozen,
            run.ValidatedAtCalculate,
            run.ConfigContentHash);
        return Ok(dto);
    }

    [HttpPut("employees/{employeeId:guid}/pay-profile")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> UpsertPayProfile(
        Guid employeeId,
        [FromBody] UpsertEmployeePayProfileApiRequest request,
        CancellationToken ct)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(
            User,
            _db.OrgUnitSnapshots,
            ct: ct);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        if (scope.MustFilter)
        {
            var employeeOrgUnitId = await _db.EmployeePayProfiles
                .AsNoTracking()
                .Where(p => p.EmployeeId == employeeId)
                .Select(p => p.OrgUnitId)
                .FirstOrDefaultAsync(ct);

            if (this.ForbidIfOrgUnitOutOfScope(
                    scope,
                    employeeOrgUnitId,
                    resourceType: "EmployeePayProfile",
                    resourceId: employeeId.ToString("D")) is { } employeeDenied)
                return employeeDenied;
        }

        var (success, profileId, error) = await _mediator.Send(new UpsertEmployeePayProfileCommand(
            employeeId,
            request.BaseSalary,
            request.PayGradeId,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.IsActive,
            request.ChangeReason), ct);
        if (!success) return BadRequest(new { error });
        return Ok(new { profileId });
    }

    [HttpPost("runs/{id:guid}/funding-decisions")]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> CreateFundingDecision(
        Guid id,
        [FromBody] CreateFundingDecisionApiRequest request,
        CancellationToken ct)
    {
        var (success, decisionId, error) = await _mediator.Send(new CreateFundingDecisionCommand(
            id,
            request.RequestedAmount,
            request.ApprovedAmount,
            request.FundingRatio,
            request.PriorityRuleSetId,
            RequireUserId()), ct);
        if (!success) return BadRequest(new { error });
        return CreatedAtAction(nameof(GetRun), new { id }, new { id = decisionId });
    }

    [HttpPost("runs/{id:guid}/partial-disburse")]
    // Partial disbursement prepares the payment batch and arrears ledger. It does not
    // submit money to the bank, so it belongs to the processor stage; actual posting
    // and payment remain protected by Payroll.Post.
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> PartialDisburse(
        Guid id,
        [FromBody] PartialDisburseApiRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ExecutePartialDisbursementCommand(
            id, request.FundingDecisionId), ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        if (result.WasIdempotentReplay)
            return Conflict(new
            {
                batchId = result.BatchId,
                totalDisbursed = result.TotalDisbursed,
                totalArrears = result.TotalArrears,
                orders = result.Orders,
                invariantOk = result.InvariantOk,
                idempotentReplay = true
            });

        return Ok(new
        {
            batchId = result.BatchId,
            totalDisbursed = result.TotalDisbursed,
            totalArrears = result.TotalArrears,
            orders = result.Orders,
            invariantOk = result.InvariantOk
        });
    }

    [HttpPost("runs/{id:guid}/fraud-scan")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> FraudScan(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RunPayrollFraudScanCommand(id), ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new
        {
            alertsCreated = result.AlertsCreated,
            holdsCreated = result.HoldsCreated,
            holdIds = result.HoldIds
        });
    }

    [HttpGet("runs/{id:guid}/provenance")]
    public async Task<IActionResult> GetRunProvenance(Guid id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();

        var job = await _db.PayrollRunJobs
            .AsNoTracking()
            .Include(j => j.Partitions)
            .Include(j => j.InputSnapshots)
            .Where(j => j.PayrollRunId == id)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

        DateTime? configEffectiveFrom = null;
        if (run.PayrollConfigurationId.HasValue)
        {
            configEffectiveFrom = await _db.PayrollConfigurations
                .AsNoTracking()
                .Where(c => c.Id == run.PayrollConfigurationId.Value)
                .Select(c => (DateTime?)c.EffectiveFrom)
                .FirstOrDefaultAsync(ct);
        }

        var snapshots = job?.InputSnapshots ?? [];
        var aggregateFingerprint = snapshots.Count > 0
            ? PayrollDeterminismComparer.ComputeAggregateInputFingerprint(
                snapshots.Select(s => (s.EmployeeId, s.InputFingerprint)))
            : null;

        var dto = new PayrollRunProvenanceDto(
            run.Id,
            run.PeriodStart,
            run.PeriodEnd,
            run.ConfigContentHash,
            run.PayrollConfigurationId,
            configEffectiveFrom,
            job?.Id,
            job?.TotalEmployeeCount ?? snapshots.Count,
            job?.Partitions.Count ?? 0,
            job?.LastCheckpointAt,
            aggregateFingerprint,
            "PAY-002 pinned config + PAY-003 frozen snapshot");

        return Ok(dto);
    }

    [HttpGet("jobs/{jobId:guid}/input-snapshots")]
    public async Task<IActionResult> GetInputSnapshots(
        Guid jobId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = SearchFilterBase.DefaultPageSize,
        CancellationToken ct = default)
    {
        var jobExists = await _db.PayrollRunJobs.AsNoTracking().AnyAsync(j => j.Id == jobId, ct);
        if (!jobExists) return NotFound();

        skip = Math.Max(0, skip);
        take = SearchFilterBase.ClampPageSize(take);

        var total = await _db.PayrollRunInputSnapshots
            .AsNoTracking()
            .CountAsync(s => s.PayrollRunJobId == jobId, ct);

        var items = await _db.PayrollRunInputSnapshots
            .AsNoTracking()
            .Where(s => s.PayrollRunJobId == jobId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.EmployeeId)
            .Skip(skip)
            .Take(take)
            .Select(s => new PayrollRunInputSnapshotItemDto(
                s.EmployeeId,
                s.ProfileVersion,
                s.InputFingerprint,
                s.ConfigContentHash,
                s.SortOrder))
            .ToListAsync(ct);

        return Ok(new PayrollRunInputSnapshotsPageDto(jobId, skip, take, total, items));
    }

    [HttpPost("runs/{id:guid}/calculate")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Calculate(Guid id, CancellationToken ct)
    {
        var (success, count, error) = await _mediator.Send(new CalculatePayrollRunCommand(id), ct);
        if (!success) return BadRequest(new { error });
        return Ok(new { linesGenerated = count });
    }

    [HttpPost("jobs/{jobId:guid}/pause")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> PauseJob(Guid jobId, CancellationToken ct)
    {
        var (success, error) = await _mediator.Send(new PausePayrollRunJobCommand(jobId), ct);
        if (!success) return Conflict(new { error });
        return Ok();
    }

    [HttpPost("jobs/{jobId:guid}/resume")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> ResumeJob(Guid jobId, CancellationToken ct)
    {
        var (success, count, id, error) = await _mediator.Send(new ResumePayrollRunJobCommand(jobId), ct);
        if (!success) return BadRequest(new { error });
        return Ok(new { linesGenerated = count, jobId = id });
    }

    [HttpPost("jobs/{jobId:guid}/cancel")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> CancelJob(Guid jobId, CancellationToken ct)
    {
        var (success, error) = await _mediator.Send(new CancelPayrollRunJobCommand(jobId), ct);
        if (!success) return BadRequest(new { error });
        return Ok();
    }

    [HttpPost("runs/{id:guid}/validate")]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct)
    {
        var (success, error) = await _mediator.Send(new ValidatePayrollRunCommand(id), ct);
        if (!success) return BadRequest(new { error });
        return Ok();
    }

    [HttpPost("payment-batches/{batchId:guid}/process")]
    [Authorize(Policy = HrManagementPolicies.PayrollPost)]
    public async Task<IActionResult> ProcessPayment(Guid batchId, CancellationToken ct)
    {
        var (success, error) = await _mediator.Send(new ProcessPaymentBatchCommand(batchId, RequireUserId()), ct);
        if (!success) return BadRequest(new { error });
        return Ok();
    }

    [HttpGet("payslips/{salaryLineId:guid}")]
    public async Task<IActionResult> GetPayslip(Guid salaryLineId, CancellationToken ct)
    {
        var line = await _db.SalaryLines
            .Include(l => l.PayrollRun)
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == salaryLineId, ct);
        if (line is null) return NotFound();

        var components = await _db.SalaryLineComponents
            .Where(c => c.SalaryLineId == salaryLineId)
            .AsNoTracking()
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync(ct);

        return Ok(new { line, components });
    }
}

public record CreatePayrollRunApiRequest(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime RunDate,
    PayrollRunType RunType = PayrollRunType.Regular,
    Guid? SourceRunId = null);

public record UpsertEmployeePayProfileApiRequest(
    decimal BaseSalary,
    Guid? PayGradeId,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive = true,
    string? ChangeReason = null);

public record PartialDisburseApiRequest(Guid FundingDecisionId);

public record CreateFundingDecisionApiRequest(
    decimal RequestedAmount,
    decimal ApprovedAmount,
    decimal FundingRatio,
    Guid? PriorityRuleSetId = null);
