namespace HRManagement.Integration.Abstractions;

/// <summary>نتيجة موحّدة مع حمولة typed.</summary>
public sealed record IntegrationResult<T>(
    bool Success,
    T? Data = default,
    string? Message = null,
    string? ExternalReference = null,
    string? OutputFilePath = null)
{
    public static IntegrationResult<T> Ok(T data, string? message = null, string? externalReference = null) =>
        new(true, data, message, externalReference);

    public static IntegrationResult<T> Fail(string message) =>
        new(false, default, message);
}
