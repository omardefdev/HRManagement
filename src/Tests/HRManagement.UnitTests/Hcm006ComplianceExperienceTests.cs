using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HRManagement.SharedKernel;

namespace HRManagement.UnitTests;

public class Hcm006ComplianceExperienceTests
{
    [Fact]
    public void CanRank_and_CanOffer_blocked_when_appeal_pending()
    {
        var app = new RecruitmentApplication
        {
            AppealStatus = RecruitmentAppealStatus.Pending,
            Status = RecruitmentApplicationStatus.Ranked
        };

        Assert.False(RecruitmentAppealWorkflowRules.CanRank(app));
        Assert.False(RecruitmentAppealWorkflowRules.CanOffer(app));
    }

    [Fact]
    public void CanAppeal_only_from_rejected_within_window()
    {
        var app = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.Rejected,
            ReviewedAt = DateTime.UtcNow.AddDays(-5)
        };

        Assert.True(RecruitmentAppealWorkflowRules.CanAppeal(app, DateTime.UtcNow));
    }

    [Fact]
    public void TryAnonymize_clears_pii_fields()
    {
        var citizen = new CitizenApplication
        {
            FullName = "Test Applicant",
            NationalId = "12345678901",
            NationalIdHash = PiiMask.Sha256Hex("12345678901"),
            Phone = "0999999999",
            Email = "test@example.com"
        };

        var (ok, error) = CandidateApplicationLegalLifecycleService.TryAnonymize(citizen);
        Assert.True(ok, error);
        Assert.NotNull(citizen.AnonymizedAt);
        Assert.StartsWith("ANON ", citizen.FullName);
        Assert.StartsWith("ANON-", citizen.NationalId);
        Assert.NotNull(citizen.NationalIdHash);
    }

    [Fact]
    public void CanPurge_requires_closed_requisition_and_retention_elapsed()
    {
        var req = new RecruitmentRequisition
        {
            Status = RequisitionStatus.Closed,
            ClosedDate = DateTime.UtcNow.AddYears(-4)
        };
        var citizen = new CitizenApplication
        {
            Requisition = req,
            LegalHold = false,
            SubmittedAt = DateTime.UtcNow.AddYears(-5)
        };

        Assert.True(CandidateApplicationLegalLifecycleService.CanPurge(citizen, req, DateTime.UtcNow));
    }
}
