using System.Diagnostics;
using System.Reflection;
using HRManagement.SharedKernel.Talent;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>HCM-005: tests for SkillRecord and LearningEnrollment domain entities.</summary>
public class Hcm005LearningTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "hcm005-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void SkillRecord_extends_BaseEntity()
    {
        Assert.True(typeof(SkillRecord).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void SkillRecord_has_required_properties()
    {
        var props = typeof(SkillRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("EmployeeId", props);
        Assert.Contains("SkillCode", props);
        Assert.Contains("SkillName", props);
        Assert.Contains("Category", props);
        Assert.Contains("ProficiencyLevel", props);
        Assert.Contains("RequiredLevel", props);
        Assert.Contains("AssessedDate", props);
        Assert.Contains("AssessmentSource", props);
        Assert.Contains("CertificateId", props);
        Assert.Contains("CertificateExpiry", props);
        Assert.Contains("JobCatalogEntryId", props);
    }

    [Fact]
    public void SkillRecord_Gap_calculation()
    {
        var skill = new SkillRecord { ProficiencyLevel = 3, RequiredLevel = 5 };
        Assert.Equal(2, skill.Gap);
        Assert.True(skill.HasGap);

        skill.ProficiencyLevel = 5;
        Assert.Equal(0, skill.Gap);
        Assert.False(skill.HasGap);
    }

    [Fact]
    public void SkillRecord_Gap_when_no_requirement()
    {
        var skill = new SkillRecord { ProficiencyLevel = 3, RequiredLevel = null };
        Assert.Equal(-3, skill.Gap);
        Assert.False(skill.HasGap);
    }

    [Fact]
    public void SkillRecord_IsCertificateValid()
    {
        var skill = new SkillRecord
        {
            CertificateId = "CERT-001",
            CertificateExpiry = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };
        Assert.True(skill.IsCertificateValid(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(skill.IsCertificateValid(new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void SkillRecord_IsCertificateValid_no_expiry()
    {
        var skill = new SkillRecord { CertificateId = "CERT-001", CertificateExpiry = null };
        Assert.True(skill.IsCertificateValid(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void SkillRecord_IsCertificateValid_no_cert()
    {
        var skill = new SkillRecord { CertificateId = null };
        Assert.False(skill.IsCertificateValid(DateTime.UtcNow));
    }

    [Fact]
    public void SkillCategory_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(SkillCategory), SkillCategory.Technical));
        Assert.True(Enum.IsDefined(typeof(SkillCategory), SkillCategory.Managerial));
        Assert.True(Enum.IsDefined(typeof(SkillCategory), SkillCategory.Behavioral));
        Assert.True(Enum.IsDefined(typeof(SkillCategory), SkillCategory.Legal));
        Assert.True(Enum.IsDefined(typeof(SkillCategory), SkillCategory.Language));
        Assert.True(Enum.IsDefined(typeof(SkillCategory), SkillCategory.Specialized));
    }

    [Fact]
    public void LearningEnrollment_extends_BaseEntity()
    {
        Assert.True(typeof(LearningEnrollment).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void LearningEnrollment_has_required_properties()
    {
        var props = typeof(LearningEnrollment).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("EmployeeId", props);
        Assert.Contains("CourseCode", props);
        Assert.Contains("CourseName", props);
        Assert.Contains("ProviderName", props);
        Assert.Contains("Type", props);
        Assert.Contains("Status", props);
        Assert.Contains("EnrollmentDate", props);
        Assert.Contains("StartDate", props);
        Assert.Contains("CompletionDate", props);
        Assert.Contains("BudgetAmount", props);
        Assert.Contains("ActualCost", props);
        Assert.Contains("Score", props);
        Assert.Contains("CertificateId", props);
        Assert.Contains("TargetSkillCodes", props);
        Assert.Contains("ApprovedByManagerId", props);
    }

    [Fact]
    public void LearningEnrollment_IsCompleted_status()
    {
        var enrollment = new LearningEnrollment { Status = EnrollmentStatus.Completed };
        Assert.True(enrollment.IsCompleted);

        enrollment.Status = EnrollmentStatus.InProgress;
        Assert.False(enrollment.IsCompleted);
    }

    [Fact]
    public void LearningEnrollment_IsWithinBudget()
    {
        var enrollment = new LearningEnrollment { BudgetAmount = 1000m, ActualCost = 800m };
        Assert.True(enrollment.IsWithinBudget);

        enrollment.ActualCost = 1200m;
        Assert.False(enrollment.IsWithinBudget);
    }

    [Fact]
    public void LearningEnrollment_IsWithinBudget_null_budget()
    {
        var enrollment = new LearningEnrollment { BudgetAmount = null, ActualCost = 5000m };
        Assert.True(enrollment.IsWithinBudget);
    }

    [Fact]
    public void LearningType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(LearningType), LearningType.Training));
        Assert.True(Enum.IsDefined(typeof(LearningType), LearningType.Certification));
        Assert.True(Enum.IsDefined(typeof(LearningType), LearningType.ELearning));
        Assert.True(Enum.IsDefined(typeof(LearningType), LearningType.OnTheJob));
        Assert.True(Enum.IsDefined(typeof(LearningType), LearningType.Mentoring));
    }

    [Fact]
    public void EnrollmentStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(EnrollmentStatus), EnrollmentStatus.Nominated));
        Assert.True(Enum.IsDefined(typeof(EnrollmentStatus), EnrollmentStatus.Approved));
        Assert.True(Enum.IsDefined(typeof(EnrollmentStatus), EnrollmentStatus.InProgress));
        Assert.True(Enum.IsDefined(typeof(EnrollmentStatus), EnrollmentStatus.Completed));
        Assert.True(Enum.IsDefined(typeof(EnrollmentStatus), EnrollmentStatus.Failed));
        Assert.True(Enum.IsDefined(typeof(EnrollmentStatus), EnrollmentStatus.Cancelled));
        Assert.True(Enum.IsDefined(typeof(EnrollmentStatus), EnrollmentStatus.Withdrawn));
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "HCM_005_LEARNING.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Talent", "SkillRecord.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Talent", "LearningEnrollment.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "hcm005-validate.ps1");
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
