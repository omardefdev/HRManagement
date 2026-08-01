using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using HRManagement.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Directory.Infrastructure.Services;

public class DirectoryEntrySyncService : IDirectoryEntrySyncService
{
    private readonly AuthDirectoryDbContext _db;
    private readonly ILogger<DirectoryEntrySyncService> _logger;

    public DirectoryEntrySyncService(AuthDirectoryDbContext db, ILogger<DirectoryEntrySyncService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> SyncEmployeeCreatedAsync(
        Guid employeeId,
        string fullName,
        string? nationalId,
        Guid? primaryOrgUnitId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _db.DirectoryEntries.FirstOrDefaultAsync(e => e.EmployeeId == employeeId, cancellationToken);
        if (entry is null)
        {
            entry = new DirectoryEntry
            {
                EmployeeId = employeeId,
                DisplayName = fullName,
                PrimaryOrgUnitId = primaryOrgUnitId,
                Status = "Active",
                LastSyncedAt = DateTime.UtcNow
            };
            _db.DirectoryEntries.Add(entry);
        }
        else
        {
            entry.DisplayName = fullName;
            entry.PrimaryOrgUnitId = primaryOrgUnitId ?? entry.PrimaryOrgUnitId;
            entry.Status = "Active";
            entry.LastSyncedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Auth.Directory: synced DirectoryEntry for EmployeeId={EmployeeId}", employeeId);
        return true;
    }
}
