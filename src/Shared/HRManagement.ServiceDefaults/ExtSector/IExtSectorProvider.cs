namespace HRManagement.ServiceDefaults.ExtSector;

public interface IExtSectorProvider
{
    ExtSectorOptions Options { get; }
    ExtSectorMatrixDocument Matrix { get; }
}

public interface ISectorCatalogClaimGuard
{
    string? EnsureLiveCatalogAllowed();
    string? ValidateNoImplicitCritical(bool isDeclaredCritical, bool isInCatalog);
    string? ValidateAdapterCompleteness(bool hasContract, bool hasSlo, bool hasRetry, bool hasRecon, bool hasExit);
    string? ValidateNotificationNoSensitivePii(bool containsSensitivePii);
    string? ValidateFailureSurfaced(bool failureOccurred, bool wasSwallowed);
    string? ValidateCertification(bool enabled, bool certified);
    string? ValidateReconciliation(bool required, bool completed);
    string? ValidateDeliveryReceipt(bool applicable, bool hasReceipt);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ExtSectorSigning? meta);
}
