namespace HRManagement.ServiceDefaults.ProdSemantic;

public interface IProdSemanticProvider
{
    ProdSemanticOptions Options { get; }
    ProdSemanticMatrixDocument Matrix { get; }
}

public interface ISemanticLayerClaimGuard
{
    string? EnsureLiveSemanticAllowed();
    string? ValidateUnifiedKpiDefinition(bool definitionPresent);
    string? ValidateReconciliation(bool reconciledWithSource);
    string? ValidateTenantRls(bool withinTenant);
    string? ValidatePrivacyThreshold(bool smallCellSuppressed, bool reidentificationAttempted);
    string? ValidateReportProvenance(bool hasVersion, bool hasAsOf, bool hasSource);
    string? ValidateFreshness(bool withinSla);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ProdSemanticSigning? meta);
}
