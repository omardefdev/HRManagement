using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;

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

    /// <summary>
    /// PLAN-48: a citizen reference is one national sequence, issued in the national tenant whatever the posting's agency:
    /// <c>CitizenApplications.ReferenceNumber</c> is unique across tenants and the portal finds an application by it alone, so a
    /// counter per agency would issue the same reference twice.
    /// </summary>
    public static CodeGenerationRequest CitizenReference() => new()
    {
        EntityKind = CodeEntityKind.CitizenReference,
        TenantId = NationalTenant.Id
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
            ["BudgetYear"] = budgetYear.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }
    };

    public static CodeGenerationRequest OfferLetter() => new()
    {
        EntityKind = CodeEntityKind.OfferLetter
    };

    /// <summary>HCM-031 (PLAN-31): competition number CMP-YYYY-00001.</summary>
    public static CodeGenerationRequest RecruitmentCompetition() => new()
    {
        EntityKind = CodeEntityKind.RecruitmentCompetition
    };

    /// <summary>FUN-029 (I3): decision number, scoped by decision type (APPT = appointment).</summary>
    public static CodeGenerationRequest AdministrativeDecision(string typeCode) => new()
    {
        EntityKind = CodeEntityKind.AdministrativeDecision,
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TypeCode"] = typeCode
        }
    };

    public static CodeGenerationRequest EstablishmentChangeRequest() => new()
    {
        EntityKind = CodeEntityKind.EstablishmentChangeRequest
    };
}
