using System.Net;
using HR.Core.Domain.Enums;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using static HRManagement.IntegrationTests.Hcm025ScopeAndPolicyTestHost;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-25 (D10, decision 8): a branch-scoped user (branch-manager + hr-core, scope = branch 1) sees and acts on the
/// recruitment screens only inside their branch: the list shows their unit only, a foreign Details is 404, a POST on a
/// foreign id (route or form field) is refused without changing it, and competitions are central-only. Central HR is
/// unaffected.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Hcm025OrgScopeTests(Hcm025ScopeAndPolicyTestHost fx) : IClassFixture<Hcm025ScopeAndPolicyTestHost>
{
    private static readonly Guid BranchUser = Guid.Parse("25250000-0000-4000-8000-0000000000b1");
    private static readonly Guid CentralUser = Guid.Parse("25250000-0000-4000-8000-0000000000c1");
    private static readonly string BranchRoles = $"{HrManagementRoles.BranchManager},{HrManagementRoles.HrCore}";

    [Fact]
    public async Task Branch_user_lists_only_their_branch_requisitions_and_central_sees_all()
    {
        var own = await fx.SeedRequisitionAsync(fx.Branch1);
        var foreign = await fx.SeedRequisitionAsync(fx.Branch2);
        using var branch = fx.Client(BranchUser, BranchRoles, fx.Branch1);
        using var central = fx.Client(CentralUser, HrManagementRoles.HrCore);

        var branchList = await (await branch.GetAsync("/RecruitmentRequisitions")).Content.ReadAsStringAsync();
        var centralList = await (await central.GetAsync("/RecruitmentRequisitions")).Content.ReadAsStringAsync();

        Assert.Contains(own.RequisitionNumber, branchList, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign.RequisitionNumber, branchList, StringComparison.Ordinal);
        Assert.Contains(own.RequisitionNumber, centralList, StringComparison.Ordinal);
        Assert.Contains(foreign.RequisitionNumber, centralList, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foreign_details_are_404_for_the_branch_user()
    {
        var own = await fx.SeedRequisitionAsync(fx.Branch1);
        var foreign = await fx.SeedRequisitionAsync(fx.Branch2);
        using var branch = fx.Client(BranchUser, BranchRoles, fx.Branch1);

        Assert.Equal(HttpStatusCode.OK, (await branch.GetAsync($"/RecruitmentRequisitions/Details/{own.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await branch.GetAsync($"/RecruitmentRequisitions/Details/{foreign.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await branch.GetAsync($"/SelectionCommittees?requisitionId={foreign.Id:D}")).StatusCode);
    }

    [Fact]
    public async Task Foreign_post_is_refused_and_changes_nothing_while_own_post_works()
    {
        var own = await fx.SeedRequisitionAsync(fx.Branch1);
        var foreign = await fx.SeedRequisitionAsync(fx.Branch2);
        using var branch = fx.Client(BranchUser, BranchRoles, fx.Branch1);
        var token = await TokenAsync(branch, $"/RecruitmentRequisitions/Details/{own.Id:D}");

        var foreignSubmit = await PostAsync(branch, $"/RecruitmentRequisitions/Submit/{foreign.Id:D}", token);
        // A foreign id inside a posted form (application create) is refused the same way.
        var foreignForm = await PostAsync(branch, "/RecruitmentApplications/Create", token,
            ("RequisitionId", foreign.Id.ToString("D")), ("ApplicantFullName", "مرشح"), ("NationalId", "01234567890"));
        var ownSubmit = await PostAsync(branch, $"/RecruitmentRequisitions/Submit/{own.Id:D}", token);

        Assert.Equal(HttpStatusCode.NotFound, foreignSubmit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignForm.StatusCode);
        using (var central = fx.Client(CentralUser, HrManagementRoles.HrCore))
        {
            var centralToken = await TokenAsync(central, $"/RecruitmentRequisitions/Details/{foreign.Id:D}");
            var centralForm = await PostAsync(central, "/RecruitmentApplications/Create", centralToken,
                ("RequisitionId", foreign.Id.ToString("D")), ("ApplicantFullName", "مرشح"), ("NationalId", "01234567890"));
            Assert.NotEqual(HttpStatusCode.NotFound, centralForm.StatusCode);   // control: the route exists, scope refused it
        }

        Assert.Equal(HttpStatusCode.Redirect, ownSubmit.StatusCode);
        Assert.Equal(RequisitionStatus.Draft, await fx.WithDbAsync(db =>
            db.RecruitmentRequisitions.AsNoTracking().Where(r => r.Id == foreign.Id).Select(r => r.Status).SingleAsync()));
        Assert.Equal(RequisitionStatus.PendingApproval, await fx.WithDbAsync(db =>
            db.RecruitmentRequisitions.AsNoTracking().Where(r => r.Id == own.Id).Select(r => r.Status).SingleAsync()));
    }

    [Fact]
    public async Task Competitions_are_central_only()
    {
        using var branch = fx.Client(BranchUser, BranchRoles, fx.Branch1);
        using var central = fx.Client(CentralUser, HrManagementRoles.HrCore);

        Assert.Equal(HttpStatusCode.Forbidden, (await branch.GetAsync("/Competitions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await central.GetAsync("/Competitions")).StatusCode);
    }

    [Fact]
    public async Task Branch_user_with_an_invalid_scope_claim_is_refused()
    {
        var own = await fx.SeedRequisitionAsync(fx.Branch1);
        using var invalid = fx.Client(BranchUser, BranchRoles);   // branch-manager without scope_org_unit_id

        Assert.Equal(HttpStatusCode.Forbidden, (await invalid.GetAsync($"/RecruitmentRequisitions/Details/{own.Id:D}")).StatusCode);
    }
}
