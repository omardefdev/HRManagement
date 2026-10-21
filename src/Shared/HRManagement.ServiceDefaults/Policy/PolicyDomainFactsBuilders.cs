using HRManagement.SharedKernel.Policy;

namespace HRManagement.ServiceDefaults.Policy;

/// <summary>Shared facts builders for policy domains (wave 1 + wave 2 adapters).</summary>
public static class PolicyDomainFactsBuilders
{
    public static PolicyEnforceRequest ForIDCardRequest(
        Guid requestId, Guid? tenantId, Guid employeeId, string purpose, string action) =>
        new()
        {
            Domain = PolicyDomains.IDCardLifecycle,
            TenantId = tenantId,
            SubjectType = "CardRequest",
            Action = action,
            EntityId = requestId,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["employeeId"] = employeeId.ToString(),
                ["purpose"] = purpose,
                ["action"] = action
            }
        };

    public static PolicyEnforceRequest ForLeaveRequest(
        Guid leaveId, Guid? tenantId, Guid employeeId, int durationDays, string leaveType, string action) =>
        new()
        {
            Domain = PolicyDomains.ContractsLeave,
            TenantId = tenantId,
            SubjectType = "LeaveRequest",
            Action = action,
            EntityId = leaveId,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["employeeId"] = employeeId.ToString(),
                ["durationDays"] = durationDays.ToString(),
                ["leaveType"] = leaveType,
                ["action"] = action
            }
        };

    public static PolicyEnforceRequest ForAdvanceRequest(
        Guid advanceId, Guid? tenantId, Guid employeeId, decimal amount, string action) =>
        new()
        {
            Domain = PolicyDomains.ContractsAdvance,
            TenantId = tenantId,
            SubjectType = "AdvanceRequest",
            Action = action,
            EntityId = advanceId,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["employeeId"] = employeeId.ToString(),
                ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["action"] = action
            }
        };

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
        string? contextJson = null) =>
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
                ["recruitmentApplicationId"] = recruitmentApplicationId.ToString()
            }
        };

    public static PolicyEnforceRequest ForEstablishmentChange(
        Guid changeRequestId,
        Guid? tenantId,
        string action,
        string changeType,
        string changeStatus,
        bool hasOrgScope = false,
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
                ["hasOrgScope"] = hasOrgScope ? "true" : "false"
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
