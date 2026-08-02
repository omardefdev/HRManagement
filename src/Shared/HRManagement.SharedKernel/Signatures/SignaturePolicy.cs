namespace HRManagement.SharedKernel.Signatures;

/// <summary>
/// Defines which subject types require legal signatures and the minimum signer requirements (SIG-001).
/// </summary>
public static class SignaturePolicy
{
    public static readonly IReadOnlyDictionary<string, PolicyEntry> RequiredSignatures =
        new Dictionary<string, PolicyEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["Contract"] = new("Contract", MinSigners: 2, RequiresOrganizationalSeal: true),
            ["Decision"] = new("Decision", MinSigners: 1, RequiresOrganizationalSeal: true),
            ["Payroll"] = new("Payroll", MinSigners: 2, RequiresOrganizationalSeal: false),
            ["Promotion"] = new("Promotion", MinSigners: 1, RequiresOrganizationalSeal: true),
            ["Transfer"] = new("Transfer", MinSigners: 1, RequiresOrganizationalSeal: true),
        };

    public static bool IsLegalSignatureRequired(string subjectType) =>
        RequiredSignatures.ContainsKey(subjectType);

    public static PolicyEntry? GetPolicy(string subjectType) =>
        RequiredSignatures.TryGetValue(subjectType, out var entry) ? entry : null;

    public sealed record PolicyEntry(
        string SubjectType,
        int MinSigners,
        bool RequiresOrganizationalSeal);
}
