using HRManagement.SharedKernel.MasterData;

namespace HR.Core.Infrastructure.Services.Establishment;

public sealed class EstablishmentReferenceValidator(IReferenceDataProvider referenceData)
{
    public async Task<string?> ValidateGradeCodeAsync(
        string? code,
        DateTime? asOf = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "رمز الدرجة مطلوب.";

        var at = (asOf ?? DateTime.UtcNow).ToUniversalTime();
        var entry = await referenceData.ResolveEffectiveAsync(CatalogKind.Grade, code.Trim(), at, ct);
        if (entry is null)
            return "رمز الدرجة غير موجود أو غير نشط.";

        return null;
    }

    public async Task<string?> ValidateJobClassificationAsync(
        string? code,
        DateTime? asOf = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var at = (asOf ?? DateTime.UtcNow).ToUniversalTime();
        var entry = await referenceData.ResolveEffectiveAsync(
            CatalogKind.JobClassification,
            code.Trim(),
            at,
            ct);
        if (entry is null)
            return "رمز التصنيف الوظيفي غير موجود أو غير نشط.";

        return null;
    }
}
