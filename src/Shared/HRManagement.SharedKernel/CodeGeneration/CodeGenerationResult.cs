namespace HRManagement.SharedKernel.CodeGeneration;

public sealed class CodeGenerationResult
{
    public string Code { get; set; } = string.Empty;
    public Guid? PolicyVersionId { get; set; }
    public string SequenceScope { get; set; } = string.Empty;
    public bool IsPreview { get; set; }
}
