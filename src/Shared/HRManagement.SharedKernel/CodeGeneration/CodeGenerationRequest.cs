namespace HRManagement.SharedKernel.CodeGeneration;

public sealed class CodeGenerationRequest
{
    public CodeEntityKind EntityKind { get; set; }
    public Guid? TenantId { get; set; }
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Preview { get; set; }
    public string? PolicyKey { get; set; }
}
