namespace HRManagement.ServiceDefaults.ProdEss;

public interface IProdEssProvider
{
    ProdEssOptions Options { get; }
    ProdEssMatrixDocument Matrix { get; }
}

public interface IEmployeeSelfServiceClaimGuard
{
    string? EnsureLiveEssAllowed();
    string? ValidateHighVolumeJourney(bool completedWithoutBackOffice, bool isException);
    string? ValidateCorrectionVisibility(bool sourceVisible, bool statusVisible);
    string? ValidateManagerScope(bool withinScope);
    string? ValidateUsageMeasured(bool telemetryPresent);
    string? ValidateLostSessionHandled(bool sessionLost, bool recoveredSafely);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ProdEssSigning? meta);
}
