using HRManagement.SharedKernel.CodeGeneration;

namespace HRManagement.ServiceDefaults.CodeGeneration;

public static class CodeGenerationRequests
{
    public static CodeGenerationRequest ArchiveReference(string entityPrefix) => new()
    {
        EntityKind = CodeEntityKind.ArchiveReference,
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EntityPrefix"] = entityPrefix
        }
    };

    public static CodeGenerationRequest ServiceTransaction(string typeCode) => new()
    {
        EntityKind = CodeEntityKind.ServiceTransaction,
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TypeCode"] = typeCode
        }
    };

    public static CodeGenerationRequest LegalCase() => new()
    {
        EntityKind = CodeEntityKind.LegalCase
    };

    public static CodeGenerationRequest CitizenReference() => new()
    {
        EntityKind = CodeEntityKind.CitizenReference
    };

    public static CodeGenerationRequest IdCardSerial() => new()
    {
        EntityKind = CodeEntityKind.IdCardSerial
    };

    public static CodeGenerationRequest AssetTag(string? policyKey = null) => new()
    {
        EntityKind = CodeEntityKind.AssetTag,
        PolicyKey = policyKey
    };

    public static CodeGenerationRequest ItAssetTag() => new()
    {
        EntityKind = CodeEntityKind.ItAssetTag
    };

    public static CodeGenerationRequest InventorySku() => new()
    {
        EntityKind = CodeEntityKind.InventorySku
    };

    public static CodeGenerationRequest RecruitmentRequisition(int budgetYear) => new()
    {
        EntityKind = CodeEntityKind.RecruitmentRequisition,
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BudgetYear"] = budgetYear.ToString()
        }
    };

    public static CodeGenerationRequest OfferLetter() => new()
    {
        EntityKind = CodeEntityKind.OfferLetter
    };

    public static CodeGenerationRequest EstablishmentChangeRequest() => new()
    {
        EntityKind = CodeEntityKind.EstablishmentChangeRequest
    };
}
