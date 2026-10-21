using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Movements.Infrastructure.Consumers;

/// <summary>HCM-007: draft transfer request after internal placement hire closure.</summary>
public sealed class InternalPlacementCompletedConsumer(
    MovementsDbContext db,
    ILogger<InternalPlacementCompletedConsumer> logger) : IConsumer<IInternalPlacementCompleted>
{
    public async Task Consume(ConsumeContext<IInternalPlacementCompleted> context)
    {
        var message = context.Message;
        if (message.FromOrgUnitId is null || message.ToOrgUnitId is null)
        {
            logger.LogDebug(
                "Skipping transfer draft for application {ApplicationId}: missing org units",
                message.RecruitmentApplicationId);
            return;
        }

        if (message.FromOrgUnitId == message.ToOrgUnitId)
            return;

        var exists = await db.TransferRequests.AnyAsync(
            t => t.EmployeeId == message.EmployeeId
                 && t.FromOrgUnitId == message.FromOrgUnitId
                 && t.ToOrgUnitId == message.ToOrgUnitId
                 && t.Status == ApprovalStatus.Pending,
            context.CancellationToken);

        if (exists)
            return;

        db.TransferRequests.Add(new TransferRequest
        {
            EmployeeId = message.EmployeeId,
            FromOrgUnitId = message.FromOrgUnitId.Value,
            ToOrgUnitId = message.ToOrgUnitId.Value,
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Reason = $"نقل داخلي — طلب توظيف {message.RecruitmentApplicationId:D}",
            Status = ApprovalStatus.Pending
        });

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
