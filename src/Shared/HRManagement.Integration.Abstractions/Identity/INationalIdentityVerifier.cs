namespace HRManagement.Integration.Abstractions.Identity;

/// <summary>التحقق من الهوية الوطنية (وزارة الداخلية).</summary>
public interface INationalIdentityVerifier
{
    Task<IntegrationResult<NationalIdentityVerificationResult>> VerifyAsync(
        NationalIdentityVerificationRequest request,
        CancellationToken cancellationToken = default);
}
