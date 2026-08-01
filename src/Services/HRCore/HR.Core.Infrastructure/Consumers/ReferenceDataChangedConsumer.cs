using MassTransit;
using Microsoft.Extensions.Logging;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Consumers;

/// <summary>MDM-001: minimal consumer that logs reference data change events for downstream reconciliation.</summary>
public class ReferenceDataChangedConsumer : IConsumer<IReferenceDataChanged>
{
    private readonly ILogger<ReferenceDataChangedConsumer> _logger;

    public ReferenceDataChangedConsumer(ILogger<ReferenceDataChangedConsumer> logger) =>
        _logger = logger;

    public Task Consume(ConsumeContext<IReferenceDataChanged> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 1, _logger, nameof(IReferenceDataChanged)))
            return Task.CompletedTask;

        _logger.LogInformation(
            "HR.Core: ReferenceDataChanged Kind={Kind} Code={Code} Active={Active} Change={Change}",
            m.CatalogKind, m.Code, m.IsActive, m.ChangeType);

        return Task.CompletedTask;
    }
}
