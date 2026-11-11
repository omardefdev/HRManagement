namespace HRManagement.SharedKernel.Policy;

/// <summary>PLAN-49 (D-POL-1): whether the policy engine governs a domain.</summary>
public enum PolicyDomainStatus
{
    /// <summary>The engine decides (or, in Shadow, records) at a proved call site.</summary>
    Governed,

    /// <summary>No call site: retired from the seeds, the page and the configuration, and documented with its reason.</summary>
    OutOfScope
}

/// <summary>PLAN-49: who starts the workflow a rule of the domain names.</summary>
public enum PolicyWorkflowStartPath
{
    /// <summary>HR.Core's own approval start (the domain runs inside HR.Core); the service names the exact code (G-POL-09).</summary>
    HrCoreCoordinator,

    /// <summary>The owning service, through the cross-service approval contract (PLAN-43, 45, 46); the rule is a signal only.</summary>
    OwnerApprovalContract,

    /// <summary>The internal enforce endpoint itself (PLAN-42). No trigger has used it since PLAN-43.</summary>
    InternalEndpoint
}

/// <summary>
/// A workflow code a rule may name for an entity type, who starts it, and at which action of that type it starts. A rule that
/// could match another action of the type (publishing a requisition, say) may not name the code: the call site of that action
/// takes no instance, so one started there would wait in an inbox for nobody (G-POL-11).
/// </summary>
public sealed record PolicyWorkflowTrigger(string WorkflowCode, PolicyWorkflowStartPath StartedBy, string Action);

/// <summary>
/// PLAN-49 (G-POL-02): everything the system says about one policy domain. The validator, the engine, the internal endpoint, the
/// start-up check, the policy page and docs/POLICY_DOMAIN_REGISTRY.md all read this, and nothing else.
/// </summary>
public sealed class PolicyDomainDescriptor
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NoActions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<PolicyWorkflowTrigger>> NoTriggers =
        new Dictionary<string, IReadOnlyList<PolicyWorkflowTrigger>>(StringComparer.Ordinal);

    public required string Domain { get; init; }

    public required PolicyDomainStatus Status { get; init; }

    /// <summary>The service the domain belongs to (for an out-of-scope domain: the service it was written for).</summary>
    public required string OwnerService { get; init; }

    /// <summary>
    /// The service client that may ask HR.Core for a decision over HTTP; null for <c>Core.*</c> (those run inside HR.Core) and for
    /// an out-of-scope domain (nobody may ask).
    /// </summary>
    public string? OwnerClientId { get; init; }

    /// <summary>Entity type → the actions a call site sends for it.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> SubjectActions { get; init; } = NoActions;

    /// <summary>The effects a call site of the domain consumes. A rule with any other effect is refused.</summary>
    public IReadOnlySet<PolicyEffect> AllowedEffects { get; init; } = new HashSet<PolicyEffect>();

    /// <summary>The fields a SetField effect may write.</summary>
    public IReadOnlySet<string> SetFields { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Entity type → the workflow codes a rule may name for it.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PolicyWorkflowTrigger>> WorkflowTriggers { get; init; } = NoTriggers;

    /// <summary>Repository-relative files that ask the engine for this domain (checked by Pol049PolicyDomainCatalogTests).</summary>
    public IReadOnlyList<string> CallSiteFiles { get; init; } = [];

    /// <summary>Why the engine does not govern the domain (Arabic, shown on the policy page); null when governed.</summary>
    public string? OutOfScopeReasonAr { get; init; }

    public bool IsGoverned => Status == PolicyDomainStatus.Governed;

    /// <summary>Every action of the domain, whatever its entity type.</summary>
    public IReadOnlySet<string> Actions =>
        new HashSet<string>(SubjectActions.Values.SelectMany(a => a), StringComparer.Ordinal);

    public bool AllowsSubjectType(string? subjectType) =>
        subjectType is not null && SubjectActions.ContainsKey(subjectType);

    public bool AllowsAction(string? subjectType, string? action) =>
        subjectType is not null && action is not null
        && SubjectActions.TryGetValue(subjectType, out var actions) && actions.Contains(action);

    /// <summary>The entity types that have the action.</summary>
    public IEnumerable<string> SubjectTypesOf(string action) =>
        SubjectActions.Where(kv => kv.Value.Contains(action)).Select(kv => kv.Key);

    /// <summary>Whether the entity type owns the code (at any of its actions).</summary>
    public bool AllowsWorkflowCode(string? subjectType, string? workflowCode) =>
        subjectType is not null && workflowCode is not null
        && WorkflowTriggers.TryGetValue(subjectType, out var triggers)
        && triggers.Any(t => string.Equals(t.WorkflowCode, workflowCode, StringComparison.Ordinal));

    /// <summary>Whether a rule may name the code for the entity type at this action: the action is the one that starts it.</summary>
    public bool AllowsWorkflowStart(string? subjectType, string? action, string? workflowCode) =>
        subjectType is not null && action is not null && workflowCode is not null
        && WorkflowTriggers.TryGetValue(subjectType, out var triggers)
        && triggers.Any(t => string.Equals(t.WorkflowCode, workflowCode, StringComparison.Ordinal)
                             && string.Equals(t.Action, action, StringComparison.Ordinal));

    /// <summary>
    /// Whether the engine itself starts the workflow for the entity type at this action: a trigger of HR.Core's own approval
    /// start. A workflow of an owner service (the cross-service approval contract) is a signal in the decision and starts at
    /// its owner, never in the engine.
    /// </summary>
    public bool StartsInsideHrCore(string? subjectType, string? action, string? workflowCode) =>
        subjectType is not null && action is not null && workflowCode is not null
        && WorkflowTriggers.TryGetValue(subjectType, out var triggers)
        && triggers.Any(t => t.StartedBy == PolicyWorkflowStartPath.HrCoreCoordinator
                             && string.Equals(t.WorkflowCode, workflowCode, StringComparison.Ordinal)
                             && string.Equals(t.Action, action, StringComparison.Ordinal));
}

