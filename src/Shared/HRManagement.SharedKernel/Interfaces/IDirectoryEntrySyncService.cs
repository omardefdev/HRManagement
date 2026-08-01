namespace HRManagement.SharedKernel.Interfaces;

/// <summary>
/// Cross-service hook to upsert Auth.Directory entries after HR provisioning events.
/// </summary>
public interface IDirectoryEntrySyncService
{
    /// <returns>true when sync succeeded (or intentionally skipped as success); false on failure.</returns>
    Task<bool> SyncEmployeeCreatedAsync(
        Guid employeeId,
        string fullName,
        string? nationalId,
        Guid? primaryOrgUnitId,
        CancellationToken cancellationToken = default);
}
