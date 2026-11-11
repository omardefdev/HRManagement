using HRManagement.SharedKernel.Policy;

namespace HRManagement.ServiceDefaults.Policy;

/// <summary>
/// The facts each call site of a governed domain sends the policy engine. PLAN-49 (G-POL-04): every key a builder sends is a
/// field of its domain's facts schema (the strict evaluation refuses a request that carries any other key), and every field a
/// rule of the domain reads is sent by the builder of the action the rule is bound to. Pol049FactsBuildersMatchSchemasTests
/// holds both. A builder exists only for a domain the catalog governs.
/// </summary>
public static class PolicyDomainFactsBuilders
{
    /// <summary><paramref name="tenantId"/>: the card request's own tenant.</summary>
    public static PolicyEnforceRequest ForIDCardRequest(Guid requestId, Guid? tenantId, string purpose, string action) =>
        new()
        {
            Domain = PolicyDomains.IDCardLifecycle,
            TenantId = tenantId,
            SubjectType = "CardRequest",
            Action = action,
            EntityId = requestId,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["purpose"] = purpose,
                ["action"] = action
            }
        };

    /// <summary>
    /// <paramref name="tenantId"/>: the leave request's own tenant (empty for a request not saved yet: the engine then takes the
    /// tenant of the call). <paramref name="durationDays"/>: working days, on every channel. <paramref name="isSelfApproval"/>:
    /// sent with <c>Approve</c> only: whether the approver decides a leave that is his own or that he recorded.
    /// </summary>
    public static PolicyEnforceRequest ForLeaveRequest(
        Guid leaveId, Guid? tenantId, int durationDays, string leaveType, string action, bool? isSelfApproval = null)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["durationDays"] = durationDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["leaveType"] = leaveType,
            ["action"] = action
        };
        if (isSelfApproval is bool self)
            facts["isSelfApproval"] = self ? "true" : "false";

        return new PolicyEnforceRequest
        {
            Domain = PolicyDomains.ContractsLeave,
            TenantId = tenantId,
            SubjectType = "LeaveRequest",
            Action = action,
            EntityId = leaveId,
            Facts = facts
        };
    }

    public static PolicyEnforceRequest ForRecruitmentRequisition(
        Guid requisitionId,
        Guid? tenantId,
        string action,
        string requisitionStatus,
        bool positionVacant,
        Guid fundedPositionId,
        bool hasOrgScope = true,
        string? contextJson = null) =>
        new()
        {
            Domain = PolicyDomains.CoreRecruitment,
            TenantId = tenantId,
            SubjectType = "RecruitmentRequisition",
            Action = action,
            EntityId = requisitionId,
            ContextJson = contextJson,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = action,
                ["requisitionStatus"] = requisitionStatus,
                ["positionVacant"] = positionVacant ? "true" : "false",
                ["fundedPositionId"] = fundedPositionId.ToString(),
                ["hasOrgScope"] = hasOrgScope ? "true" : "false"
            }
        };

    public static PolicyEnforceRequest ForRecruitmentApplication(
        Guid applicationId,
        Guid? tenantId,
        string action,
        bool hasAcceptedOffer,
        bool coiDeclared,
        bool rankAssigned,
        string? requisitionStatus,
        bool hasOrgScope,
        string? contextJson = null) =>
        new()
        {
            Domain = PolicyDomains.CoreRecruitment,
            TenantId = tenantId,
            SubjectType = "RecruitmentApplication",
            Action = action,
            EntityId = applicationId,
            ContextJson = contextJson,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = action,
                ["hasAcceptedOffer"] = hasAcceptedOffer ? "true" : "false",
                ["coiDeclared"] = coiDeclared ? "true" : "false",
                ["rankAssigned"] = rankAssigned ? "true" : "false",
                ["requisitionStatus"] = requisitionStatus ?? string.Empty,
                ["hasOrgScope"] = hasOrgScope ? "true" : "false"
            }
        };

    public static PolicyEnforceRequest ForOfferLetter(
        Guid offerId,
        Guid? tenantId,
        string action,
        Guid recruitmentApplicationId,
        string? contextJson = null,
        bool isMeritOverride = false,
        bool isExceptionOffer = false) =>
        new()
        {
            Domain = PolicyDomains.CoreRecruitment,
            TenantId = tenantId,
            SubjectType = "OfferLetter",
            Action = action,
            EntityId = offerId,
            ContextJson = contextJson,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = action,
                ["recruitmentApplicationId"] = recruitmentApplicationId.ToString(),
                ["isMeritOverride"] = isMeritOverride ? "true" : "false",
                // HCM-027 (E1): merit override, grade deviation or unverified salary.
                ["isExceptionOffer"] = isExceptionOffer || isMeritOverride ? "true" : "false"
            }
        };

    /// <summary>
    /// <paramref name="actorIsBranchScoped"/>: whether the person who submits is limited to a branch
    /// (<c>HrOrgScopeVisibility.IsBranchScoped</c>). It is the fact <c>hasOrgScope</c> of this domain, which R-EST-DENY-BRANCH
    /// reads; it has no default, so no call site sends a constant.
    /// </summary>
    public static PolicyEnforceRequest ForEstablishmentChange(
        Guid changeRequestId,
        Guid? tenantId,
        string action,
        string changeType,
        string changeStatus,
        bool actorIsBranchScoped,
        string? contextJson = null) =>
        new()
        {
            Domain = PolicyDomains.CoreEstablishment,
            TenantId = tenantId,
            SubjectType = "EstablishmentChangeRequest",
            Action = action,
            EntityId = changeRequestId,
            ContextJson = contextJson,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = action,
                ["changeType"] = changeType,
                ["changeStatus"] = changeStatus,
                ["hasOrgScope"] = actorIsBranchScoped ? "true" : "false"
            }
        };

    public static PolicyEnforceRequest ForGeneric(
        string domain, string subjectType, string action, Guid entityId, Guid? tenantId,
        IReadOnlyDictionary<string, string>? facts = null) =>
        new()
        {
            Domain = domain,
            TenantId = tenantId,
            SubjectType = subjectType,
            Action = action,
            EntityId = entityId,
            Facts = facts is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["action"] = action }
                : new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase)
        };
}
