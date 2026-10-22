using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

/// <summary>FUN-029 (E2): approved offer terms carried by ICandidateAccepted v4 onto the pending contract.</summary>
public sealed record ContractOfferTerms(
    string? GradeCode,
    decimal? AgreedBaseSalary,
    string? LegalRegimeId,
    string? EmploymentType,
    Guid? SourceOfferLetterId);

public static class PendingContractProvisioning
{
    public static Contract CreatePending(
        Guid employeeId,
        Guid contractTypeId,
        DateOnly startDate,
        string terms,
        ContractOfferTerms? offerTerms = null) =>
        new()
        {
            EmployeeId = employeeId,
            ContractTypeId = contractTypeId,
            StartDate = startDate,
            Status = ContractStatus.Pending,
            Terms = terms,
            GradeCode = Trim(offerTerms?.GradeCode),
            AgreedBaseSalary = offerTerms?.AgreedBaseSalary,
            LegalRegimeId = Trim(offerTerms?.LegalRegimeId),
            EmploymentType = Trim(offerTerms?.EmploymentType),
            SourceOfferLetterId = offerTerms?.SourceOfferLetterId
        };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// FUN-029: HR.Core <c>EmploymentType</c> names as they arrive on ICandidateAccepted. v4 sends the enum name in
/// <c>EmploymentType</c>; v3 messages only carry <c>EmploymentCategory</c> with the legacy codes.
/// </summary>
public static class HireEmploymentTypes
{
    public const string Permanent = "Permanent";
    public const string Administrative = "Administrative";
    public const string Temporary = "Temporary";

    public static IReadOnlyList<string> All { get; } = [Permanent, Administrative, Temporary];

    /// <summary>Canonical name, or null when unknown (INTERNSHIP and free text included).</summary>
    public static string? Normalize(string? employmentType, string? legacyCategory = null) =>
        Map(employmentType) ?? Map(legacyCategory);

    private static string? Map(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PERMANENT" => Permanent,
        "ADMINISTRATIVE" => Administrative,
        "TEMPORARY" or "CONTRACT" => Temporary,
        _ => null
    };
}
