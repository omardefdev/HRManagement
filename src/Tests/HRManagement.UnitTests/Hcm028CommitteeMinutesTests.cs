using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Services;
using static HRManagement.UnitTests.Hcm028TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 (D): committee minutes data and PDF.</summary>
public class Hcm028CommitteeMinutesTests
{
    [Fact]
    public async Task Minutes_pdf_builds_for_an_approved_session()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 80, 40);
        var (session, _) = await SeedReadySessionAsync(db, req, candidates);
        var committee = new SelectionCommitteeService(db);
        Assert.True((await committee.UpsertDecisionAsync(
            session.Id, candidates[2].Id, CommitteeRecommendation.Reject, null, "دون الحد الأدنى للمقابلة")).Success);
        await ApproveAllStepsAsync(db, session.Id);

        var (minutes, error) = await new SelectionCommitteeMinutesService(db, committee).GetMinutesAsync(session.Id);

        Assert.Null(error);
        Assert.NotNull(minutes);
        Assert.Equal(req.RequisitionNumber, minutes!.RequisitionNumber);
        Assert.Equal(3, minutes.Members.Count);
        Assert.Single(minutes.Members, m => m.IsChair);
        Assert.Equal(3, minutes.Candidates.Count);
        Assert.Equal(CommitteeRecommendation.Reject, minutes.Candidates[^1].Recommendation);
        Assert.All(minutes.Candidates, c => Assert.Equal(3, c.Votes.Agree));
        Assert.Equal(3, minutes.Approvals.Count);
        Assert.NotNull(minutes.ApprovedAt);

        var pdf = SelectionCommitteeMinutesDocument.Generate(minutes);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Minutes_are_not_available_before_submit()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var session = await SeedSessionAsync(db, req);

        var (minutes, error) = await new SelectionCommitteeMinutesService(db, new SelectionCommitteeService(db))
            .GetMinutesAsync(session.Id);

        Assert.Null(minutes);
        Assert.Equal(SelectionCommitteeMinutesService.NotSubmittedMessage, error);
    }
}
