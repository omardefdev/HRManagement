using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

public sealed class WorkflowInboxQueryService(
    HRCoreDbContext db,
    WorkflowInboxEligibilityService eligibility,
    WorkflowDeepLinkResolver deepLinkResolver,
    OrgScopeService orgScope,
    // PLAN-50 (§6): a delegate sees his principal's items; optional so manual constructions stay role-only.
    HRManagement.ServiceDefaults.Delegation.IApprovalDelegationLookup? delegationLookup = null)
{
    /// <summary>The user's delegations in force (none on any failure), and each stored principal scope among them.</summary>
    private async Task<(IReadOnlyList<HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation> Delegations,
        IReadOnlyDictionary<Guid, OrgAccessScope> PrincipalScopes)> DelegationsOfAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (delegationLookup is null || ServiceCallerPrincipal.IsServiceAccount(user))
            return ([], new Dictionary<Guid, OrgAccessScope>());

        IReadOnlyList<HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation> delegations;
        try
        {
            delegations = await delegationLookup.GetActiveForAsync(user, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-closed: no delegation; the user's own items are listed as before.
            return ([], new Dictionary<Guid, OrgAccessScope>());
        }

        var scopes = new Dictionary<Guid, OrgAccessScope>();
        foreach (var delegation in delegations.Where(d => d.PrincipalScopeOrgUnitId is not null))
        {
            var root = delegation.PrincipalScopeOrgUnitId!.Value;
            if (!scopes.ContainsKey(root))
                scopes[root] = await orgScope.GetAllowedOrgUnitIdsAsync(
                    HR.Core.Infrastructure.Services.Workflow.WorkflowActionActor.PrincipalOf(delegation).ToPrincipal(), ct);
        }

        return (delegations, scopes);
    }

    private static readonly string[] SupportedEntityTypes =
    [
        "RecruitmentRequisition",
        "OfferLetter",
        "SelectionCommitteeSession",
        "RecruitmentAppeal",
        "EstablishmentChangeRequest",
        .. ExternalApprovalTypes.All.Select(t => t.EntityType)
    ];

    private static readonly string[] AppealOnlyEntityTypes = ["RecruitmentAppeal"];

    public async Task<IReadOnlyList<WorkInboxItem>> GetActionableWorkflowItemsAsync(
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        if (userId is null)
            return [];

        var accessScope = await orgScope.GetAllowedOrgUnitIdsAsync(user, ct);

        var entityTypes = EntityTypesFor(user);
        var instances = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Status == WorkflowStatus.InProgress
                        && entityTypes.Contains(i.EntityType))
            .OrderBy(i => i.StartedAt)
            .ToListAsync(ct);

        if (instances.Count == 0)
            return [];

        var definitionKeys = instances
            .Select(i => (i.DefinitionId, i.DefinitionVersion, i.TenantId))
            .Distinct()
            .ToList();

        var definitions = await LoadDefinitionsAsync(definitionKeys, ct);
        var contexts = await LoadEntityContextsAsync(instances, ct);
        var (delegations, principalScopes) = await DelegationsOfAsync(user, ct);

        var eligiblePairs = new List<(WorkflowInstance Instance, WorkflowDefinition Definition)>();
        var onBehalf = new Dictionary<Guid, HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation>();
        foreach (var instance in instances)
        {
            if (!definitions.TryGetValue((instance.DefinitionId, instance.DefinitionVersion, instance.TenantId), out var definition))
                continue;

            if (!contexts.TryGetValue(instance.Id, out var context))
                continue;

            var (actionable, delegation) = eligibility.Evaluate(
                instance, definition, user, userId, context, accessScope, delegations, principalScopes);
            if (!actionable)
                continue;

            eligiblePairs.Add((instance, definition));
            if (delegation is not null)
                onBehalf[instance.Id] = delegation;
        }

        if (eligiblePairs.Count == 0)
            return [];

        var links = await deepLinkResolver.ResolveAsync(eligiblePairs, ct);
        return eligiblePairs
            .Select(pair =>
            {
                links.TryGetValue(pair.Instance.Id, out var link);
                link ??= new WorkflowDeepLinkInfo(
                    $"/WorkflowInstances/Details/{pair.Instance.Id}",
                    pair.Definition.Name,
                    pair.Instance.EntityType,
                    pair.Instance.EntityType,
                    null,
                    pair.Definition.Name);
                var item = WorkInboxItemMapper.FromWorkflow(pair.Instance, pair.Definition, link, userId.Value);
                // PLAN-50 (§6): «بالنيابة عن {الأصيل}».
                if (onBehalf.TryGetValue(pair.Instance.Id, out var delegation))
                {
                    item.DelegatedFromId = delegation.PrincipalUserId.ToString("D");
                    item.OnBehalfOfName = delegation.PrincipalName ?? delegation.PrincipalUserId.ToString("D");
                }

                return item;
            })
            .ToList();
    }

    /// <summary>
    /// HCM-028 (F4): the compliance reviewer only sees the appeal reviews assigned to them. PLAN-45: a role from outside HR.Core sees its
    /// external types and nothing else.
    /// </summary>
    private static IReadOnlyList<string> EntityTypesFor(ClaimsPrincipal user)
    {
        if (RecruitmentAppealAccess.IsComplianceOnly(user))
            return [.. AppealOnlyEntityTypes, .. WorkInboxAudience.EntityTypesFor(user)];

        return WorkInboxAudience.IsAudienceOnly(user) ? WorkInboxAudience.EntityTypesFor(user) : SupportedEntityTypes;
    }

    public async Task<IReadOnlyList<WorkInboxItem>> GetActionableLegacyItemsAsync(
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        if (userId is null || RecruitmentAppealAccess.IsComplianceOnly(user) || WorkInboxAudience.IsAudienceOnly(user))
            return [];

        var requests = await db.ApprovalRequests.AsNoTracking()
            .Include(r => r.Steps)
            .Where(r => r.Status == ApprovalStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);

        var items = new List<WorkInboxItem>();
        foreach (var request in requests)
        {
            if (!IsLegacyApprovalActionable(request, user, userId, eligibility.AdminBypass))
                continue;

            var currentStep = request.Steps
                .FirstOrDefault(s => s.Level == request.CurrentLevel && s.Status == ApprovalStatus.Pending);
            if (currentStep is null)
                continue;

            items.Add(WorkInboxItemMapper.FromLegacyApproval(request, currentStep, userId.Value));
        }

        return items;
    }

    public async Task<IReadOnlyList<WorkInboxItem>> GetActionableItemsAsync(
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var workflowItems = await GetActionableWorkflowItemsAsync(user, ct);
        var legacyItems = await GetActionableLegacyItemsAsync(user, ct);
        return workflowItems.Concat(legacyItems).OrderBy(i => i.CreatedAt).ToList();
    }

    internal static bool IsLegacyApprovalActionable(
        ApprovalRequest request,
        ClaimsPrincipal user,
        Guid? userId,
        bool adminBypass = false)
    {
        if (userId.HasValue && request.RequestedBy == userId)
            return false;

        var currentStep = request.Steps
            .FirstOrDefault(s => s.Level == request.CurrentLevel && s.Status == ApprovalStatus.Pending);
        if (currentStep is null)
            return false;

        // PLAN-44: as ApprovalWorkflowService judges it.
        if (adminBypass && user.IsInRole(HrManagementRoles.Admin))
            return true;

        return user.IsInRole(currentStep.RoleRequired);
    }

    private async Task<Dictionary<(Guid DefinitionId, int Version, Guid TenantId), WorkflowDefinition>> LoadDefinitionsAsync(
        List<(Guid DefinitionId, int Version, Guid TenantId)> keys,
        CancellationToken ct)
    {
        var definitionIds = keys.Select(k => k.DefinitionId).Distinct().ToList();
        // PLAN-48: the national read path (the session tenant's or the national definitions).
        var rows = await db.WorkflowDefinitions
            .OwnOrNational(db)
            .AsNoTracking()
            .Where(d => definitionIds.Contains(d.Id))
            .ToListAsync(ct);

        var map = new Dictionary<(Guid, int, Guid), WorkflowDefinition>();
        foreach (var instanceKey in keys)
        {
            var match = rows.FirstOrDefault(d =>
                d.Id == instanceKey.DefinitionId
                && d.Version == instanceKey.Version
                && (d.TenantId == instanceKey.TenantId || d.TenantId == TenantDefaults.DefaultTenantId));
            if (match is not null)
                map[instanceKey] = match;
        }

        return map;
    }

    private async Task<Dictionary<Guid, WorkflowInboxEntityContext>> LoadEntityContextsAsync(
        IReadOnlyList<WorkflowInstance> instances,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, WorkflowInboxEntityContext>();
        var byType = instances.GroupBy(i => i.EntityType);

        foreach (var group in byType)
        {
            // PLAN-45: the entity lives in its owning service, so the instance is the item. The five local types keep PLAN-44's linkage rule.
            if (ExternalApprovalTypes.IsRegistered(group.Key))
            {
                foreach (var instance in group)
                {
                    result[instance.Id] = new WorkflowInboxEntityContext
                    {
                        ScopeOrgUnitId = instance.ScopeOrgUnitId,
                        EntityMakers = eligibility.MakersOf(null, instance),
                        IsAwaitingApproval = instance.Status == WorkflowStatus.InProgress
                    };
                }

                continue;
            }

            var entityIds = group.Select(g => g.EntityId).Distinct().ToList();
            switch (group.Key)
            {
                case "RecruitmentRequisition":
                    {
                        var rows = await db.RecruitmentRequisitions.AsNoTracking()
                            .Include(r => r.FundedPosition)
                            .Where(r => entityIds.Contains(r.Id))
                            .ToDictionaryAsync(r => r.Id, ct);
                        foreach (var instance in group)
                        {
                            rows.TryGetValue(instance.EntityId, out var req);
                            result[instance.Id] = new WorkflowInboxEntityContext
                            {
                                ScopeOrgUnitId = req?.FundedPosition?.OrgUnitId,
                                EntityMakers = eligibility.MakersOf(req?.CreatedBy, instance),
                                IsAwaitingApproval = req?.Status == RequisitionStatus.PendingApproval && req.WorkflowInstanceId == instance.Id
                            };
                        }

                        break;
                    }
                case "OfferLetter":
                    {
                        var rows = await db.OfferLetters.AsNoTracking()
                            .Include(o => o.FundedPosition)
                            .Where(o => entityIds.Contains(o.Id))
                            .ToDictionaryAsync(o => o.Id, ct);
                        foreach (var instance in group)
                        {
                            rows.TryGetValue(instance.EntityId, out var offer);
                            result[instance.Id] = new WorkflowInboxEntityContext
                            {
                                ScopeOrgUnitId = offer?.FundedPosition?.OrgUnitId,
                                EntityMakers = eligibility.MakersOf(offer?.CreatedBy, instance),
                                IsAwaitingApproval = offer?.Status == OfferStatus.PendingApproval && offer.WorkflowInstanceId == instance.Id
                            };
                        }

                        break;
                    }
                case "SelectionCommitteeSession":
                    {
                        var rows = await db.SelectionCommitteeSessions.AsNoTracking()
                            .Include(s => s.Members)
                            .Include(s => s.Requisition!)
                                .ThenInclude(r => r.FundedPosition)
                            .Where(s => entityIds.Contains(s.Id))
                            .ToDictionaryAsync(s => s.Id, ct);
                        foreach (var instance in group)
                        {
                            rows.TryGetValue(instance.EntityId, out var session);
                            result[instance.Id] = new WorkflowInboxEntityContext
                            {
                                ScopeOrgUnitId = session?.Requisition?.FundedPosition?.OrgUnitId,
                                EntityMakers = WorkflowMakerRules.Makers(session?.CreatedBy, null, null),
                                CommitteeCreatedBy = session?.CreatedBy,
                                CommitteeMembers = session?.Members.ToList(),
                                IsAwaitingApproval = session is { ApprovedAt: null } && session.WorkflowInstanceId == instance.Id
                            };
                        }

                        break;
                    }
                case "RecruitmentAppeal":
                    {
                        var rows = await db.RecruitmentAppeals.AsNoTracking()
                            .Include(a => a.RecruitmentApplication!)
                                .ThenInclude(app => app.Requisition!)
                                    .ThenInclude(r => r.FundedPosition)
                            .Where(a => entityIds.Contains(a.Id))
                            .ToDictionaryAsync(a => a.Id, ct);
                        foreach (var instance in group)
                        {
                            rows.TryGetValue(instance.EntityId, out var appeal);
                            result[instance.Id] = new WorkflowInboxEntityContext
                            {
                                ScopeOrgUnitId = appeal?.RecruitmentApplication?.Requisition?.FundedPosition?.OrgUnitId,
                                Appeal = appeal,
                                IsAwaitingApproval = appeal?.Status == RecruitmentAppealStatus.Pending && appeal.WorkflowInstanceId == instance.Id
                            };
                        }

                        break;
                    }
                case "EstablishmentChangeRequest":
                    {
                        var rows = await db.EstablishmentChangeRequests.AsNoTracking()
                            .Where(r => entityIds.Contains(r.Id))
                            .ToDictionaryAsync(r => r.Id, ct);
                        foreach (var instance in group)
                        {
                            rows.TryGetValue(instance.EntityId, out var change);
                            result[instance.Id] = new WorkflowInboxEntityContext
                            {
                                EntityMakers = eligibility.MakersOf(change?.CreatedBy, instance),
                                IsAwaitingApproval = change?.Status == EstablishmentChangeStatus.PendingApproval && change.WorkflowInstanceId == instance.Id
                            };
                        }

                        break;
                    }
            }
        }

        return result;
    }
}
