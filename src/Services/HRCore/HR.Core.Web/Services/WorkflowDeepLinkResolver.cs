using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

public sealed record WorkflowDeepLinkInfo(
    string DeepLink,
    string Title,
    string? Description,
    string EntityTypeLabel,
    string? CurrentStepName,
    string? WorkflowName);

public sealed class WorkflowDeepLinkResolver(HRCoreDbContext db)
{
    public async Task<IReadOnlyDictionary<Guid, WorkflowDeepLinkInfo>> ResolveAsync(
        IReadOnlyList<(WorkflowInstance Instance, WorkflowDefinition Definition)> pairs,
        CancellationToken ct = default)
    {
        if (pairs.Count == 0)
            return new Dictionary<Guid, WorkflowDeepLinkInfo>();

        var byType = pairs.GroupBy(p => p.Instance.EntityType).ToList();
        var result = new Dictionary<Guid, WorkflowDeepLinkInfo>();

        foreach (var group in byType)
        {
            var ids = group.Select(g => g.Instance.EntityId).Distinct().ToList();
            switch (group.Key)
            {
                case "RecruitmentRequisition":
                    await ResolveRequisitionsAsync(group, ids, result, ct);
                    break;
                case "OfferLetter":
                    await ResolveOffersAsync(group, ids, result, ct);
                    break;
                case "SelectionCommitteeSession":
                    await ResolveCommitteesAsync(group, ids, result, ct);
                    break;
                case "RecruitmentAppeal":
                    await ResolveAppealsAsync(group, ids, result, ct);
                    break;
                case "EstablishmentChangeRequest":
                    await ResolveEstablishmentChangesAsync(group, ids, result, ct);
                    break;
                default:
                    foreach (var (instance, definition) in group)
                        result[instance.Id] = BuildFallback(instance, definition);
                    break;
            }
        }

        return result;
    }

