using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Dtos;
using Payroll.Application.Options;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-58 (§4, D-PAY-3): a payroll run's read, for the module's users and for the independent reviewer (<c>Payroll.RunRead</c>): the
/// reviewer reads the run he reviews, and reaches no other payroll API. A run belongs to the whole agency.
/// PLAN-59 (P-9): the read also answers the run's present version (<c>revision</c>), the approvals of that version given and required
/// (<c>approvalsGiven</c>, <c>approvalsRequired</c>, as the approval counts them) and the posting's disbursement batch
/// (<c>paymentBatchId</c>, null before the posting): what an acceptance cycle reads instead of the database. No account number.
/// </summary>
[ApiController]
[Route("api/payroll")]
[ModulePolicyOverride(HrManagementPolicies.PayrollRunRead)]
[Authorize(Policy = HrManagementPolicies.PayrollRunRead)]
[OrgScopeCentralOnly("a payroll run belongs to the whole agency, not to a unit")]
public sealed class PayrollRunReadApiController(PayrollDbContext db, SeparationOfDutiesService sod) : ControllerBase
{
    [HttpGet("runs/{id:guid}")]
    public async Task<IActionResult> GetRun(Guid id, CancellationToken ct)
    {
        var run = await db.PayrollRuns
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
                        line.PayComponents,
                        line.TotalAdjustments))
                    .ToList(),
                r.Revision,
                r.Approvals
                    .Where(a => a.Decision == ApprovalDecision.Approved && a.Revision == r.Revision && a.ApproverId != null)
                    .Select(a => a.ApproverId)
                    .Distinct()
                    .Count(),
                1,
                db.PaymentBatches
                    .Where(b => b.PayrollRunId == r.Id && b.BatchKind == PaymentBatchKind.Disbursement)
                    .OrderBy(b => b.Source == PaymentBatchSource.PayrollRunPosted ? 0 : 1)
                    .ThenBy(b => b.CreatedAt)
                    .Select(b => (Guid?)b.Id)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(ct);
        if (run is null) return NotFound();
        return Ok(run with { ApprovalsRequired = sod.GetRequiredApprovalCount(PayrollSeparationOfDuties.RunEntityType, run.TotalNetPay) });
    }
}
