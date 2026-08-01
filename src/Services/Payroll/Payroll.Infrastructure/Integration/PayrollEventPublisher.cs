using HRManagement.MessageContracts.Events;
using MassTransit;
using Payroll.Application.Abstractions;

namespace Payroll.Infrastructure.Integration;

public class PayrollEventPublisher(IPublishEndpoint publish) : IPayrollEventPublisher
{
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
}
