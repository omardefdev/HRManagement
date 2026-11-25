using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Payroll.Application.Abstractions;

namespace Payroll.Infrastructure.Integration;

public class PayrollEventPublisher(IPublishEndpoint publish) : IPayrollEventPublisher
{
    [StagesOutboxMessages]
    public Task PublishPayrollProcessedAsync(
        Guid payrollRunId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal totalNetPay,
        CancellationToken ct) =>
        publish.Publish<IPayrollProcessed>(new
        {
            PayrollRunId = payrollRunId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalNetPay = totalNetPay
        }, ct);

    [StagesOutboxMessages]
    public Task PublishPayrollReversedAsync(
        Guid payrollRunId,
        DateTime periodStart,
        DateTime periodEnd,
        decimal reversedNetPay,
        Guid reversalRequestId,
        DateTime reversedAt,
        CancellationToken ct) =>
        publish.Publish<IPayrollReversed>(new
        {
            PayrollRunId = payrollRunId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ReversedNetPay = reversedNetPay,
            ReversalRequestId = reversalRequestId,
            ReversedAt = reversedAt
        }, ct);
}
