using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-027 (C1/C2): OfferDeclined / ReserveListed transitions and requisition cascades.</summary>
public class Hcm027SelectionTransitionsTests
{
    private static RecruitmentApplication App(RecruitmentApplicationStatus status, Guid? activeOfferId = null) =>
        new()
        {
            ApplicantFullName = "مرشح",
            Status = status,
            Rank = 1,
            RequisitionId = Guid.NewGuid(),
            ActiveOfferLetterId = activeOfferId
        };

    [Theory]
    [InlineData(RecruitmentApplicationStatus.OfferExtended, RecruitmentApplicationStatus.OfferDeclined, true)]
    [InlineData(RecruitmentApplicationStatus.Ranked, RecruitmentApplicationStatus.ReserveListed, true)]
    [InlineData(RecruitmentApplicationStatus.ReserveListed, RecruitmentApplicationStatus.Rejected, true)]
    [InlineData(RecruitmentApplicationStatus.ReserveListed, RecruitmentApplicationStatus.Withdrawn, true)]
    [InlineData(RecruitmentApplicationStatus.ReserveListed, RecruitmentApplicationStatus.Ranked, false)]
    [InlineData(RecruitmentApplicationStatus.Assessed, RecruitmentApplicationStatus.OfferExtended, false)]
    [InlineData(RecruitmentApplicationStatus.Assessed, RecruitmentApplicationStatus.ReserveListed, false)]
    [InlineData(RecruitmentApplicationStatus.OfferDeclined, RecruitmentApplicationStatus.UnderReview, false)]
    [InlineData(RecruitmentApplicationStatus.OfferDeclined, RecruitmentApplicationStatus.Ranked, false)]
    public void Transitions(RecruitmentApplicationStatus from, RecruitmentApplicationStatus to, bool expected)
    {
        Assert.Equal(expected, SelectionWorkflowRules.CanTransition(App(from), to));
    }

    [Fact]
    public void ReserveListed_to_offer_extended_requires_an_offer()
    {
        Assert.False(SelectionWorkflowRules.CanTransition(App(RecruitmentApplicationStatus.ReserveListed), RecruitmentApplicationStatus.OfferExtended));
        Assert.True(SelectionWorkflowRules.CanTransition(
            App(RecruitmentApplicationStatus.ReserveListed, Guid.NewGuid()), RecruitmentApplicationStatus.OfferExtended));
    }

    [Fact]
    public void Allowed_targets_never_offer_system_only_statuses()
    {
        foreach (var status in Enum.GetValues<RecruitmentApplicationStatus>())
        {
            var targets = SelectionWorkflowRules.GetAllowedTargets(App(status, Guid.NewGuid()));
            Assert.DoesNotContain(RecruitmentApplicationStatus.OfferDeclined, targets);
            Assert.DoesNotContain(RecruitmentApplicationStatus.ReserveListed, targets);
        }

        Assert.Empty(SelectionWorkflowRules.GetAllowedTargets(App(RecruitmentApplicationStatus.OfferDeclined)));
    }

    [Theory]
    [InlineData(RecruitmentApplicationStatus.OfferDeclined)]
    [InlineData(RecruitmentApplicationStatus.ReserveListed)]
    public async Task Manual_transition_to_system_only_status_is_rejected(RecruitmentApplicationStatus to)
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db);
        var app = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1);

        var (success, error) = await new RecruitmentSelectionService(db).TransitionStatusAsync(app.Id, to, false);

        Assert.False(success);
        Assert.Equal(SelectionWorkflowRules.OfferOutcomeIsAutomaticMessage, error);
    }

    [Fact]
    public void Terminal_set_contains_offer_declined_but_not_reserve_listed()
    {
        Assert.Contains(RecruitmentApplicationStatus.OfferDeclined, SelectionWorkflowRules.TerminalStatuses);
        Assert.DoesNotContain(RecruitmentApplicationStatus.ReserveListed, SelectionWorkflowRules.TerminalStatuses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancel_and_close_reject_reserve_listed_and_keep_offer_declined(bool cancel)
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        var reserve = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.ReserveListed, 2);
        var declined = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.OfferDeclined, 1);

        var lifecycle = new RecruitmentRequisitionLifecycleService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            Mock.Of<IWorkflowStarter>(),
            Config(),
            Options.Create(new RecruitmentOptions()));

        var (success, error) = cancel
            ? await lifecycle.CancelAsync(req.Id, "إلغاء الشاغر لأسباب تنظيمية", CreatorId, actorCanApprove: true)
            : await lifecycle.CloseAsync(req.Id);
        Assert.True(success, error);

        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.Rejected, (await db.RecruitmentApplications.SingleAsync(a => a.Id == reserve.Id)).Status);
        var declinedNow = await db.RecruitmentApplications.SingleAsync(a => a.Id == declined.Id);
        Assert.Equal(RecruitmentApplicationStatus.OfferDeclined, declinedNow.Status);
        Assert.Null(declinedNow.Notes);
    }
}
