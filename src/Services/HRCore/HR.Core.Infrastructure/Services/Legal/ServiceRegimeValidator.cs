using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-003: validates service regime assignment and benefit plan legal bindings.</summary>
public sealed class ServiceRegimeValidator(
    HRCoreDbContext db,
    Recruitment.RecruitmentLegalSourceValidator legalSourceValidator)
{
    public async Task<string?> ValidateAssignableAsync(string? legalRegimeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalRegimeId))
            return null;

        var regime = await db.LegalRegimes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.LegalRegimeId == legalRegimeId, ct);

        if (regime is null)
            return ServiceRegimeRules.NotFoundMessage;

        if (!regime.IsEffectiveOn(DateTime.UtcNow))
            return ServiceRegimeRules.NotActiveMessage;

        if (ServiceRegimeRules.RequiresResolvedLegislation(regime.LegalRegimeId)
            && string.IsNullOrWhiteSpace(regime.ResolvedLegalSourceId))
            return ServiceRegimeRules.MissingLegislationLinkMessage;

        return null;
    }

    public Task<string?> ValidateBenefitPlanLegalSourceAsync(string? legalSourceId, CancellationToken ct = default) =>
        legalSourceValidator.ValidateOptionalAsync(legalSourceId, ct);
}
