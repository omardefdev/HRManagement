using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.TestSupport.CodeGeneration;

/// <summary>PLAN-04: thread-safe fake code generation client for integration tests.</summary>
public sealed class FakeCodeGenerationClient : ICodeGenerationClient
{
    private int _sequence;

    public Func<CodeGenerationRequest, CodeGenerationResult>? GenerateHandler { get; set; }

    public IReadOnlyList<CodeGenerationRequest> Requests => _requests;
    private readonly List<CodeGenerationRequest> _requests = [];

    public Task<CodeGenerationResult?> GenerateAsync(CodeGenerationRequest request, CancellationToken ct = default)
    {
        _requests.Add(Clone(request));
        if (GenerateHandler is not null)
            return Task.FromResult<CodeGenerationResult?>(GenerateHandler(request));

        var n = Interlocked.Increment(ref _sequence);
        var code = request.EntityKind switch
        {
            CodeEntityKind.ServiceTransaction => $"SRV-TEST-{n:D5}",
            CodeEntityKind.AssetTag => $"AH-TEST-{n:D5}",
            CodeEntityKind.OrgUnit => $"OU-TEST-{n:D5}",
            _ => $"TEST-{n:D5}"
        };
        return Task.FromResult<CodeGenerationResult?>(new CodeGenerationResult { Code = code });
    }

    public Task<CodeGenerationResult?> PreviewAsync(CodeGenerationRequest request, CancellationToken ct = default)
    {
        var preview = Clone(request);
        preview.Preview = true;
        return GenerateAsync(preview, ct);
    }

    private static CodeGenerationRequest Clone(CodeGenerationRequest request) => new()
    {
        EntityKind = request.EntityKind,
        TenantId = request.TenantId,
        Facts = new Dictionary<string, string>(request.Facts, StringComparer.OrdinalIgnoreCase),
        Preview = request.Preview,
        PolicyKey = request.PolicyKey
    };
}

public static class FakeCodeGenerationClientExtensions
{
    public static IServiceCollection ReplaceCodeGenerationClientWithFake(this IServiceCollection services)
    {
        services.AddSingleton<FakeCodeGenerationClient>();
        services.AddSingleton<ICodeGenerationClient>(sp => sp.GetRequiredService<FakeCodeGenerationClient>());
        return services;
    }
}
