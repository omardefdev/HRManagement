using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Application.Services;

/// <summary>
/// PLAN-60 (§2, D-SEP-4): which of HR.Core's two separation chains a record's type takes. A type listed in
/// <c>Separation:Approval:SensitiveTypes</c> (default Disciplinary, Judicial) takes the sensitive chain, with compliance in it.
/// </summary>
public static class SeparationApprovalRouting
{
    public const string EntityType = "SeparationRecord";
    public const string StandardCode = "SEPARATION-APPROVAL";
    public const string SensitiveCode = "SEPARATION-APPROVAL-SENSITIVE";

    public static IReadOnlyCollection<SeparationType> SensitiveTypes(SeparationApprovalOptions options)
    {
        if (options.SensitiveTypes is null)
            return SeparationApprovalOptions.DefaultSensitiveTypes.ToHashSet();

        // A name that is not a type is ignored rather than guessed; the start-up keeps running.
        return options.SensitiveTypes
            .Select(name => Enum.TryParse<SeparationType>(name?.Trim(), ignoreCase: true, out var type) ? (SeparationType?)type : null)
            .Where(type => type is not null)
            .Select(type => type!.Value)
            .ToHashSet();
    }

    public static string CodeFor(SeparationType type, SeparationApprovalOptions options) =>
        SensitiveTypes(options).Contains(type) ? SensitiveCode : StandardCode;
}
