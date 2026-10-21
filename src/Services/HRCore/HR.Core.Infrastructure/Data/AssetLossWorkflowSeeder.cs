using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

public static class AssetLossWorkflowSeeder
{
    private static readonly Guid DefinitionId = Guid.Parse("f0050002-0002-4000-8000-000000000002");

    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        if (await db.WorkflowDefinitions.AnyAsync(d => d.DomainEntityType == "AssetLossReport", ct))
            return;

        var steps = new[]
        {
            new WorkflowStepSnapshot
            {
                Level = 1,
                Name = "موافقة فقدان أصل",
                StepType = WorkflowStepType.Approval,
                RoleRequired = "admin-logistics"
            }
        };

        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = DefinitionId,
            Code = "ASSET-LOSS-APPROVAL",
            Name = "موافقة فقدان أصل",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "AssetLossReport",
            StepsJson = JsonSerializer.Serialize(steps),
            GuardrailRolesJson = "[]",
            Description = "FUN-005: approval before marking privileged/high-value assets as lost"
        });

        await db.SaveChangesAsync(ct);
    }
}
