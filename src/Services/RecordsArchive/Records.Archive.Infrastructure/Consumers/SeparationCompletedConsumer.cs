using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Infrastructure.Data;
using HRManagement.MessageContracts.Events;

namespace Records.Archive.Infrastructure.Consumers;

public class SeparationCompletedConsumer : IConsumer<ISeparationCompleted>
{
    private readonly ArchiveDbContext _db;
    private readonly ILogger<SeparationCompletedConsumer> _logger;

    public SeparationCompletedConsumer(ArchiveDbContext db, ILogger<SeparationCompletedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ISeparationCompleted> context)
    {
        var m = context.Message;
        var reference = $"SEP-{m.EmployeeId}";

        _logger.LogInformation(
            "Records.Archive: SeparationCompleted EmployeeId={EmployeeId}, Type={SeparationType}",
            m.EmployeeId, m.SeparationType);

        if (await _db.ArchiveItems.AnyAsync(x => x.ReferenceNumber == reference))
            return;

        _db.ArchiveItems.Add(new ArchiveItem
        {
            Title = $"ملف فصل الموظف {m.EmployeeId} — {m.SeparationType}",
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = "HRContracts/Separations",
            RetentionPeriodYears = 10
        });

        await _db.SaveChangesAsync();
    }
}
