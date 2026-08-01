using System.Diagnostics;
using System.Reflection;
using HRManagement.SharedKernel.Ess;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>ESS-001: tests for EmployeeServiceCase domain entity.</summary>
public class Ess001ServiceCenterTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "ess001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void EmployeeServiceCase_extends_BaseEntity()
    {
        Assert.True(typeof(EmployeeServiceCase).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void EmployeeServiceCase_has_required_properties()
    {
        var props = typeof(EmployeeServiceCase).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("CaseId", props);
        Assert.Contains("EmployeeId", props);
        Assert.Contains("CaseType", props);
        Assert.Contains("Status", props);
        Assert.Contains("SubmittedAt", props);
        Assert.Contains("SlaDeadline", props);
        Assert.Contains("SlaBreached", props);
        Assert.Contains("AssignedTo", props);
        Assert.Contains("WorkflowInstanceId", props);
        Assert.Contains("IsWithdrawn", props);
    }

    [Fact]
    public void ServiceCaseType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(ServiceCaseType), ServiceCaseType.ProfileCorrection));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseType), ServiceCaseType.LeaveRequest));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseType), ServiceCaseType.DocumentRequest));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseType), ServiceCaseType.PayslipQuery));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseType), ServiceCaseType.BenefitEnquiry));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseType), ServiceCaseType.GeneralInquiry));
    }

    [Fact]
    public void ServiceCaseStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(ServiceCaseStatus), ServiceCaseStatus.Submitted));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseStatus), ServiceCaseStatus.Acknowledged));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseStatus), ServiceCaseStatus.InProgress));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseStatus), ServiceCaseStatus.Resolved));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseStatus), ServiceCaseStatus.Closed));
        Assert.True(Enum.IsDefined(typeof(ServiceCaseStatus), ServiceCaseStatus.Withdrawn));
    }

    [Fact]
    public void IsSlaMet_returns_true_when_resolved_before_deadline()
    {
        var c = new EmployeeServiceCase
        {
            SlaDeadline = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            SlaBreached = false,
            ResolvedAt = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc)
        };
        Assert.True(c.IsSlaMet);
    }

    [Fact]
    public void IsSlaMet_returns_false_when_breached()
    {
        var c = new EmployeeServiceCase
        {
            SlaDeadline = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            SlaBreached = true,
            ResolvedAt = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc)
        };
        Assert.False(c.IsSlaMet);
    }

    [Fact]
    public void IsSlaMet_returns_false_when_resolved_after_deadline()
    {
        var c = new EmployeeServiceCase
        {
            SlaDeadline = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            SlaBreached = false,
            ResolvedAt = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc)
        };
        Assert.False(c.IsSlaMet);
    }

    [Fact]
    public void Default_status_is_Submitted()
    {
        var c = new EmployeeServiceCase();
        Assert.Equal(ServiceCaseStatus.Submitted, c.Status);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ESS_001_SERVICE_CENTER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Ess", "EmployeeServiceCase.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ess001-validate.ps1");
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
