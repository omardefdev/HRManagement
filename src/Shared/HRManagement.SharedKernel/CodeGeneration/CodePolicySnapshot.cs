namespace HRManagement.SharedKernel.CodeGeneration;

public sealed class CodePolicySnapshot
{
    public Guid VersionId { get; set; }
    public CodeEntityKind EntityKind { get; set; }
    public IReadOnlyList<CodeSegmentDefinition> Segments { get; set; } = [];
    public CodeSequenceReset SequenceReset { get; set; } = CodeSequenceReset.Never;
    public string FallbackPrefix { get; set; } = "GEN";
}
