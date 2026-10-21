using HR.Core.Domain.Services;
using HRManagement.SharedKernel.Legal;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>SYR-001: validates recruitment requisition legal source against the national register.</summary>
public sealed class RecruitmentLegalSourceValidator(ILegislationRegisterProvider provider)
{
    public Task<string?> ValidateOptionalAsync(string? legalSourceId, CancellationToken ct = default)
    {
        if (RecruitmentLegalSourceRules.IsEmpty(legalSourceId))
            return Task.FromResult<string?>(null);

        return ValidateExistsAndActiveAsync(legalSourceId!.Trim(), ct);
    }

    public Task<string?> ValidateRequiredAsync(string? legalSourceId, CancellationToken ct = default) =>
        ValidateRequiredAsync(
            legalSourceId,
            RecruitmentLegalSourceRules.RequiredOnSubmitMessage,
            ct);

    public async Task<string?> ValidateRequiredAsync(
        string? legalSourceId,
        string requiredWhenEmptyMessage,
        CancellationToken ct = default)
    {
        if (RecruitmentLegalSourceRules.IsEmpty(legalSourceId))
            return requiredWhenEmptyMessage;

        return await ValidateExistsAndActiveAsync(legalSourceId!.Trim(), ct);
    }

    private async Task<string?> ValidateExistsAndActiveAsync(string legalSourceId, CancellationToken ct)
    {
        var source = await provider.GetByIdAsync(legalSourceId, ct);
        if (source is null)
            return RecruitmentLegalSourceRules.NotFoundMessage;

        if (!string.Equals(source.Status, "active", StringComparison.OrdinalIgnoreCase))
            return RecruitmentLegalSourceRules.NotActiveMessage;

        return null;
    }
}
