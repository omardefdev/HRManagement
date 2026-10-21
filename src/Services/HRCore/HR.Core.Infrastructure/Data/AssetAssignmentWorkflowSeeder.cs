using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

public static class AssetAssignmentWorkflowSeeder
{
    private static readonly Guid DefinitionId = Guid.Parse("f0050001-0001-4000-8000-000000000001");

    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        if (await db.WorkflowDefinitions.AnyAsync(d => d.DomainEntityType == "AssetAssignment", ct))
            return;

        var steps = new[]
        {
            new WorkflowStepSnapshot
            {
                Level = 1,
                Name = "موافقة مدير الأصول",
                StepType = WorkflowStepType.Approval,
                RoleRequired = "admin-logistics"
            }
        };

        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = DefinitionId,
            Code = "ASSET-ASSIGN-APPROVAL",
            Name = "موافقة تسليم عهدة",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "AssetAssignment",
            StepsJson = JsonSerializer.Serialize(steps),
            GuardrailRolesJson = "[]",
            Description = "FUN-005-EXT: optional approval for privileged/high-value asset assignments"
        });

        await db.SaveChangesAsync(ct);
    }
}
