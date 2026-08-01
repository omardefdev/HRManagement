using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

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

    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun(Guid id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns
            .Include(r => r.SalaryLines)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();
        return Ok(run);
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
        var (success, error) = await _mediator.Send(new ProcessPaymentBatchCommand(batchId), ct);
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
