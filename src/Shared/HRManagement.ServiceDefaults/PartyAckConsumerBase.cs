using HRManagement.MessageContracts.Commands;
using MassTransit;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// INT-003 helper: party ACK consumer template used by peripheral services.
/// Concrete services filter by Party name and publish completed/faulted.
/// </summary>
public abstract class SeparationPartyAckConsumerBase(string partyName) : IConsumer<IExecuteSeparationParty>
{
    public async Task Consume(ConsumeContext<IExecuteSeparationParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, partyName, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string evidence;
            if (m.Compensate)
            {
                evidence = await CompensateAsync(context) ?? $"{partyName}:compensate";
            }
            else
            {
                evidence = await ExecuteAsync(context) ?? $"{partyName}:ack";
            }

            await context.Publish<ISeparationPartyCompleted>(new
            {
                m.SeparationRecordId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = evidence,
                m.CorrelationId,
                m.TenantId
            });
        }
        catch (Exception ex)
        {
            await context.Publish<ISeparationPartyFaulted>(new
            {
                m.SeparationRecordId,
                m.EmployeeId,
                m.Party,
                Reason = ex.Message,
                m.CorrelationId,
                m.TenantId
            });
            throw;
        }
    }

    /// <returns>Optional evidence ref override.</returns>
    protected virtual Task<string?> ExecuteAsync(ConsumeContext<IExecuteSeparationParty> context) =>
        Task.FromResult<string?>(null);

    /// <summary>FUN-002: light compensate — not full PAY-003 posted reverse.</summary>
    protected virtual Task<string?> CompensateAsync(ConsumeContext<IExecuteSeparationParty> context) =>
        Task.FromResult<string?>(null);
}

public abstract class HirePartyAckConsumerBase(string partyName) : IConsumer<IExecuteHireParty>
{
    public async Task Consume(ConsumeContext<IExecuteHireParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, partyName, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (m.Compensate)
                await CompensateAsync(context);
            else
                await ExecuteAsync(context);

            await context.Publish<IHirePartyCompleted>(new
            {
                m.ServiceTransactionId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = m.Compensate ? $"{partyName}:compensate" : $"{partyName}:ack",
                m.CorrelationId,
                m.TenantId
            });
        }
        catch (Exception ex)
        {
            await context.Publish<IHirePartyFaulted>(new
            {
                m.ServiceTransactionId,
                m.EmployeeId,
                m.Party,
                Reason = ex.Message,
                m.CorrelationId,
                m.TenantId
            });
            throw;
        }
    }

    protected virtual Task ExecuteAsync(ConsumeContext<IExecuteHireParty> context) => Task.CompletedTask;

    /// <summary>FUN-001: light compensate (deactivate/ticket close) — not full separation reverse.</summary>
    protected virtual Task CompensateAsync(ConsumeContext<IExecuteHireParty> context) => Task.CompletedTask;
}

public abstract class TransferPartyAckConsumerBase(string partyName) : IConsumer<IExecuteTransferParty>
{
    public async Task Consume(ConsumeContext<IExecuteTransferParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, partyName, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string evidence;
            if (m.Compensate)
                evidence = await CompensateAsync(context) ?? $"{partyName}:compensate";
            else
                evidence = await ExecuteAsync(context) ?? $"{partyName}:ack";

            await context.Publish<ITransferPartyCompleted>(new
            {
                m.TransferRequestId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = evidence,
                m.CorrelationId,
                m.TenantId
            });
        }
        catch (Exception ex)
        {
            await context.Publish<ITransferPartyFaulted>(new
            {
                m.TransferRequestId,
                m.EmployeeId,
                m.Party,
                Reason = ex.Message,
                m.CorrelationId,
                m.TenantId
            });
            throw;
        }
    }

    protected virtual Task<string?> ExecuteAsync(ConsumeContext<IExecuteTransferParty> context) =>
        Task.FromResult<string?>(null);

    /// <summary>FUN-003: light compensate — not full posted payroll reverse.</summary>
    protected virtual Task<string?> CompensateAsync(ConsumeContext<IExecuteTransferParty> context) =>
        Task.FromResult<string?>(null);
}

public abstract class SecondmentPartyAckConsumerBase(string partyName) : IConsumer<IExecuteSecondmentParty>
{
    public async Task Consume(ConsumeContext<IExecuteSecondmentParty> context)
    {
        var m = context.Message;
        if (!string.Equals(m.Party, partyName, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string evidence;
            if (m.Compensate)
                evidence = await CompensateAsync(context) ?? $"{partyName}:compensate";
            else
                evidence = await ExecuteAsync(context) ?? $"{partyName}:ack";

            await context.Publish<ISecondmentPartyCompleted>(new
            {
                m.SecondmentRequestId,
                m.EmployeeId,
                m.Party,
                EvidenceRef = evidence,
                m.CorrelationId,
                m.TenantId
            });
        }
        catch (Exception ex)
        {
            await context.Publish<ISecondmentPartyFaulted>(new
            {
                m.SecondmentRequestId,
                m.EmployeeId,
                m.Party,
                Reason = ex.Message,
                m.CorrelationId,
                m.TenantId
            });
            throw;
        }
    }

    protected virtual Task<string?> ExecuteAsync(ConsumeContext<IExecuteSecondmentParty> context) =>
        Task.FromResult<string?>(null);

    protected virtual Task<string?> CompensateAsync(ConsumeContext<IExecuteSecondmentParty> context) =>
        Task.FromResult<string?>(null);
}

