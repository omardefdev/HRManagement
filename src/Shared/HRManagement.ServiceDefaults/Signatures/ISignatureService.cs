using HRManagement.SharedKernel.Signatures;

namespace HRManagement.ServiceDefaults.Signatures;

/// <summary>
/// Abstraction over the signing back-end (SIG-001).
/// Software X.509 today; swap to HSM/cloud KMS by replacing the implementation.
/// </summary>
public interface ISignatureService
{
    /// <summary>Sign the canonical payload hash and return a populated <see cref="SignatureRecord"/>.</summary>
    Task<SignatureRecord> SignAsync(SignatureRequest request, CancellationToken cancellationToken = default);

    /// <summary>Verify that a previously stored signature is still valid against the payload hash.</summary>
    bool Verify(SignatureRecord record);
}

/// <summary>Input DTO for <see cref="ISignatureService.SignAsync"/>.</summary>
public sealed record SignatureRequest(
    Guid TenantId,
    string SubjectType,
    string SubjectId,
    string SignerSubject,
    string SignerRole,
    string Intent,
    string CanonicalPayloadHash);
