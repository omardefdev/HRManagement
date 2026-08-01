namespace HRManagement.Integration.Abstractions;

/// <summary>نتيجة موحّدة لعمليات الموصّلات الخارجية.</summary>
public sealed record IntegrationResult(
    bool Success,
    string? Message = null,
    string? ExternalReference = null,
    string? OutputFilePath = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static IntegrationResult Ok(string? message = null, string? externalReference = null, string? outputFilePath = null) =>
        new(true, message, externalReference, outputFilePath);

    public static IntegrationResult Fail(string message) =>
        new(false, message);
}
