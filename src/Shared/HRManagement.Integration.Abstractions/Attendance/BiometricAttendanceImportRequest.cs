namespace HRManagement.Integration.Abstractions.Attendance;

public sealed record BiometricAttendanceImportRequest(
    DateOnly FromDate,
    DateOnly ToDate,
    string? SourceDeviceId = null);

public sealed record BiometricAttendanceRecord(
    Guid EmployeeId,
    string? NationalId,
    DateOnly WorkDate,
    TimeOnly? CheckIn,
    TimeOnly? CheckOut,
    string? DeviceId);
