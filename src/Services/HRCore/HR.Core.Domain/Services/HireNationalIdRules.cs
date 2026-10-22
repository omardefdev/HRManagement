using HR.Core.Domain.Entities;
using HRManagement.SharedKernel;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-029 (A3): national-id checks at hire. National ids are compared by SHA-256 (<see cref="PiiMask.Sha256Hex"/>,
/// which trims), never in plaintext, and are never written to audit payloads.
/// </summary>
public static class HireNationalIdRules
{
    public const int MinMismatchReasonLength = 10;

    public const string MismatchReasonRequiredMessage =
        "الرقم الوطني لا يطابق الرقم المسجَّل في طلب التوظيف — أدخل مبرر الاختلاف (10 أحرف على الأقل).";

    /// <summary>Audit action recorded on the application when a mismatch was justified.</summary>
    public const string MismatchAuditAction = "HireNationalIdMismatch";

    /// <summary>Trimmed national id, or null when blank.</summary>
    public static string? Normalize(string? nationalId) =>
        string.IsNullOrWhiteSpace(nationalId) ? null : nationalId.Trim();

    public static string? HashOf(string? nationalId) =>
        Normalize(nationalId) is string normalized ? PiiMask.Sha256Hex(normalized) : null;

    /// <summary>Hash of the national id recorded on the application, or null when it has none.</summary>
    public static string? ApplicationHash(RecruitmentApplication app) =>
        !string.IsNullOrWhiteSpace(app.NationalIdHash) ? app.NationalIdHash : HashOf(app.NationalId);

    /// <summary>Employee national-id hash (the stored hash, or the hash of the decrypted id).</summary>
    public static string? EmployeeHash(Employee employee) =>
        !string.IsNullOrWhiteSpace(employee.NationalIdHash) ? employee.NationalIdHash : HashOf(employee.NationalId);

    /// <summary>True when both hashes exist and differ. No application national id means nothing to compare.</summary>
    public static bool Differs(string? applicationHash, string? otherHash) =>
        applicationHash is not null
        && otherHash is not null
        && !string.Equals(applicationHash, otherHash, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidMismatchReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= MinMismatchReasonLength;
}
