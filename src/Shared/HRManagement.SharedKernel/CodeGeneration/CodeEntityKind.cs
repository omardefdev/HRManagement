namespace HRManagement.SharedKernel.CodeGeneration;

public enum CodeEntityKind
{
    OrgUnit,
    FundedPosition,
    AssetTag,
    ItAssetTag,
    ArchiveReference,
    LegalCase,
    ServiceTransaction,
    IdCardSerial,
    CitizenReference,
    ForensicCase,
    CostCenter,
    InventorySku,
    RecruitmentRequisition,
    OfferLetter,
    EstablishmentChangeRequest,
    JobCatalogEntry,
    /// <summary>FUN-029 (I3): administrative decision numbers (appointment decisions first).</summary>
    AdministrativeDecision
}
