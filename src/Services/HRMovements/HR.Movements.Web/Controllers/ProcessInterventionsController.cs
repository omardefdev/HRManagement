using HR.Movements.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Movements.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleHrMovements)]
[ApiController]
[Route("api/process-interventions")]
public class ProcessInterventionsController(MovementsDbContext db, IPublishEndpoint publish) : ControllerBase
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

        var items = await q.OrderByDescending(i => i.CreatedAt).Take(200)
            .Select(i => new { i.Id, i.SagaType, i.CorrelationId, i.Reason, i.FailedParty, i.Status, i.CreatedAt })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var item = await db.ProcessInterventions.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status != ProcessInterventionStatuses.Open)
            return BadRequest("التدخل ليس مفتوحاً");

        if (item.SagaType == SagaTypeNames.Transfer)
        {
            await publish.Publish<IResumeTransferSaga>(new
            {
                TransferRequestId = item.CorrelationId,
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else if (item.SagaType == SagaTypeNames.Secondment)
        {
            await publish.Publish<IResumeSecondmentSaga>(new
            {
                SecondmentRequestId = item.CorrelationId,
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else
            return BadRequest("نوع الرحلة غير مدعوم");

        item.Status = ProcessInterventionStatuses.Resolved;
        item.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { resumed = true });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelInterventionRequest? body, CancellationToken ct)
    {
        var item = await db.ProcessInterventions.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        if (item.Status != ProcessInterventionStatuses.Open)
            return BadRequest("التدخل ليس مفتوحاً");

        var reason = string.IsNullOrWhiteSpace(body?.Reason) ? "إلغاء عبر التدخل" : body!.Reason!.Trim();

        if (item.SagaType == SagaTypeNames.Transfer)
        {
            await publish.Publish<ICancelTransferSaga>(new
            {
                TransferRequestId = item.CorrelationId,
                Reason = reason,
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else if (item.SagaType == SagaTypeNames.Secondment)
        {
            await publish.Publish<ICancelSecondmentSaga>(new
            {
                SecondmentRequestId = item.CorrelationId,
                Reason = reason,
                CorrelationId = CorrelationIdFlow.Get(),
                TenantId = TenantIdFlow.Get()
            }, ct);
        }
        else
            return BadRequest("نوع الرحلة غير مدعوم");

        item.Status = ProcessInterventionStatuses.Cancelled;
        item.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { cancelled = true });
    }

    public sealed class CancelInterventionRequest
    {
        public string? Reason { get; set; }
    }
}
