namespace HRManagement.SharedKernel.CodeGeneration;

public interface ICodeGenerator
{
    Task<CodeGenerationResult> GenerateAsync(CodeGenerationRequest request, CancellationToken ct = default);

    /// <summary>PLAN-42: as <see cref="GenerateAsync"/>, for the internal endpoint: the issuance records the client that asked.</summary>
    Task<CodeGenerationResult> GenerateForServiceAsync(CodeGenerationRequest request, string callerClientId, CancellationToken ct = default);

    /// <summary>PLAN-48: the published policy for the kind and policy key (none = the kind's default template), as a tenant resolves it.</summary>
    Task<CodePolicySnapshot?> GetPublishedSnapshotAsync(CodeEntityKind entityKind, Guid? tenantId, CancellationToken ct = default, string? policyKey = null);
}
