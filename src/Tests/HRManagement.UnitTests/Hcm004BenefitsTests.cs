using System.Diagnostics;
using System.Reflection;
using HRManagement.SharedKernel.Benefits;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>HCM-004: tests for BenefitPlan and PensionEnrollment domain entities.</summary>
public class Hcm004BenefitsTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "hcm004-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void BenefitPlan_extends_BaseEntity()
    {
        Assert.True(typeof(BenefitPlan).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void BenefitPlan_has_required_properties()
    {
        var props = typeof(BenefitPlan).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("PlanCode", props);
        Assert.Contains("PlanName", props);
        Assert.Contains("PlanType", props);
        Assert.Contains("EmployeeContributionRate", props);
        Assert.Contains("EmployerContributionRate", props);
        Assert.Contains("CeilingAmount", props);
        Assert.Contains("EligibilityMinServiceMonths", props);
        Assert.Contains("LegalSourceId", props);
        Assert.Contains("EffectiveFrom", props);
        Assert.Contains("EffectiveTo", props);
        Assert.Contains("IsActive", props);
    }

    [Fact]
    public void BenefitPlan_IsEffectiveOn_returns_true_within_range()
    {
        var plan = new BenefitPlan
        {
            IsActive = true,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.True(plan.IsEffectiveOn(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(plan.IsEffectiveOn(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(plan.IsEffectiveOn(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void BenefitPlan_IsEffectiveOn_returns_false_when_inactive()
    {
        var plan = new BenefitPlan
        {
            IsActive = false,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = null
        };

        Assert.False(plan.IsEffectiveOn(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void BenefitPlan_open_ended_effective_range()
    {
        var plan = new BenefitPlan
        {
            IsActive = true,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = null
        };

        Assert.True(plan.IsEffectiveOn(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void PensionEnrollment_extends_BaseEntity()
    {
        Assert.True(typeof(PensionEnrollment).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void PensionEnrollment_has_required_properties()
    {
        var props = typeof(PensionEnrollment).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("EmployeeId", props);
        Assert.Contains("BenefitPlanId", props);
        Assert.Contains("EnrollmentDate", props);
        Assert.Contains("TerminationDate", props);
        Assert.Contains("ServiceCreditMonths", props);
        Assert.Contains("ContributionBalance", props);
        Assert.Contains("BeneficiaryInfo", props);
        Assert.Contains("Status", props);
        Assert.Contains("LegalRegimeId", props);
    }

    [Fact]
    public void PensionEnrollment_IsActive_when_active_and_not_terminated()
    {
        var enrollment = new PensionEnrollment
        {
            Status = PensionEnrollmentStatus.Active,
            TerminationDate = null
        };
        Assert.True(enrollment.IsActive);

        enrollment.Status = PensionEnrollmentStatus.Suspended;
        Assert.False(enrollment.IsActive);

        enrollment.Status = PensionEnrollmentStatus.Active;
        enrollment.TerminationDate = DateTime.UtcNow;
        Assert.False(enrollment.IsActive);
    }

    [Fact]
    public void BenefitPlanType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(BenefitPlanType), BenefitPlanType.Health));
        Assert.True(Enum.IsDefined(typeof(BenefitPlanType), BenefitPlanType.Pension));
        Assert.True(Enum.IsDefined(typeof(BenefitPlanType), BenefitPlanType.SocialInsurance));
        Assert.True(Enum.IsDefined(typeof(BenefitPlanType), BenefitPlanType.Allowance));
    }

    [Fact]
    public void PensionEnrollmentStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(PensionEnrollmentStatus), PensionEnrollmentStatus.Active));
        Assert.True(Enum.IsDefined(typeof(PensionEnrollmentStatus), PensionEnrollmentStatus.Suspended));
        Assert.True(Enum.IsDefined(typeof(PensionEnrollmentStatus), PensionEnrollmentStatus.Terminated));
        Assert.True(Enum.IsDefined(typeof(PensionEnrollmentStatus), PensionEnrollmentStatus.Retired));
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "HCM_004_BENEFITS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Benefits", "BenefitPlan.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Benefits", "PensionEnrollment.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "hcm004-validate.ps1");
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
