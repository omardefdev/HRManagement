using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
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
    OrgScopeService orgScope)
{
    private static readonly string[] SupportedEntityTypes =
    [
        "RecruitmentRequisition",
        "OfferLetter",
        "SelectionCommitteeSession",
        "RecruitmentAppeal",
        "EstablishmentChangeRequest"
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

        // HCM-028 (F4): the compliance reviewer only sees the appeal reviews assigned to them.
        var entityTypes = RecruitmentAppealAccess.IsComplianceOnly(user) ? AppealOnlyEntityTypes : SupportedEntityTypes;
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

        var eligiblePairs = new List<(WorkflowInstance Instance, WorkflowDefinition Definition)>();
        foreach (var instance in instances)
        {
            if (!definitions.TryGetValue((instance.DefinitionId, instance.DefinitionVersion, instance.TenantId), out var definition))
                continue;

            if (!contexts.TryGetValue(instance.Id, out var context))
                continue;

            if (eligibility.IsActionable(instance, definition, user, userId, context, accessScope))
                eligiblePairs.Add((instance, definition));
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
                return WorkInboxItemMapper.FromWorkflow(pair.Instance, pair.Definition, link, userId.Value);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<WorkInboxItem>> GetActionableLegacyItemsAsync(
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        if (userId is null || RecruitmentAppealAccess.IsComplianceOnly(user))
            return [];

        var requests = await db.ApprovalRequests.AsNoTracking()
            .Include(r => r.Steps)
            .Where(r => r.Status == ApprovalStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);

        var items = new List<WorkInboxItem>();
        foreach (var request in requests)
        {
            if (!IsLegacyApprovalActionable(request, user, userId))
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
        Guid? userId)
    {
        if (userId.HasValue && request.RequestedBy == userId)
            return false;

        var currentStep = request.Steps
            .FirstOrDefault(s => s.Level == request.CurrentLevel && s.Status == ApprovalStatus.Pending);
        if (currentStep is null)
            return false;

        if (user.IsInRole(HrManagementRoles.Admin))
            return true;

        return user.IsInRole(currentStep.RoleRequired);
    }

    private async Task<Dictionary<(Guid DefinitionId, int Version, Guid TenantId), WorkflowDefinition>> LoadDefinitionsAsync(
        List<(Guid DefinitionId, int Version, Guid TenantId)> keys,
        CancellationToken ct)
    {
        var definitionIds = keys.Select(k => k.DefinitionId).Distinct().ToList();
        var rows = await db.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
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
                            EntityCreatedBy = req?.CreatedBy ?? instance.InitiatedBy,
                            IsAwaitingApproval = req?.Status == RequisitionStatus.PendingApproval
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
                            EntityCreatedBy = offer?.CreatedBy ?? instance.InitiatedBy,
                            IsAwaitingApproval = offer?.Status == OfferStatus.PendingApproval
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
                            EntityCreatedBy = session?.CreatedBy ?? instance.InitiatedBy,
                            CommitteeMembers = session?.Members.ToList(),
                            IsAwaitingApproval = session?.ApprovedAt is null
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
                            IsAwaitingApproval = appeal?.Status == RecruitmentAppealStatus.Pending
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
                            EntityCreatedBy = change?.CreatedBy ?? instance.InitiatedBy,
                            IsAwaitingApproval = change?.Status == EstablishmentChangeStatus.PendingApproval
                        };
                    }

                    break;
                }
            }
        }

        return result;
    }
}