    private async Task ResolveRequisitionsAsync(
        IEnumerable<(WorkflowInstance Instance, WorkflowDefinition Definition)> pairs,
        List<Guid> ids,
        Dictionary<Guid, WorkflowDeepLinkInfo> result,
        CancellationToken ct)
    {
        var rows = await db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.RequisitionNumber, r.JobTitle })
            .ToDictionaryAsync(r => r.Id, ct);

        foreach (var (instance, definition) in pairs)
        {
            rows.TryGetValue(instance.EntityId, out var row);
            var stepName = GetCurrentStepName(instance, definition);
            result[instance.Id] = new WorkflowDeepLinkInfo(
                DeepLink: $"/RecruitmentRequisitions/Details/{instance.EntityId}",
                Title: row is null
                    ? $"اعتماد طلب شاغر — {instance.EntityId:N}"
                    : $"اعتماد طلب شاغر {row.RequisitionNumber} — {row.JobTitle}",
                Description: row?.JobTitle,
                EntityTypeLabel: "طلب شاغر",
                CurrentStepName: stepName,
                WorkflowName: definition.Name);
        }
    }

    private async Task ResolveOffersAsync(
        IEnumerable<(WorkflowInstance Instance, WorkflowDefinition Definition)> pairs,
        List<Guid> ids,
        Dictionary<Guid, WorkflowDeepLinkInfo> result,
        CancellationToken ct)
    {
        var rows = await db.OfferLetters.AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.OfferNumber })
            .ToDictionaryAsync(o => o.Id, ct);

        foreach (var (instance, definition) in pairs)
        {
            rows.TryGetValue(instance.EntityId, out var row);
            var stepName = GetCurrentStepName(instance, definition);
            result[instance.Id] = new WorkflowDeepLinkInfo(
                DeepLink: $"/OfferLetters/Details/{instance.EntityId}",
                Title: row is null
                    ? $"اعتماد عرض — {instance.EntityId:N}"
                    : $"اعتماد عرض {row.OfferNumber}",
                Description: row?.OfferNumber,
                EntityTypeLabel: "خطاب عرض",
                CurrentStepName: stepName,
                WorkflowName: definition.Name);
        }
    }

    private async Task ResolveCommitteesAsync(
        IEnumerable<(WorkflowInstance Instance, WorkflowDefinition Definition)> pairs,
        List<Guid> ids,
        Dictionary<Guid, WorkflowDeepLinkInfo> result,
        CancellationToken ct)
    {
        var rows = await db.SelectionCommitteeSessions.AsNoTracking()
            .Include(s => s.Requisition)
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        foreach (var (instance, definition) in pairs)
        {
            rows.TryGetValue(instance.EntityId, out var session);
            var reqNumber = session?.Requisition?.RequisitionNumber;
            var stepName = GetCurrentStepName(instance, definition);
            result[instance.Id] = new WorkflowDeepLinkInfo(
                DeepLink: $"/SelectionCommittees/Details/{instance.EntityId}",
                Title: reqNumber is null
                    ? $"اعتماد لجنة اختيار — {instance.EntityId:N}"
                    : $"اعتماد لجنة — طلب {reqNumber}",
                Description: session?.Requisition?.JobTitle,
                EntityTypeLabel: "لجنة اختيار",
                CurrentStepName: stepName,
                WorkflowName: definition.Name);
        }
    }

    private async Task ResolveAppealsAsync(
        IEnumerable<(WorkflowInstance Instance, WorkflowDefinition Definition)> pairs,
        List<Guid> ids,
        Dictionary<Guid, WorkflowDeepLinkInfo> result,
        CancellationToken ct)
    {
        var rows = await db.RecruitmentAppeals.AsNoTracking()
            .Include(a => a.RecruitmentApplication)
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var (instance, definition) in pairs)
        {
            rows.TryGetValue(instance.EntityId, out var appeal);
            var applicant = appeal?.RecruitmentApplication?.ApplicantFullName;
            var stepName = GetCurrentStepName(instance, definition);
            result[instance.Id] = new WorkflowDeepLinkInfo(
                // HCM-028 (F4): appeals are decided on their own page, which the compliance reviewer can open.
                DeepLink: $"/RecruitmentAppeals/Details/{instance.EntityId}",
                Title: applicant is null
                    ? "مراجعة استئناف"
                    : $"مراجعة استئناف — {applicant}",
                Description: appeal?.Reason,
                EntityTypeLabel: "استئناف",
                CurrentStepName: stepName,
                WorkflowName: definition.Name);
        }
    }

    private async Task ResolveEstablishmentChangesAsync(
        IEnumerable<(WorkflowInstance Instance, WorkflowDefinition Definition)> pairs,
        List<Guid> ids,
        Dictionary<Guid, WorkflowDeepLinkInfo> result,
        CancellationToken ct)
    {
        var rows = await db.EstablishmentChangeRequests.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.RequestNumber, r.ChangeType })
            .ToDictionaryAsync(r => r.Id, ct);

        foreach (var (instance, definition) in pairs)
        {
            rows.TryGetValue(instance.EntityId, out var row);
            var stepName = GetCurrentStepName(instance, definition);
            result[instance.Id] = new WorkflowDeepLinkInfo(
                DeepLink: $"/EstablishmentChanges/Details/{instance.EntityId}",
                Title: row is null
                    ? $"اعتماد تغيير ملاك — {instance.EntityId:N}"
                    : $"اعتماد {row.RequestNumber} — {row.ChangeType}",
                Description: row?.ChangeType.ToString(),
                EntityTypeLabel: "تغيير ملاك",
                CurrentStepName: stepName,
                WorkflowName: definition.Name);
        }
    }

    private static WorkflowDeepLinkInfo BuildFallback(WorkflowInstance instance, WorkflowDefinition definition)
    {
        var stepName = GetCurrentStepName(instance, definition);
        return new WorkflowDeepLinkInfo(
            DeepLink: $"/WorkflowInstances/Details/{instance.Id}",
            Title: $"{definition.Name} — {instance.EntityType}",
            Description: instance.EntityId.ToString(),
            EntityTypeLabel: instance.EntityType,
            CurrentStepName: stepName,
            WorkflowName: definition.Name);
    }

    private static string? GetCurrentStepName(WorkflowInstance instance, WorkflowDefinition definition)
    {
        var steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(definition.StepsJson) ?? [];
        return steps.FirstOrDefault(s => s.Level == instance.CurrentStepLevel)?.Name;
    }
}