/// <summary>
/// PLAN-49 (G-POL-02, D-POL-1): the one source of truth about the policy engine's domains. Five are governed (each has a proved
/// call site and a seeded national template, which may hold no rule, as the card domain's does, and runs in Shadow in
/// production files); fifteen are out of scope (no call site: their templates
/// are retired by a final version with no rules, and their mode is Off whatever the configuration says). Extending the engine
/// later means a descriptor here, a call site, and a seed change set.
/// </summary>
public static class PolicyDomainCatalog
{
    /// <summary>What every refusal about an out-of-scope domain says (the validator, the engine, the endpoint, the page).</summary>
    public const string OutOfScopeMessageAr = "المجال خارج نطاق المحرك";

    public const string HrCoreService = "HR.Core";
    public const string AdminLogisticsService = "Admin.Logistics";
    public const string IdCardService = "IDCard";
    public const string HrContractsService = "HR.Contracts";

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);

    private static IReadOnlySet<PolicyEffect> Effects(params PolicyEffect[] effects) => new HashSet<PolicyEffect>(effects);

    private static IReadOnlyList<PolicyWorkflowTrigger> Started(PolicyWorkflowStartPath by, string action, params string[] codes) =>
        codes.Select(code => new PolicyWorkflowTrigger(code, by, action)).ToList();

    private static PolicyDomainDescriptor OutOfScope(string domain, string ownerService, string reasonAr) =>
        new()
        {
            Domain = domain,
            Status = PolicyDomainStatus.OutOfScope,
            OwnerService = ownerService,
            OutOfScopeReasonAr = reasonAr
        };

    private static readonly IReadOnlyList<PolicyDomainDescriptor> Descriptors =
    [
        new()
        {
            Domain = PolicyDomains.AssetsCustody,
            Status = PolicyDomainStatus.Governed,
            OwnerService = AdminLogisticsService,
            OwnerClientId = "svc-admin-logistics",
            SubjectActions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["Asset"] = Set("Classify", "Create"),
                ["AssetAssignment"] = Set("Assign")
            },
            // Classify reads SetField(isPrivileged); Create reads SelectCodePolicy; Assign reads RequireEvidence and TriggerWorkflow.
            AllowedEffects = Effects(
                PolicyEffect.Allow, PolicyEffect.Deny, PolicyEffect.RequireEvidence, PolicyEffect.TriggerWorkflow,
                PolicyEffect.SetField, PolicyEffect.SelectCodePolicy),
            SetFields = Set("isPrivileged"),
            WorkflowTriggers = new Dictionary<string, IReadOnlyList<PolicyWorkflowTrigger>>(StringComparer.Ordinal)
            {
                ["AssetAssignment"] = Started(PolicyWorkflowStartPath.OwnerApprovalContract, "Assign", "ASSET-ASSIGN-APPROVAL")
            },
            CallSiteFiles =
            [
                "src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/AssetClassifier.cs",
                "src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs"
            ]
        },
        new()
        {
            Domain = PolicyDomains.IDCardLifecycle,
            Status = PolicyDomainStatus.Governed,
            OwnerService = IdCardService,
            OwnerClientId = "svc-idcard",
            SubjectActions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["CardRequest"] = Set("Process")
            },
            // The card's approval starts through the cross-service contract (PLAN-46), never from a rule: no trigger here.
            AllowedEffects = Effects(PolicyEffect.Allow, PolicyEffect.Deny),
            CallSiteFiles = ["src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs"]
        },
        new()
        {
            Domain = PolicyDomains.ContractsLeave,
            Status = PolicyDomainStatus.Governed,
            OwnerService = HrContractsService,
            OwnerClientId = "svc-hr-contracts",
            SubjectActions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["LeaveRequest"] = Set("Create", "Update", "Approve")
            },
            AllowedEffects = Effects(PolicyEffect.Allow, PolicyEffect.Deny),
            // The HR page (create, update, approve) and the self-service request that arrives as ILeaveRequested (create).
            CallSiteFiles =
            [
                "src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs",
                "src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs"
            ]
        },
        new()
        {
            Domain = PolicyDomains.CoreRecruitment,
            Status = PolicyDomainStatus.Governed,
            OwnerService = HrCoreService,
            SubjectActions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["RecruitmentRequisition"] = Set("SubmitForApproval", "Publish", "PublishInternal"),
                ["RecruitmentApplication"] = Set("Hire"),
                ["OfferLetter"] = Set("Issue"),
                ["SelectionCommitteeSession"] = Set("SubmitCommittee"),
                ["RecruitmentAppeal"] = Set("FileAppeal")
            },
            AllowedEffects = Effects(PolicyEffect.Allow, PolicyEffect.Deny, PolicyEffect.TriggerWorkflow),
            WorkflowTriggers = new Dictionary<string, IReadOnlyList<PolicyWorkflowTrigger>>(StringComparer.Ordinal)
            {
                // The requisition's approval starts when it is submitted; publishing it starts nothing.
                ["RecruitmentRequisition"] = Started(
                    PolicyWorkflowStartPath.HrCoreCoordinator, "SubmitForApproval", "RECRUITMENT-REQUISITION-APPROVAL"),
                ["OfferLetter"] = Started(
                    PolicyWorkflowStartPath.HrCoreCoordinator, "Issue", "RECRUITMENT-OFFER-APPROVAL", "RECRUITMENT-OFFER-OVERRIDE-APPROVAL"),
                ["SelectionCommitteeSession"] = Started(
                    PolicyWorkflowStartPath.HrCoreCoordinator, "SubmitCommittee", "RECRUITMENT-SELECTION-COMMITTEE"),
                ["RecruitmentAppeal"] = Started(PolicyWorkflowStartPath.HrCoreCoordinator, "FileAppeal", "RECRUITMENT-APPEAL-REVIEW")
            },
            CallSiteFiles =
            [
                "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionLifecycleService.cs",
                "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs",
                "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeLifecycleService.cs",
                "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealService.cs",
                "src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentApplicationsController.cs"
            ]
        },
        new()
        {
            Domain = PolicyDomains.CoreEstablishment,
            Status = PolicyDomainStatus.Governed,
            OwnerService = HrCoreService,
            SubjectActions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["EstablishmentChangeRequest"] = Set("SubmitForApproval")
            },
            AllowedEffects = Effects(PolicyEffect.Allow, PolicyEffect.Deny, PolicyEffect.TriggerWorkflow),
            WorkflowTriggers = new Dictionary<string, IReadOnlyList<PolicyWorkflowTrigger>>(StringComparer.Ordinal)
            {
                ["EstablishmentChangeRequest"] = Started(
                    PolicyWorkflowStartPath.HrCoreCoordinator, "SubmitForApproval", "ESTABLISHMENT-CHANGE-APPROVAL")
            },
            CallSiteFiles = ["src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs"]
        },

        OutOfScope(PolicyDomains.AssetsProcurement, AdminLogisticsService,
            "لا نقطة استدعاء: استلام التوريد تحكمه قواعد Admin.Logistics المحلية."),
        OutOfScope(PolicyDomains.ContractsAdvance, HrContractsService,
            "لا نقطة استدعاء: السلف تحكمها قواعد HR.Contracts المحلية، وقاعدتها المبذورة كانت بأثر لا يستهلكه أحد (RequireDualApproval)."),
        OutOfScope(PolicyDomains.ContractsHire, HrContractsService,
            "لا نقطة استدعاء: التعيين يحكمه اعتماد خطاب العرض، ومسار HIRE-WIZARD مسحوب (PLAN-46)."),
        OutOfScope(PolicyDomains.ContractsSeparation, HrContractsService,
            "لا نقطة استدعاء: اعتماد الانفصال مسجَّل في العقد العابر للخدمات (PLAN-46، ويكمله PLAN-60)، ومسار SEPARATION-WIZARD مسحوب."),
        OutOfScope(PolicyDomains.ContractsContract, HrContractsService,
            "لا نقطة استدعاء: تجديد العقد تحكمه قواعد HR.Contracts المحلية، وقاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequireApproval)."),
        OutOfScope(PolicyDomains.MovementsTransfer, "HR.Movements",
            "لا نقطة استدعاء: مسار TRANSFER-APPROVAL مسحوب (PLAN-46)، والنقل تحكمه قواعد HR.Movements المحلية."),
        OutOfScope(PolicyDomains.MovementsWorkforce, "HR.Movements",
            "لا نقطة استدعاء ولا قواعد مبذورة."),
        OutOfScope(PolicyDomains.CoreEmployment, HrCoreService,
            "لا نقطة استدعاء ولا قواعد مبذورة."),
        OutOfScope(PolicyDomains.HealthInsurance, "HR.Health",
            "لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequirePrivacyMask)."),
        OutOfScope(PolicyDomains.ComplianceDisciplinary, "Compliance",
            "لا نقطة استدعاء: اعتماد الإجراء التأديبي يمر بالعقد العابر للخدمات (PLAN-46) لا بقاعدة سياسة."),
        OutOfScope(PolicyDomains.PayrollPeriod, "Payroll",
            "لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequireDualApproval)."),
        OutOfScope(PolicyDomains.ITTickets, "IT.Infra",
            "لا نقطة استدعاء: مسار IT-TICKET مسحوب (PLAN-46)."),
        OutOfScope(PolicyDomains.ArchiveRetention, "Records.Archive",
            "لا نقطة استدعاء: الاحتفاظ والحجز القانوني تحكمهما قواعد Records.Archive المحلية."),
        OutOfScope(PolicyDomains.TrainingLearning, "HR.Training",
            "لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (BlockUntilChecklist)."),
        OutOfScope(PolicyDomains.AnalyticsEtl, "Dev.Analytics",
            "لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequireApproval).")
    ];

    private static readonly IReadOnlyDictionary<string, PolicyDomainDescriptor> ByDomain =
        Descriptors.ToDictionary(d => d.Domain, StringComparer.Ordinal);

    /// <summary>One descriptor per entry of <see cref="PolicyDomains.All"/>, governed first.</summary>
    public static IReadOnlyList<PolicyDomainDescriptor> All => Descriptors;

    public static IEnumerable<PolicyDomainDescriptor> Governed => Descriptors.Where(d => d.IsGoverned);

    public static IEnumerable<PolicyDomainDescriptor> OutOfScopeDomains => Descriptors.Where(d => !d.IsGoverned);

    /// <summary>The descriptor of a domain, by its exact name; null for a name that is not a policy domain.</summary>
    public static PolicyDomainDescriptor? Find(string? domain) =>
        domain is not null && ByDomain.TryGetValue(domain, out var descriptor) ? descriptor : null;

    /// <summary>As <see cref="Find"/>, for a configuration key (configuration keys ignore case).</summary>
    public static PolicyDomainDescriptor? FindByConfigurationKey(string? key) =>
        key is null ? null : Descriptors.FirstOrDefault(d => string.Equals(d.Domain, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>True only for a governed domain: an out-of-scope one and an unknown name are both outside the engine.</summary>
    public static bool IsGoverned(string? domain) => Find(domain) is { IsGoverned: true };

    /// <summary>True for a known domain the engine does not govern.</summary>
    public static bool IsOutOfScope(string? domain) => Find(domain) is { IsGoverned: false };
}
