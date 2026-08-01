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
public class ProcessInterventionsController(
    ContractsDbContext db,
    IPublishEndpoint publish) : ControllerBase
{
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

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var item = await db.ProcessInterventions.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status != ProcessInterventionStatuses.Open)
            return BadRequest("التدخل ليس مفتوحاً");

        if (item.SagaType == SagaTypeNames.Separation)
        {
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

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest? body, CancellationToken ct)
    {
        var item = await db.ProcessInterventions.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status != ProcessInterventionStatuses.Open)
            return BadRequest("التدخل ليس مفتوحاً");

        if (item.SagaType == SagaTypeNames.Separation)
        {
            await publish.Publish<ICancelSeparationSaga>(new
            {
                SeparationRecordId = item.CorrelationId,
                Reason = body?.Reason ?? "إلغاء من المشغّل",
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else if (item.SagaType == SagaTypeNames.Hire)
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

    public sealed class CancelRequest
    {
        public string? Reason { get; set; }
    }
}
