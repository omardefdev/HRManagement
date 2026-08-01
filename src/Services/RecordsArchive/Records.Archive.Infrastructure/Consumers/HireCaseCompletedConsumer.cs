using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

public class HireCaseCompletedConsumer : IConsumer<IHireCaseCompleted>
{
    private readonly ArchiveDbContext _db;
    private readonly ILogger<HireCaseCompletedConsumer> _logger;

    public HireCaseCompletedConsumer(ArchiveDbContext db, ILogger<HireCaseCompletedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IHireCaseCompleted> context)
    {
        var m = context.Message;
        var reference = $"HIRE-{m.ServiceTransactionId:N}";

        if (await _db.ArchiveItems.AnyAsync(x => x.ReferenceNumber == reference, context.CancellationToken))
            return;

        _db.ArchiveItems.Add(new ArchiveItem
        {
            Title = $"ملف تعيين الموظف {m.EmployeeId}",
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = "HR.Contracts/Appointments",
            RetentionPeriodYears = 10
        });

        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation(
            "Records.Archive: HireCaseCompleted EmployeeId={EmployeeId}, TransactionId={TransactionId}",
            m.EmployeeId, m.ServiceTransactionId);
    }
}
