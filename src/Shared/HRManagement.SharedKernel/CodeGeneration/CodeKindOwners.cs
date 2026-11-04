namespace HRManagement.SharedKernel.CodeGeneration;

/// <summary>
/// PLAN-42 (D-S2S-3, G-COD-05): which service client may ask HR.Core for an official number of a kind. Every kind not listed here is
/// issued inside HR.Core only.
/// </summary>
public static class CodeKindOwners
{
    private static readonly IReadOnlyDictionary<CodeEntityKind, string> Owners = new Dictionary<CodeEntityKind, string>
    {
        [CodeEntityKind.AssetTag] = "svc-admin-logistics",
        [CodeEntityKind.InventorySku] = "svc-admin-logistics",
        [CodeEntityKind.ItAssetTag] = "svc-it-infra",
        [CodeEntityKind.ArchiveReference] = "svc-records-archive",
        [CodeEntityKind.LegalCase] = "svc-compliance",
        [CodeEntityKind.ServiceTransaction] = "svc-hr-contracts",
        [CodeEntityKind.AdministrativeDecision] = "svc-hr-contracts",
        [CodeEntityKind.IdCardSerial] = "svc-idcard"
    };

    public static bool Owns(string? clientId, CodeEntityKind kind) =>
        !string.IsNullOrWhiteSpace(clientId)
        && Owners.TryGetValue(kind, out var owner)
        && string.Equals(owner, clientId, StringComparison.Ordinal);

    public static bool IsOwnerOfAnyKind(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId)
        && Owners.Values.Any(owner => string.Equals(owner, clientId, StringComparison.Ordinal));
}
