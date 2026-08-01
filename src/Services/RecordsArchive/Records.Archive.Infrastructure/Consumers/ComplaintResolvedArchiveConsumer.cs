using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Infrastructure.Data;
using HRManagement.MessageContracts.Events;

namespace Records.Archive.Infrastructure.Consumers;

public class ComplaintResolvedArchiveConsumer : IConsumer<IComplaintResolved>
{
    private readonly ArchiveDbContext _db;
    private readonly ILogger<ComplaintResolvedArchiveConsumer> _logger;

    public ComplaintResolvedArchiveConsumer(ArchiveDbContext db, ILogger<ComplaintResolvedArchiveConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IComplaintResolved> context)
    {
        var m = context.Message;
        var reference = $"CMP-RES-{m.ComplaintId:N}";
        if (await _db.ArchiveItems.AnyAsync(x => x.ReferenceNumber == reference, context.CancellationToken))
            return;

        _logger.LogInformation(
            "Records.Archive: ComplaintResolved ComplaintId={ComplaintId}",
            m.ComplaintId);

        _db.ArchiveItems.Add(new ArchiveItem
        {
            Title = $"ملف شكوى — {m.Subject}: {m.Resolution}",
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = "Compliance/Complaints",
            RetentionPeriodYears = 10
        });

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
