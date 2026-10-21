using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;

namespace HR.Core.Infrastructure.Services.Establishment;

/// <summary>EST-002: validates legal source for establishment change requests (SYR-001).</summary>
public sealed class EstablishmentLegalSourceValidator(RecruitmentLegalSourceValidator recruitmentValidator)
{
    public const string RequiredOnSubmitMessage = "المرجع القانوني مطلوب عند تقديم طلب تغيير الملاك.";

    public Task<string?> ValidateRequiredAsync(string? legalSourceId, CancellationToken ct = default) =>
        recruitmentValidator.ValidateRequiredAsync(legalSourceId, RequiredOnSubmitMessage, ct);

    public Task<string?> ValidateOptionalAsync(string? legalSourceId, CancellationToken ct = default) =>
        recruitmentValidator.ValidateOptionalAsync(legalSourceId, ct);
}
