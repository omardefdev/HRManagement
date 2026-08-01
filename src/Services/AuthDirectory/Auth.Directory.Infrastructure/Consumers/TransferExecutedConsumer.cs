using Auth.Directory.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Directory.Infrastructure.Consumers;

public class TransferExecutedConsumer : IConsumer<ITransferExecuted>
{
    private readonly AuthDirectoryDbContext _db;
    private readonly ILogger<TransferExecutedConsumer> _logger;

    public TransferExecutedConsumer(AuthDirectoryDbContext db, ILogger<TransferExecutedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        _logger.LogInformation(
            "Auth.Directory: TransferExecuted EmployeeId={EmployeeId} To={ToOrgUnitId}",
            m.EmployeeId, m.ToOrgUnitId);

        var entry = await _db.DirectoryEntries.FirstOrDefaultAsync(e => e.EmployeeId == m.EmployeeId);
        if (entry is null)
        {
            entry = new Auth.Directory.Domain.Entities.DirectoryEntry
            {
                EmployeeId = m.EmployeeId,
                DisplayName = m.EmployeeId.ToString(),
                PrimaryOrgUnitId = m.ToOrgUnitId,
                Status = "Active",
                LastSyncedAt = DateTime.UtcNow
            };
            _db.DirectoryEntries.Add(entry);
        }
        else
        {
            entry.PrimaryOrgUnitId = m.ToOrgUnitId;
            entry.LastSyncedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
