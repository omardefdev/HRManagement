using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults.Delegation;

/// <summary>
/// PLAN-50 (D-WF-5, D-50-1, D-50-5): the <c>Delegation</c> section. <c>DelegableRoles</c>: the roles a delegation may carry
/// (<c>hr-management-approver</c> only); <c>EntityTypes</c>: the closed list an entity scope may name; <c>CacheSeconds</c>: how long a
/// service other than HR.Core keeps a user's answer (a revocation, or a new delegation, acts there within it). The defaults apply when a
/// key is absent or empty, so a missing section never widens anything.
/// </summary>
public static class DelegationOptions
{
    public const string SectionName = "Delegation";
    public const string DelegableRolesKey = "Delegation:DelegableRoles";
    public const string EntityTypesKey = "Delegation:EntityTypes";
    public const string CacheSecondsKey = "Delegation:CacheSeconds";
    public const int DefaultCacheSeconds = 60;

    public static readonly IReadOnlyList<string> DefaultDelegableRoles = [HrManagementRoles.HrManagementApprover];

    /// <summary>The types with a step or an approval of <c>hr-management-approver</c>: HR.Core's workflow types and the five movement types.</summary>
    public static readonly IReadOnlyList<string> DefaultEntityTypes =
    [
        "RecruitmentRequisition",
        "OfferLetter",
        "SelectionCommitteeSession",
        "EstablishmentChangeRequest",
        "RecruitmentCompetition",
        "CompetitionResultVersion",
        "AssetAssignment",
        "AssetLossReport",
        "DisciplinaryAction",
        "TransferRequest",
        "SecondmentRequest",
        "LoanAssignmentRequest",
        "PromotionRequest",
        "DelegationRequest"
    ];

    public static IReadOnlyList<string> DelegableRoles(IConfiguration? configuration) =>
        Read(configuration, DelegableRolesKey, DefaultDelegableRoles);

    public static IReadOnlyList<string> EntityTypes(IConfiguration? configuration) =>
        Read(configuration, EntityTypesKey, DefaultEntityTypes);

    /// <summary>Between 0 (no cache) and 300 seconds; 60 when absent or not a number.</summary>
    public static TimeSpan CacheDuration(IConfiguration? configuration)
    {
        var raw = configuration?[CacheSecondsKey];
        var seconds = int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 300)
            : DefaultCacheSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    private static IReadOnlyList<string> Read(IConfiguration? configuration, string key, IReadOnlyList<string> defaults)
    {
        var configured = configuration?.GetSection(key).Get<string[]>()?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return configured is { Count: > 0 } ? configured : defaults;
    }
}
