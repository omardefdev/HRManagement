namespace HRManagement.SharedKernel.MasterData;

/// <summary>MDM-001: discriminator for <see cref="ReferenceCatalogEntry"/> rows.</summary>
public enum CatalogKind
{
    JobClassification = 1,
    Grade = 2,
    Geography = 3,
    Agency = 4,
    Qualification = 5,
    Bank = 6,
    Currency = 7,
    SalaryComponent = 8,
    DocumentType = 9,
    ContractType = 10,
    MovementType = 11,
    ServiceType = 12
}
