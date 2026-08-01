using System.Text.Json;
using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Identity;
using Microsoft.Extensions.Options;

namespace HRManagement.Integration.FileAdapters.Identity;

/// <summary>محاكاة التحقق من الهوية — يقبل أرقاماً تبدأ بالبادئة المُعدّة.</summary>
public sealed class StubNationalIdentityVerifier(IOptions<ExternalIntegrationsOptions> options) : INationalIdentityVerifier
{
    public Task<IntegrationResult<NationalIdentityVerificationResult>> VerifyAsync(
        NationalIdentityVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NationalId))
        {
            return Task.FromResult(IntegrationResult<NationalIdentityVerificationResult>.Fail("الرقم الوطني مطلوب"));
        }

        var prefix = options.Value.StubNationalIdAcceptedPrefix ?? "01";
        var accepted = request.NationalId.StartsWith(prefix, StringComparison.Ordinal);

        var data = new NationalIdentityVerificationResult(
            IsValid: accepted,
            FullName: accepted ? (request.FullName ?? "مواطن (محاكاة)") : null,
            StatusMessage: accepted ? "تحقق محاكى — مقبول" : "تحقق محاكى — الرقم الوطني غير مطابق للبادئة المُعدّة",
            IsSyrianNational: accepted);

        return Task.FromResult(IntegrationResult<NationalIdentityVerificationResult>.Ok(
            data,
            message: accepted ? "Stub verification passed" : "Stub verification rejected",
            externalReference: $"STUB-ID-{request.NationalId}"));
    }
}

/// <summary>في وضع File: يسجّل طلب التحقق في ملف JSON دون اتصال حي.</summary>
public sealed class FileNationalIdentityVerifier(IOptions<ExternalIntegrationsOptions> options) : INationalIdentityVerifier
{
    public async Task<IntegrationResult<NationalIdentityVerificationResult>> VerifyAsync(
        NationalIdentityVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var dir = IntegrationFileHelper.ResolveOutputDirectory(options.Value, "identity-requests");
        var fileName = $"identity-verify-{DateTime.UtcNow:yyyyMMddHHmmss}-{request.NationalId}.json";
        var path = Path.Combine(dir, fileName);

        var payload = new
        {
            request.NationalId,
            request.FullName,
            BirthDate = request.BirthDate?.ToString("yyyy-MM-dd"),
            RequestedAt = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

        // File mode still returns a pending result — actual verification happens offline.
        var data = new NationalIdentityVerificationResult(
            IsValid: false,
            FullName: request.FullName,
            StatusMessage: "تم تسجيل طلب التحقق في ملف — بانتظار الربط اليدوي/الحي",
            IsSyrianNational: false);

        return IntegrationResult<NationalIdentityVerificationResult>.Ok(
            data,
            message: "Identity verification queued to file",
            externalReference: Path.GetFileName(path));
    }
}
