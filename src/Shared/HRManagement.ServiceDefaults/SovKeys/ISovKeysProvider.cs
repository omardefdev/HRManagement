namespace HRManagement.ServiceDefaults.SovKeys;

public interface ISovKeysProvider
{
    SovKeysOptions Options { get; }
    SovKeysMatrixDocument Matrix { get; }
}

public interface IKeyCustodyClaimGuard
{
    string? EnsureLiveKeyCustodyAllowed();
    string? ValidateVendorAloneCannotDecrypt(bool vendorAlone);
    string? ValidateHierarchy(bool inventoryPresent);
    string? ValidateDualControl(bool dualControlPresent);
    string? ValidateRotation(bool rotationDrillPresent);
    string? ValidateAlgorithmAgility(bool deprecatedAlgClaimedInProd);
    string? ValidateTrustedTime(bool clockDriftBoundPresent);
    string? ValidateSoftKeyProdClaim(bool softKeyClaimedAsProd);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(SovKeysSigning? meta);
}
