using HRManagement.SharedKernel.CodeGeneration;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

public interface IJobCatalogCodeService
{
    Task<string> GenerateCodeAsync(
        string gradeCode,
        string? jobClassificationCode,
        CancellationToken ct = default);
}

public sealed class JobCatalogCodeService(ICodeGenerator generator) : IJobCatalogCodeService
{
    public async Task<string> GenerateCodeAsync(
        string gradeCode,
        string? jobClassificationCode,
        CancellationToken ct = default)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GradeCode"] = gradeCode.Trim(),
            ["JobClassificationCode"] = string.IsNullOrWhiteSpace(jobClassificationCode)
                ? "GEN"
                : jobClassificationCode.Trim()
        };

        var result = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.JobCatalogEntry,
            Facts = facts
        }, ct);

        return result.Code;
    }
}
