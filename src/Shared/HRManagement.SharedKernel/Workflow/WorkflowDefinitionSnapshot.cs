namespace HRManagement.SharedKernel.Workflow;

/// <summary>
/// A read-only snapshot of a workflow definition, used for engine evaluation.
/// Decouples engine logic from the EF entity.
/// </summary>
public sealed record WorkflowDefinitionSnapshot
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public string DomainEntityType { get; init; } = string.Empty;
    public IReadOnlyList<WorkflowStepSnapshot> Steps { get; init; } = [];
    public IReadOnlyList<string> GuardrailRoles { get; init; } = [];
}
