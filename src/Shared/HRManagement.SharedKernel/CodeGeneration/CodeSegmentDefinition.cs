namespace HRManagement.SharedKernel.CodeGeneration;

public sealed class CodeSegmentDefinition
{
    public CodeSegmentKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;
    public int PadWidth { get; set; }
    public string[]? ScopeFields { get; set; }
}
