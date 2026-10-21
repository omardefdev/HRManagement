using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Infrastructure.Services.Workflow;

public static class WorkflowDefinitionMapper
{
    public static WorkflowDefinitionSnapshot ToSnapshot(WorkflowDefinition def) => new()
    {
        Id = def.Id,
        Code = def.Code,
        Name = def.Name,
        Version = def.Version,
        DomainEntityType = def.DomainEntityType,
        Steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(def.StepsJson) ?? [],
        GuardrailRoles = JsonSerializer.Deserialize<List<string>>(def.GuardrailRolesJson) ?? []
    };

    public static WorkflowInstanceState ToState(WorkflowInstance instance) => new()
    {
        Id = instance.Id,
        CurrentStepLevel = instance.CurrentStepLevel,
        Status = instance.Status,
        StepHistory = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [],
        ContextJson = instance.ContextJson
    };
}
