using System.Diagnostics;
using System.Reflection;
using HRManagement.SharedKernel.Cases;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>HCM-006: tests for EmployeeRelationCase domain entity.</summary>
public class Hcm006EmployeeRelationsTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "hcm006-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void EmployeeRelationCase_extends_BaseEntity()
    {
        Assert.True(typeof(EmployeeRelationCase).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void EmployeeRelationCase_has_required_properties()
    {
        var props = typeof(EmployeeRelationCase).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("CaseNumber", props);
        Assert.Contains("CaseType", props);
        Assert.Contains("Status", props);
        Assert.Contains("EmployeeId", props);
        Assert.Contains("ReportedById", props);
        Assert.Contains("IsWhistleblower", props);
        Assert.Contains("IsConfidential", props);
        Assert.Contains("Deadline", props);
        Assert.Contains("AssignedInvestigatorId", props);
        Assert.Contains("LegalHoldReference", props);
        Assert.Contains("HasConflictOfInterest", props);
        Assert.Contains("EscalatedToLegal", props);
    }

    [Fact]
    public void RelationCaseType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(RelationCaseType), RelationCaseType.Grievance));
        Assert.True(Enum.IsDefined(typeof(RelationCaseType), RelationCaseType.Discipline));
        Assert.True(Enum.IsDefined(typeof(RelationCaseType), RelationCaseType.Investigation));
        Assert.True(Enum.IsDefined(typeof(RelationCaseType), RelationCaseType.Whistleblower));
        Assert.True(Enum.IsDefined(typeof(RelationCaseType), RelationCaseType.WorkplaceIncident));
        Assert.True(Enum.IsDefined(typeof(RelationCaseType), RelationCaseType.Appeal));
    }

    [Fact]
    public void RelationCaseStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(RelationCaseStatus), RelationCaseStatus.Open));
        Assert.True(Enum.IsDefined(typeof(RelationCaseStatus), RelationCaseStatus.UnderInvestigation));
        Assert.True(Enum.IsDefined(typeof(RelationCaseStatus), RelationCaseStatus.PendingHearing));
        Assert.True(Enum.IsDefined(typeof(RelationCaseStatus), RelationCaseStatus.ActionTaken));
        Assert.True(Enum.IsDefined(typeof(RelationCaseStatus), RelationCaseStatus.Closed));
        Assert.True(Enum.IsDefined(typeof(RelationCaseStatus), RelationCaseStatus.Dismissed));
    }

    [Fact]
    public void Whistleblower_case_allows_null_reporter()
    {
        var wbCase = new EmployeeRelationCase
        {
            CaseType = RelationCaseType.Whistleblower,
            IsWhistleblower = true,
            ReportedById = null
        };

        Assert.Null(wbCase.ReportedById);
        Assert.True(wbCase.IsWhistleblower);
    }

    [Fact]
    public void Default_values_are_correct()
    {
        var c = new EmployeeRelationCase();
        Assert.Equal(RelationCaseStatus.Open, c.Status);
        Assert.True(c.IsConfidential);
        Assert.False(c.IsWhistleblower);
        Assert.False(c.EscalatedToLegal);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "HCM_006_EMPLOYEE_RELATIONS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Cases", "EmployeeRelationCase.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "hcm006-validate.ps1");
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
