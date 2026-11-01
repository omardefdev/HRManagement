using HRManagement.SharedKernel.CodeGeneration;

namespace HRManagement.ServiceDefaults.CodeGeneration;

/// <summary>
/// PLAN-37 (§5, G-COD-06): HR.Core could not issue a required number (unreachable, refused, or answered without a code).
/// A transient fault: it derives directly from <see cref="Exception"/>, not from <see cref="InvalidOperationException"/>
/// or <see cref="ArgumentException"/>, which MassTransit's retry policy ignores (MassTransitExtensions), so a consumer
/// that needs the number retries and then moves the message to its <c>_error</c> queue instead of acknowledging it.
/// </summary>
public sealed class CodeGenerationUnavailableException : Exception
{
    public CodeGenerationUnavailableException(CodeEntityKind entityKind, int? statusCode, string reason, Exception? inner = null)
        : base($"Code generation for {entityKind} is unavailable: {reason}", inner)
    {
        EntityKind = entityKind;
        StatusCode = statusCode;
    }

    public CodeEntityKind EntityKind { get; }

    /// <summary>The HTTP status HR.Core answered, or null when there was no response.</summary>
    public int? StatusCode { get; }
}
