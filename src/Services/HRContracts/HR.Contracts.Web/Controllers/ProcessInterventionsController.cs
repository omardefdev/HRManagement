using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Sagas;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleHrContracts)]
[ApiController]
[Route("api/process-interventions")]
[OrgScopeCentralOnly("saga repair: the agency's process interventions")]
public class ProcessInterventionsController(
    ContractsDbContext db,
    IPublishEndpoint publish,
    HR.Contracts.Application.Abstractions.IContractsRepository repository) : ControllerBase
{
    public const string SeparationCancelRefusedMessage = HR.Contracts.Domain.Services.SeparationWorkflowRules.ExecutingCancelMessage;

    /// <summary>PLAN-60: the token the two POSTs validate (they now write a separation record's status, or refuse it).</summary>
    [HttpGet("antiforgery")]
    public HRManagement.ServiceDefaults.Messaging.MessagingRepairAntiforgery Antiforgery(
        [FromServices] Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
        HRManagement.ServiceDefaults.Messaging.MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(
        [FromQuery] string? sagaType = null,
        [FromQuery] string status = ProcessInterventionStatuses.Open,
        CancellationToken ct = default)
    {
        var q = db.ProcessInterventions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(sagaType))
            q = q.Where(i => i.SagaType == sagaType);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(i => i.Status == status);

        var items = await q.OrderByDescending(i => i.CreatedAt).Take(SearchFilterBase.MaxPageSize)
            .Select(i => new
            {
                i.Id,
                i.SagaType,
                i.CorrelationId,
                i.Reason,
                i.FailedParty,
                i.Status,
                i.CreatedAt,
                i.ResolvedAt
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var item = await db.ProcessInterventions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost("{id:guid}/resume")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var item = await db.ProcessInterventions.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status != ProcessInterventionStatuses.Open)
            return BadRequest("التدخل ليس مفتوحاً");

        if (item.SagaType == SagaTypeNames.Separation)
        {
            // PLAN-60 (§5): the record waiting for the intervention executes again, by this person.
            var record = await db.SeparationRecords.FirstOrDefaultAsync(r => r.Id == item.CorrelationId, ct);
            if (record is { Status: HR.Contracts.Domain.Enums.SeparationStatus.AwaitingIntervention })
                await HR.Contracts.Application.Features.SeparationRecords.SeparationStatusSync.TransitionAsync(
                    repository, record, HR.Contracts.Domain.Enums.SeparationStatus.Executing, Actor(), "استئناف بعد تدخل بشري", ct);

            await publish.Publish<IResumeSeparationSaga>(new
            {
                SeparationRecordId = item.CorrelationId,
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else if (item.SagaType == SagaTypeNames.Hire)
        {
            await publish.Publish<IResumeHireSaga>(new
            {
                ServiceTransactionId = item.CorrelationId,
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else
            return BadRequest("نوع الرحلة غير مدعوم في هذه الخدمة");

        item.Status = ProcessInterventionStatuses.Resolved;
        item.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { resumed = true });
    }

    [Authorize(Policy = HrManagementPolicies.HrContractsWrite)]
    [HttpPost("{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest? body, CancellationToken ct)
    {
        var item = await db.ProcessInterventions.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status != ProcessInterventionStatuses.Open)
            return BadRequest("التدخل ليس مفتوحاً");

        // PLAN-60 (§5): cancelling an executing separation is PLAN-63's compensation path; a hire's cancellation stays as it was.
        if (item.SagaType == SagaTypeNames.Separation)
            return Conflict(SeparationCancelRefusedMessage);

        if (item.SagaType == SagaTypeNames.Hire)
        {
            await publish.Publish<ICancelHireSaga>(new
            {
                ServiceTransactionId = item.CorrelationId,
                Reason = body?.Reason ?? "إلغاء تعيين من المشغّل",
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else
            return BadRequest("نوع الرحلة غير مدعوم للإلغاء في هذه الخدمة");

        item.Status = ProcessInterventionStatuses.Cancelled;
        item.ResolvedAt = DateTime.UtcNow;
        item.ResolutionNotes = body?.Reason;
        await db.SaveChangesAsync(ct);
        return Ok(new { cancelled = true });
    }

    private Guid? Actor()
    {
        var value = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var actor) ? actor : null;
    }

    public sealed class CancelRequest
    {
        public string? Reason { get; set; }
    }
}
