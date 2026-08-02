using System.Diagnostics;
using System.Reflection;
using HRManagement.SharedKernel.Recruitment;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>HCM-001: tests for RecruitmentRequisition, CandidateApplication, and OfferLetter domain entities.</summary>
public class Hcm001RecruitmentTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "hcm001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void RecruitmentRequisition_extends_BaseEntity()
    {
        Assert.True(typeof(RecruitmentRequisition).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void RecruitmentRequisition_has_required_properties()
    {
        var props = typeof(RecruitmentRequisition).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("RequisitionNumber", props);
        Assert.Contains("PositionId", props);
        Assert.Contains("JobTitle", props);
        Assert.Contains("Status", props);
        Assert.Contains("RequestedDate", props);
        Assert.Contains("ApprovedDate", props);
        Assert.Contains("VacancyCount", props);
        Assert.Contains("LegalSourceId", props);
    }

    [Fact]
    public void CandidateApplication_extends_BaseEntity()
    {
        Assert.True(typeof(CandidateApplication).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void CandidateApplication_has_required_properties()
    {
        var props = typeof(CandidateApplication).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("RequisitionId", props);
        Assert.Contains("CandidateName", props);
        Assert.Contains("NationalId", props);
        Assert.Contains("Status", props);
        Assert.Contains("SubmittedAt", props);
        Assert.Contains("AssessmentScore", props);
        Assert.Contains("Rank", props);
        Assert.Contains("ConflictOfInterestDeclared", props);
    }

    [Fact]
    public void OfferLetter_extends_BaseEntity()
    {
        Assert.True(typeof(OfferLetter).IsSubclassOf(typeof(BaseEntity)));
    }

    [Fact]
    public void OfferLetter_has_required_properties()
    {
        var props = typeof(OfferLetter).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("ApplicationId", props);
        Assert.Contains("PositionId", props);
        Assert.Contains("OfferNumber", props);
        Assert.Contains("Status", props);
        Assert.Contains("OfferedSalary", props);
        Assert.Contains("IsConvertedToEmployee", props);
        Assert.Contains("ProposedStartDate", props);
        Assert.Contains("LegalSourceId", props);
    }

    [Fact]
    public void RequisitionStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(RequisitionStatus), RequisitionStatus.Draft));
        Assert.True(Enum.IsDefined(typeof(RequisitionStatus), RequisitionStatus.Approved));
        Assert.True(Enum.IsDefined(typeof(RequisitionStatus), RequisitionStatus.Published));
        Assert.True(Enum.IsDefined(typeof(RequisitionStatus), RequisitionStatus.Closed));
        Assert.True(Enum.IsDefined(typeof(RequisitionStatus), RequisitionStatus.Cancelled));
    }

    [Fact]
    public void ApplicationStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(ApplicationStatus), ApplicationStatus.Submitted));
        Assert.True(Enum.IsDefined(typeof(ApplicationStatus), ApplicationStatus.Shortlisted));
        Assert.True(Enum.IsDefined(typeof(ApplicationStatus), ApplicationStatus.OfferExtended));
        Assert.True(Enum.IsDefined(typeof(ApplicationStatus), ApplicationStatus.Accepted));
        Assert.True(Enum.IsDefined(typeof(ApplicationStatus), ApplicationStatus.Rejected));
        Assert.True(Enum.IsDefined(typeof(ApplicationStatus), ApplicationStatus.Withdrawn));
    }

    [Fact]
    public void OfferStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(OfferStatus), OfferStatus.Draft));
        Assert.True(Enum.IsDefined(typeof(OfferStatus), OfferStatus.Issued));
        Assert.True(Enum.IsDefined(typeof(OfferStatus), OfferStatus.Accepted));
        Assert.True(Enum.IsDefined(typeof(OfferStatus), OfferStatus.Declined));
        Assert.True(Enum.IsDefined(typeof(OfferStatus), OfferStatus.Expired));
        Assert.True(Enum.IsDefined(typeof(OfferStatus), OfferStatus.Revoked));
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "HCM_001_RECRUITMENT.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Recruitment", "RecruitmentRequisition.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Recruitment", "CandidateApplication.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.SharedKernel", "Recruitment", "OfferLetter.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "hcm001-validate.ps1");
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
