using HRManagement.MessageContracts.Commands;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>INT-003: ACK hire/separation/transfer parties for HR.Core.</summary>
public class ProcessPartyAckConsumers :
    IConsumer<IExecuteSeparationParty>,
    IConsumer<IExecuteHireParty>,
    IConsumer<IExecuteTransferParty>
{
    private readonly ILogger<ProcessPartyAckConsumers> _logger;

    public ProcessPartyAckConsumers(ILogger<ProcessPartyAckConsumers> logger) => _logger = logger;

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

        await context.Publish<IHirePartyCompleted>(new
        {
            m.ServiceTransactionId,
            m.EmployeeId,
            m.Party,
            EvidenceRef = m.Compensate ? "hrcore:hire-compensate" : "hrcore:hire-ack",
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
}
