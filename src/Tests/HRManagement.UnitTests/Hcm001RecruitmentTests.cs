using System.Diagnostics;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>
/// HCM-001 smoke tests of the real recruitment model in <c>HR.Core.Domain</c> (PLAN-33 P5): requisition, selection
/// and offer state machines and the appeal window. Detailed rules are covered by the <c>Hcm026+</c> suites; these
/// replace the reflection tests on the removed <c>SharedKernel.Recruitment</c> types.
/// </summary>
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
    public void Requisition_follows_draft_approval_publish_close()
    {
        Assert.True(RequisitionWorkflowRules.CanTransition(RequisitionStatus.Draft, RequisitionStatus.PendingApproval));
        Assert.True(RequisitionWorkflowRules.CanTransition(RequisitionStatus.PendingApproval, RequisitionStatus.Approved));
        Assert.True(RequisitionWorkflowRules.CanTransition(RequisitionStatus.Approved, RequisitionStatus.Published));
        Assert.True(RequisitionWorkflowRules.CanTransition(RequisitionStatus.Published, RequisitionStatus.Closed));
    }

    [Fact]
    public void Requisition_rejection_returns_to_draft_and_approval_cannot_be_skipped()
    {
        Assert.True(RequisitionWorkflowRules.CanTransition(RequisitionStatus.PendingApproval, RequisitionStatus.Draft));
        Assert.False(RequisitionWorkflowRules.CanTransition(RequisitionStatus.Draft, RequisitionStatus.Approved));
        Assert.False(RequisitionWorkflowRules.CanTransition(RequisitionStatus.Draft, RequisitionStatus.Published));
        Assert.False(RequisitionWorkflowRules.CanTransition(RequisitionStatus.Closed, RequisitionStatus.Published));
        Assert.False(RequisitionWorkflowRules.CanClose(RequisitionStatus.Approved));
        Assert.True(RequisitionWorkflowRules.CanEdit(RequisitionStatus.Draft));
        Assert.False(RequisitionWorkflowRules.CanEdit(RequisitionStatus.Published));
    }

    [Fact]
    public void Requisition_publish_requires_an_approved_requisition_on_a_vacant_position()
    {
        var vacant = new FundedPosition { Status = FundedPositionStatus.Vacant };
        Assert.True(RequisitionWorkflowRules.CanPublish(RequisitionStatus.Approved, vacant, hasPublishedPosting: false));
        Assert.False(RequisitionWorkflowRules.CanPublish(RequisitionStatus.PendingApproval, vacant, hasPublishedPosting: false));
        Assert.False(RequisitionWorkflowRules.CanPublish(RequisitionStatus.Approved, vacant, hasPublishedPosting: true));
        Assert.False(RequisitionWorkflowRules.CanPublish(
            RequisitionStatus.Approved, vacant, hasPublishedPosting: false, RecruitmentPublishingChannel.InternalOnly));
    }

    [Fact]
    public void Selection_requires_conflict_of_interest_declaration_before_shortlisting()
    {
        var application = new RecruitmentApplication { Status = RecruitmentApplicationStatus.UnderReview };
        Assert.False(SelectionWorkflowRules.CanTransition(application, RecruitmentApplicationStatus.Shortlisted));
        Assert.NotNull(SelectionWorkflowRules.ValidateTransitionGate(application, RecruitmentApplicationStatus.Shortlisted));

        application.ConflictOfInterestDeclared = true;
        Assert.True(SelectionWorkflowRules.CanTransition(application, RecruitmentApplicationStatus.Shortlisted));
    }

    [Fact]
    public void Selection_assessed_is_system_only_and_ranking_needs_a_rank()
    {
        Assert.Contains(RecruitmentApplicationStatus.Assessed, SelectionWorkflowRules.SystemOnlyTargets);

        var assessed = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Assessed };
        Assert.False(SelectionWorkflowRules.CanTransition(assessed, RecruitmentApplicationStatus.Ranked));
        assessed.Rank = 1;
        Assert.True(SelectionWorkflowRules.CanTransition(assessed, RecruitmentApplicationStatus.Ranked));

        var submitted = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Submitted };
        Assert.False(SelectionWorkflowRules.CanTransition(submitted, RecruitmentApplicationStatus.Hired));
    }

    [Fact]
    public void Selection_offer_needs_a_draft_offer_and_no_pending_appeal()
    {
        var ranked = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Ranked, Rank = 1 };
        Assert.False(SelectionWorkflowRules.CanTransition(ranked, RecruitmentApplicationStatus.OfferExtended));

        ranked.ActiveOfferLetterId = Guid.NewGuid();
        Assert.True(SelectionWorkflowRules.CanTransition(ranked, RecruitmentApplicationStatus.OfferExtended));

        ranked.AppealStatus = RecruitmentAppealStatus.Pending;
        Assert.False(SelectionWorkflowRules.CanTransition(ranked, RecruitmentApplicationStatus.OfferExtended));
    }

    [Fact]
    public void Offer_is_approved_before_issue_and_accepted_only_while_issued_and_unexpired()
    {
        Assert.True(OfferWorkflowRules.CanTransition(OfferStatus.Draft, OfferStatus.PendingApproval));
        Assert.True(OfferWorkflowRules.CanTransition(OfferStatus.PendingApproval, OfferStatus.Issued));
        Assert.False(OfferWorkflowRules.CanTransition(OfferStatus.Draft, OfferStatus.Issued));
        Assert.False(OfferWorkflowRules.CanTransition(OfferStatus.PendingApproval, OfferStatus.Accepted));
        Assert.False(OfferWorkflowRules.CanTransition(OfferStatus.Accepted, OfferStatus.Revoked));

        var offer = new OfferLetter { Status = OfferStatus.PendingApproval, ValidityDays = 7 };
        Assert.False(OfferWorkflowRules.CanAccept(offer));

        var issuedAt = DateTime.UtcNow;
        OfferWorkflowRules.MarkIssued(offer, issuedAt);
        Assert.Equal(OfferStatus.Issued, offer.Status);
        Assert.Equal(OfferWorkflowRules.ComputeExpiryDate(issuedAt, 7), offer.ExpiryDate);
        Assert.True(OfferWorkflowRules.CanAccept(offer));
        Assert.False(OfferWorkflowRules.CanExpire(offer, issuedAt));
        Assert.True(OfferWorkflowRules.CanExpire(offer, offer.ExpiryDate!.Value.AddSeconds(1)));
    }

    [Fact]
    public void Appeal_window_runs_from_rejection_and_allows_one_appeal()
    {
        var rejectedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var application = new RecruitmentApplication
        {
            Status = RecruitmentApplicationStatus.Rejected,
            RejectionKind = RecruitmentRejectionKind.SelectionDecision,
            RejectedAt = rejectedAt
        };

        var window = RecruitmentAppealWorkflowRules.DefaultAppealWindowDays;
        Assert.True(RecruitmentAppealWorkflowRules.CanAppeal(application, rejectedAt.AddDays(window)));
        Assert.Equal(RecruitmentAppealWorkflowRules.WindowExpiredMessage,
            RecruitmentAppealWorkflowRules.GetAppealError(application, rejectedAt.AddDays(window).AddMinutes(1)));

        application.AppealStatus = RecruitmentAppealStatus.Pending;
        Assert.Equal(RecruitmentAppealWorkflowRules.AlreadyAppealedMessage,
            RecruitmentAppealWorkflowRules.GetAppealError(application, rejectedAt.AddDays(1)));

        var open = new RecruitmentApplication { Status = RecruitmentApplicationStatus.Ranked };
        Assert.Equal(RecruitmentAppealWorkflowRules.NotRejectedMessage,
            RecruitmentAppealWorkflowRules.GetAppealError(open, rejectedAt));
    }

    [Fact]
    public void Appeal_is_decided_only_by_the_assigned_reviewer()
    {
        var reviewer = Guid.NewGuid();
        var appeal = new RecruitmentAppeal
        {
            Status = RecruitmentAppealStatus.Pending,
            IndependentReviewerEmployeeId = reviewer
        };

        Assert.True(RecruitmentAppealWorkflowRules.IsAssignedReviewer(appeal, true, null, reviewer));
        Assert.False(RecruitmentAppealWorkflowRules.IsAssignedReviewer(appeal, true, null, Guid.NewGuid()));
        Assert.False(RecruitmentAppealWorkflowRules.IsAssignedReviewer(appeal, false, null, reviewer));
    }

    [Fact]
    public void Docs_and_real_model_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "HCM_001_RECRUITMENT.md")));
        foreach (var entity in new[] { "RecruitmentRequisition.cs", "RecruitmentApplication.cs", "OfferLetter.cs", "RecruitmentAppeal.cs" })
        {
            Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
                "src", "Services", "HRCore", "HR.Core.Domain", "Entities", entity)), entity);
        }

        Assert.False(Directory.Exists(Path.Combine(RepoRoot, "src", "Shared", "HRManagement.SharedKernel", "Recruitment")),
            "SharedKernel.Recruitment is not the runtime model and was removed (PLAN-33).");
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
