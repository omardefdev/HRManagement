using HRManagement.SharedKernel.Legal;

namespace HR.Contracts.Application.Services;

/// <summary>SYR-009: validates separation type against active service regime.</summary>
public static class SeparationRegimeValidationService
{
    public static async Task<(string? SourceRegimeId, string? PayrollRegimeCode, string? Error)> SnapshotAndValidateAsync(
        Guid employeeId,
        string separationTypeName,
        IHrCoreActiveRegimeClient activeRegimeClient,
        CancellationToken ct = default)
    {
        var regimes = await activeRegimeClient.GetActiveRegimesAsync([employeeId], ct);
        var sourceRegimeId = regimes.FirstOrDefault()?.LegalRegimeId;

        if (string.IsNullOrWhiteSpace(sourceRegimeId))
            return (null, null, ServiceRegimeSeparationRules.RegimeRulesNotFoundMessage);

        var typeCode = ServiceRegimeSeparationRules.ToSeparationTypeCode(separationTypeName);
        var error = ServiceRegimeSeparationRules.ValidateSeparationType(sourceRegimeId, typeCode);
        if (error is not null)
            return (sourceRegimeId, null, error);

        var payrollCode = ServiceRegimeToPayrollMapper.MapToPayrollCode(sourceRegimeId);
        return (sourceRegimeId, payrollCode, null);
    }
}
