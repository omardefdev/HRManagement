using System.Diagnostics;
using System.Reflection;
using HRManagement.SharedKernel.Time;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>HCM-002: tests for AttendanceRecord and ShiftDefinition domain entities.</summary>
public class Hcm002AttendanceTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "hcm002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void AttendanceRecord_extends_BaseEntity()
    {
        Assert.True(typeof(AttendanceRecord).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void AttendanceRecord_has_required_properties()
    {
        var props = typeof(AttendanceRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("EmployeeId", props);
        Assert.Contains("WorkDate", props);
        Assert.Contains("ClockIn", props);
        Assert.Contains("ClockOut", props);
        Assert.Contains("Status", props);
        Assert.Contains("WorkedMinutes", props);
        Assert.Contains("OvertimeMinutes", props);
        Assert.Contains("ShiftDefinitionId", props);
        Assert.Contains("SourceDeviceId", props);
        Assert.Contains("CorrectionReason", props);
        Assert.Contains("IsPayrollLocked", props);
    }

    [Fact]
    public void AttendanceRecord_IsComplete_when_both_punches()
    {
        var record = new AttendanceRecord
        {
            ClockIn = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
            ClockOut = new DateTime(2026, 7, 1, 16, 0, 0, DateTimeKind.Utc)
        };
        Assert.True(record.IsComplete);

        record.ClockOut = null;
        Assert.False(record.IsComplete);
    }

    [Fact]
    public void AttendanceRecord_CrossesMidnight_detection()
    {
        var record = new AttendanceRecord
        {
            ClockIn = new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc),
            ClockOut = new DateTime(2026, 7, 2, 6, 0, 0, DateTimeKind.Utc)
        };
        Assert.True(record.CrossesMidnight);

        record.ClockOut = new DateTime(2026, 7, 1, 23, 0, 0, DateTimeKind.Utc);
        Assert.False(record.CrossesMidnight);
    }

    [Fact]
    public void AttendanceStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(AttendanceStatus), AttendanceStatus.Present));
        Assert.True(Enum.IsDefined(typeof(AttendanceStatus), AttendanceStatus.Absent));
        Assert.True(Enum.IsDefined(typeof(AttendanceStatus), AttendanceStatus.Leave));
        Assert.True(Enum.IsDefined(typeof(AttendanceStatus), AttendanceStatus.Holiday));
        Assert.True(Enum.IsDefined(typeof(AttendanceStatus), AttendanceStatus.Rest));
        Assert.True(Enum.IsDefined(typeof(AttendanceStatus), AttendanceStatus.Mission));
    }

    [Fact]
    public void ShiftDefinition_extends_BaseEntity()
    {
        Assert.True(typeof(ShiftDefinition).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void ShiftDefinition_has_required_properties()
    {
        var props = typeof(ShiftDefinition).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("ShiftCode", props);
        Assert.Contains("ShiftName", props);
        Assert.Contains("Type", props);
        Assert.Contains("StartTime", props);
        Assert.Contains("EndTime", props);
        Assert.Contains("BreakMinutes", props);
        Assert.Contains("CrossesMidnight", props);
        Assert.Contains("OvertimeThresholdMinutes", props);
        Assert.Contains("MaxOvertimeMinutes", props);
        Assert.Contains("EffectiveFrom", props);
        Assert.Contains("EffectiveTo", props);
        Assert.Contains("IsActive", props);
    }

    [Fact]
    public void ShiftDefinition_ExpectedWorkMinutes_regular()
    {
        var shift = new ShiftDefinition
        {
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(16),
            BreakMinutes = 60,
            CrossesMidnight = false
        };
        Assert.Equal(420, shift.ExpectedWorkMinutes);
    }

    [Fact]
    public void ShiftDefinition_ExpectedWorkMinutes_night_shift()
    {
        var shift = new ShiftDefinition
        {
            StartTime = TimeSpan.FromHours(22),
            EndTime = TimeSpan.FromHours(6),
            BreakMinutes = 30,
            CrossesMidnight = true
        };
        Assert.Equal(450, shift.ExpectedWorkMinutes);
    }

    [Fact]
    public void ShiftDefinition_IsEffectiveOn_returns_correct_result()
    {
        var shift = new ShiftDefinition
        {
            IsActive = true,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.True(shift.IsEffectiveOn(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(shift.IsEffectiveOn(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void ShiftType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(ShiftType), ShiftType.Regular));
        Assert.True(Enum.IsDefined(typeof(ShiftType), ShiftType.Night));
        Assert.True(Enum.IsDefined(typeof(ShiftType), ShiftType.Split));
        Assert.True(Enum.IsDefined(typeof(ShiftType), ShiftType.Continuous));
        Assert.True(Enum.IsDefined(typeof(ShiftType), ShiftType.Flexible));
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "HCM_002_ATTENDANCE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Time", "AttendanceRecord.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Time", "ShiftDefinition.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "hcm002-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
