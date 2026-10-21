namespace HRManagement.SharedKernel.CodeGeneration;

public interface ICodeGenerator
{
    Task<CodeGenerationResult> GenerateAsync(CodeGenerationRequest request, CancellationToken ct = default);
    Task<CodePolicySnapshot?> GetPublishedSnapshotAsync(CodeEntityKind entityKind, Guid? tenantId, CancellationToken ct = default);
}
