using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HRManagement.MessageContracts.Commands;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>INT-003: ACK hire/separation/transfer parties for HR.Core.</summary>
public class ProcessPartyAckConsumers :
    IConsumer<IExecuteSeparationParty>,
    IConsumer<IExecuteHireParty>,
    IConsumer<IExecuteTransferParty>
{
    private readonly HRCoreDbContext _db;
    private readonly EstablishmentService _establishmentService;
    private readonly IFundedPositionVersionService _fundedPositionVersions;
    private readonly ILogger<ProcessPartyAckConsumers> _logger;

    public ProcessPartyAckConsumers(
        HRCoreDbContext db,
        EstablishmentService establishmentService,
        IFundedPositionVersionService fundedPositionVersions,
        ILogger<ProcessPartyAckConsumers> logger)
    {
        _db = db;
        _establishmentService = establishmentService;
        _fundedPositionVersions = fundedPositionVersions;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IExecuteSeparationParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.HrCore, StringComparison.OrdinalIgnoreCase))
            return;

        _logger.LogInformation("HR.Core separation party for {Id}", m.SeparationRecordId);
        await context.Publish<ISeparationPartyCompleted>(new
        {
            m.SeparationRecordId,
            m.EmployeeId,
            m.Party,
            EvidenceRef = "hrcore:ack",
            m.CorrelationId,
            m.TenantId
        });
    }

    public async Task Consume(ConsumeContext<IExecuteHireParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.HrCore, StringComparison.OrdinalIgnoreCase))
            return;

        if (m.Compensate)
        {
            await CompensateHireAsync(m, context.CancellationToken);
            await context.Publish<IHirePartyCompleted>(new
            {
                m.ServiceTransactionId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = "hrcore:hire-compensate",
                m.CorrelationId,
                m.TenantId
            });
            return;
        }

        if (m.RecruitmentApplicationId is Guid appId)
        {
            var app = await _db.RecruitmentApplications
                .Include(a => a.Requisition)
                    .ThenInclude(r => r!.FundedPosition)
                .FirstOrDefaultAsync(a => a.Id == appId, context.CancellationToken);

            var position = app?.Requisition?.FundedPosition;
            if (position is null
                || position.Status != FundedPositionStatus.Occupied
                || position.OccupantEmployeeId != m.EmployeeId)
            {
                await context.Publish<IHirePartyFaulted>(new
                {
                    m.ServiceTransactionId,
                    m.EmployeeId,
                    m.Party,
                    Reason = "المنصب غير مشغول بالموظف المعيّن.",
                    m.CorrelationId,
                    m.TenantId
                });
                return;
            }
        }

        await context.Publish<IHirePartyCompleted>(new
        {
            m.ServiceTransactionId,
            m.EmployeeId,
            m.Party,
            EvidenceRef = "hrcore:hire-ack",
            m.CorrelationId,
            m.TenantId
        });
    }

    public async Task Consume(ConsumeContext<IExecuteTransferParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, ProcessParties.HrCore, StringComparison.OrdinalIgnoreCase))
            return;

        await context.Publish<ITransferPartyCompleted>(new
        {
            m.TransferRequestId,
            m.EmployeeId,
            m.Party,
            EvidenceRef = m.Compensate ? "hrcore:transfer-compensate" : "hrcore:transfer-ack",
            m.CorrelationId,
            m.TenantId
        });
    }

    private async Task CompensateHireAsync(IExecuteHireParty message, CancellationToken cancellationToken)
    {
        if (message.RecruitmentApplicationId is not Guid appId)
            return;

        var app = await _db.RecruitmentApplications
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .Include(a => a.ActiveOfferLetter)
            .FirstOrDefaultAsync(a => a.Id == appId, cancellationToken);

        if (app is null)
            return;

        var position = app.Requisition?.FundedPosition;
        if (position is not null && position.OccupantEmployeeId == message.EmployeeId)
        {
            _establishmentService.VacatePosition(position);
            await _fundedPositionVersions.AppendFromPositionAsync(
                position,
                EstablishmentOperationType.Vacated,
                DateTime.UtcNow,
                ct: cancellationToken);
        }

        if (app.Status == RecruitmentApplicationStatus.Hired)
        {
            app.Status = RecruitmentApplicationStatus.OfferAccepted;
            app.HiredEmployeeId = null;
            app.ReviewedAt = DateTime.UtcNow;
        }

        if (app.ActiveOfferLetter is { IsConvertedToEmployee: true } offer)
            offer.IsConvertedToEmployee = false;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
