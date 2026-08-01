using System.Globalization;
using System.Text;
using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Attendance;
using Microsoft.Extensions.Options;

namespace HRManagement.Integration.FileAdapters.Attendance;

/// <summary>يقرأ سجلات حضور من ملف CSV في مجلد وارد (biometric-inbox).</summary>
public sealed class FileBiometricAttendanceProvider(IOptions<ExternalIntegrationsOptions> options) : IBiometricAttendanceProvider
{
    public async Task<IntegrationResult<IReadOnlyList<BiometricAttendanceRecord>>> ImportAsync(
        BiometricAttendanceImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var inbox = IntegrationFileHelper.ResolveOutputDirectory(options.Value, "biometric-inbox");
        var files = Directory.GetFiles(inbox, "attendance-*.csv").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        if (files.Count == 0)
        {
            return IntegrationResult<IReadOnlyList<BiometricAttendanceRecord>>.Ok(
                Array.Empty<BiometricAttendanceRecord>(),
                message: "لا توجد ملفات حضور في مجلد biometric-inbox");
        }

        var records = new List<BiometricAttendanceRecord>();
        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, cancellationToken);
            foreach (var line in lines.Skip(1)) // header
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 4) continue;

                if (!Guid.TryParse(parts[0], out var employeeId)) continue;
                if (!DateOnly.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate)) continue;
                if (workDate < request.FromDate || workDate > request.ToDate) continue;

                TimeOnly? checkIn = TimeOnly.TryParse(parts[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var ci) ? ci : null;
                TimeOnly? checkOut = parts.Length > 4 && TimeOnly.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var co) ? co : null;

                records.Add(new BiometricAttendanceRecord(
                    employeeId,
                    parts.Length > 1 ? parts[1] : null,
                    workDate,
                    checkIn,
                    checkOut,
                    request.SourceDeviceId));
            }
        }

        return IntegrationResult<IReadOnlyList<BiometricAttendanceRecord>>.Ok(
            records,
            message: $"Imported {records.Count} attendance records from {files.Count} file(s)",
            externalReference: Path.GetFileName(files[0]));
    }
}

public sealed class StubBiometricAttendanceProvider : IBiometricAttendanceProvider
{
    public Task<IntegrationResult<IReadOnlyList<BiometricAttendanceRecord>>> ImportAsync(
        BiometricAttendanceImportRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(IntegrationResult<IReadOnlyList<BiometricAttendanceRecord>>.Ok(
            Array.Empty<BiometricAttendanceRecord>(),
            message: "Stub: no biometric records (simulated empty import)"));
}
