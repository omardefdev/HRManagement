namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-008: S2S active service regime lookup against HR.Core.</summary>
public interface IHrCoreActiveRegimeClient
{
    Task<IReadOnlyList<HrCoreActiveRegimeDto>> GetActiveRegimesAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default);
}

public sealed record HrCoreActiveRegimeDto(
    Guid EmployeeId,
    string? LegalRegimeId,
    Guid? EmploymentRecordId);
