using HRManagement.MessageContracts.Commands;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.ExportJobs;

public sealed class ExportJobRequestedConsumer(
    IHRCoreExportJobService exportJobs,
    ILogger<ExportJobRequestedConsumer> logger) : IConsumer<IExportJobRequested>
{
    public async Task Consume(ConsumeContext<IExportJobRequested> context)
    {
        var msg = context.Message;
        if (!string.Equals(msg.ServiceKey, HRCoreExportJobService.ServiceKey, StringComparison.OrdinalIgnoreCase))
            return;

        logger.LogInformation("PERF-004 processing export job {JobId} type {ExportType}", msg.JobId, msg.ExportType);
        await exportJobs.ProcessAsync(msg.JobId, context.CancellationToken);
    }
}
