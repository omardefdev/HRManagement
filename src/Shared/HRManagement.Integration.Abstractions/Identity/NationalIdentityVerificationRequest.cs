namespace HRManagement.Integration.Abstractions.Identity;

public sealed record NationalIdentityVerificationRequest(
    string NationalId,
    string? FullName = null,
    DateOnly? BirthDate = null);

public sealed record NationalIdentityVerificationResult(
    bool IsValid,
    string? FullName,
    string? StatusMessage,
    bool IsSyrianNational = true);
