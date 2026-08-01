namespace HRManagement.Integration.Abstractions.Attendance;

/// <summary>استيراد سجلات الحضور من نظام البصمة الوطني أو أجهزة محلية.</summary>
public interface IBiometricAttendanceProvider
{
    Task<IntegrationResult<IReadOnlyList<BiometricAttendanceRecord>>> ImportAsync(
        BiometricAttendanceImportRequest request,
        CancellationToken cancellationToken = default);
}
