using System.Text.Json;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-33: the deterministic recruitment states of <c>testdata/acceptance/recruitment-states.json</c>
/// (seeded by <c>AcceptanceRecruitmentStatesPatch</c>). Scenarios read their ids here instead of scraping HTML.
/// </summary>
public sealed record RecruitmentStates(
    RecruitmentStates.AgencyInfo Agency,
    RecruitmentStates.PersonaRoles Personas,
    RecruitmentStates.RequisitionSet Requisitions,
    RecruitmentStates.ApplicationSet Applications,
    RecruitmentStates.OfferSet Offers,
    RecruitmentStates.AppealInfo Appeal,
    RecruitmentStates.CommitteeInfo Committee,
    RecruitmentStates.LegacyApproval LegacyApprovalRequest,
    RecruitmentStates.PositionPoolInfo PositionPool)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<RecruitmentStates> Cached = new(Read);

    public static RecruitmentStates Load() => Cached.Value;

    private static RecruitmentStates Read()
    {
        var path = AcceptanceHarness.ResolveDataPath(Path.Combine("testdata", "acceptance", "recruitment-states.json"));
        Assert.True(File.Exists(path), $"Missing recruitment states at {path}");
        var states = JsonSerializer.Deserialize<RecruitmentStates>(File.ReadAllText(path), Options);
        Assert.NotNull(states);
        return states!;
    }

    public sealed record AgencyInfo(string Code, Guid TenantId, Guid ScopeOrgUnitId);

    public sealed record PersonaRoles(string Creator, string Step1Approver, string Step2Approver, string AppealReviewer, string OtherReviewer);

    public sealed record Requisition(Guid Id, string Number, string Status, Guid? WorkflowInstanceId, int? CurrentStep, Guid? PostingId,
        Guid? CriteriaId, DateOnly? ApplicationClosingDate);

    public sealed record RequisitionSet(Requisition Draft, Requisition PendingStep1, Requisition PendingStep2, Requisition Approved,
        Requisition Published);

    public sealed record ApplicationSet(Guid Submitted, Guid Shortlisted, Guid Assessed, Guid Ranked1, Guid Ranked2, Guid Ranked3,
        Guid OfferPending, Guid OfferIssued, Guid RejectedWithAppeal);

    public sealed record Offer(Guid Id, string Number, Guid? WorkflowInstanceId, Guid ApplicationId);

    public sealed record OfferSet(Offer PendingApproval, Offer Issued);

    public sealed record AppealInfo(Guid Id, Guid ApplicationId, Guid WorkflowInstanceId);

    public sealed record CommitteeInfo(Guid RequisitionId, Guid ReadySessionId, Guid ShortSessionId);

    public sealed record LegacyApproval(Guid Id, string RoleRequired);

    public sealed record PositionPoolInfo(string CodePrefix, int Size, Guid FirstId, Guid LastId);
}
