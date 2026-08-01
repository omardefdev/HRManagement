using HRManagement.SharedKernel.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

public class NoOpDirectoryEntrySyncService(ILogger<NoOpDirectoryEntrySyncService> logger) : IDirectoryEntrySyncService
{
    public Task<bool> SyncEmployeeCreatedAsync(
        Guid employeeId,
        string fullName,
        string? nationalId,
        Guid? primaryOrgUnitId,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "DirectoryEntrySync skipped (no-op): EmployeeId={EmployeeId}",
            employeeId);
        return Task.FromResult(true);
    }
}
