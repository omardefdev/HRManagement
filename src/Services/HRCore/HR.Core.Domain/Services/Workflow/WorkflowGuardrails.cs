namespace HR.Core.Domain.Services.Workflow;

/// <summary>What a definition of one entity type must keep: the floor of steps, the role of the last step, mandatory roles.</summary>
public sealed record WorkflowGuardrail(
    string EntityType,
    int MinSteps,
    string? LastStepRole = null,
    IReadOnlyList<string>? RequiredRoles = null,
    IReadOnlyDictionary<string, int>? MinStepsByCode = null,
    bool RequiresDistinctApprovers = false,
    string? FirstStepRole = null,
    int? MaxSteps = null)
{
    public int MinStepsFor(string? workflowCode) =>
        workflowCode is not null && MinStepsByCode is not null && MinStepsByCode.TryGetValue(workflowCode, out var min)
            ? min
            : MinSteps;
}

/// <summary>
/// PLAN-43 (G-WF-04): the limits of each entity type, a tested table kept in code. They are a working default reviewed with the
/// owner, not a legal value. PLAN-45 (D-WF-2): custody and loss are exactly the logistics officer, then the management approver.
/// </summary>
public static class WorkflowGuardrails
{
    private const string ManagementApprover = "hr-management-approver";
    private const string Compliance = "compliance";
    private const string AdminLogistics = "admin-logistics";

    private static readonly IReadOnlyDictionary<string, WorkflowGuardrail> Table =
        new WorkflowGuardrail[]
        {
            new("RecruitmentRequisition", 2, ManagementApprover),
            new("EstablishmentChangeRequest", 2, ManagementApprover),
            new("RecruitmentCompetition", 2, ManagementApprover),
            new("CompetitionResultVersion", 2, ManagementApprover),
            new("OfferLetter", 2, ManagementApprover, MinStepsByCode: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["RECRUITMENT-OFFER-OVERRIDE-APPROVAL"] = 3
            }),
            new("SelectionCommitteeSession", 3, ManagementApprover),
            new("RecruitmentAppeal", 1, Compliance),
            new("AssetAssignment", 2, ManagementApprover, RequiresDistinctApprovers: true, FirstStepRole: AdminLogistics, MaxSteps: 2),
            new("AssetLossReport", 2, ManagementApprover, RequiresDistinctApprovers: true, FirstStepRole: AdminLogistics, MaxSteps: 2)
        }.ToDictionary(g => g.EntityType, StringComparer.OrdinalIgnoreCase);

    /// <summary>Any other known type: one step, and the roles still come from the catalog.</summary>
    public static WorkflowGuardrail For(string entityType) =>
        Table.TryGetValue(entityType, out var guardrail) ? guardrail : new WorkflowGuardrail(entityType, 1);

    public static IReadOnlyCollection<WorkflowGuardrail> All => [.. Table.Values];
}
