using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>PLAN-03: multi-step ASSET-ASSIGN-APPROVAL for AC-11 (upgrade from single-step v1).</summary>
public static class WorkflowPlan03Seeder
{
    private static readonly Guid V2DefinitionId = Guid.Parse("f0050001-0001-4000-8000-000000000011");

    public static async Task EnsureMultiStepAssetAssignmentAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        const string code = "ASSET-ASSIGN-APPROVAL";

        var published = await db.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .Where(d => d.TenantId == TenantDefaults.DefaultTenantId && d.Code == code && d.IsPublished)
            .OrderByDescending(d => d.Version)
            .ToListAsync(ct);

        if (published.Any(d => CountSteps(d) >= 2))
            return;

        var nextVersion = published.Count == 0 ? 1 : published.Max(d => d.Version) + 1;
        var steps = new[]
        {
            new WorkflowStepSnapshot
            {
                Level = 1,
                Name = "موافقة لوجستية",
                StepType = WorkflowStepType.Approval,
                RoleRequired = "hr-core"
            },
            new WorkflowStepSnapshot
            {
                Level = 2,
                Name = "موافقة إدارية",
                StepType = WorkflowStepType.Approval,
                RoleRequired = "hr-management-approver"
            }
        };

        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = nextVersion == 1 ? Guid.Parse("f0050001-0001-4000-8000-000000000001") : V2DefinitionId,
            TenantId = TenantDefaults.DefaultTenantId,
            Code = code,
            Name = "موافقة تسليم عهدة",
            Version = nextVersion,
            IsPublished = true,
            DomainEntityType = "AssetAssignment",
            StepsJson = JsonSerializer.Serialize(steps),
            GuardrailRolesJson = "[]",
            Description = "PLAN-03: two-step asset assignment approval (AC-11)"
        });

        await db.SaveChangesAsync(ct);
    }

    private static int CountSteps(WorkflowDefinition definition)
    {
        var steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(definition.StepsJson) ?? [];
        return steps.Count;
    }
}
